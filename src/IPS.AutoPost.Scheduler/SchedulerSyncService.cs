using System.Data;
using System.Text.Json;
using Amazon.Scheduler;
using Amazon.Scheduler.Model;
using IPS.AutoPost.Core.DataAccess;
using IPS.AutoPost.Scheduler.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace IPS.AutoPost.Scheduler;

/// <summary>
/// Reads all active schedule rows from <c>generic_execution_schedule</c> joined with
/// <c>generic_job_configuration</c> and synchronises the corresponding Amazon EventBridge
/// Scheduler rules so that schedule changes take effect within 10 minutes.
/// </summary>
/// <remarks>
/// <para>
/// Rule naming convention: <c>ips-autopost-{jobId}-{scheduleType}</c>
/// (e.g. <c>ips-autopost-1001-POST</c>, <c>ips-autopost-1001-DOWNLOAD</c>).
/// </para>
/// <para>
/// Target mapping:
/// <list type="bullet">
///   <item><c>schedule_type = 'POST'</c>     → <c>ips-post-queue</c></item>
///   <item><c>schedule_type = 'DOWNLOAD'</c> → <c>ips-feed-queue</c></item>
/// </list>
/// </para>
/// <para>
/// The SQS message body is a JSON object with <c>JobId</c>, <c>ClientType</c>,
/// <c>Pipeline</c>, and <c>TriggerType</c> fields — matching <c>SqsMessagePayload</c>.
/// </para>
/// </remarks>
public class SchedulerSyncService
{
    private readonly string _connectionString;
    private readonly IAmazonScheduler _schedulerClient;
    private readonly ILogger<SchedulerSyncService> _logger;

    // -----------------------------------------------------------------------
    // Configuration constants
    // -----------------------------------------------------------------------

    /// <summary>
    /// Prefix applied to every EventBridge Scheduler rule name managed by this service.
    /// </summary>
    public const string RuleNamePrefix = "ips-autopost-";

    /// <summary>
    /// EventBridge Scheduler group name. All rules are placed in this group.
    /// </summary>
    public const string ScheduleGroupName = "ips-autopost";

    /// <summary>
    /// Flexible time window in minutes. EventBridge may start the rule up to this many
    /// minutes after the scheduled time to allow batching and reduce cold starts.
    /// </summary>
    private const int FlexibleWindowMinutes = 5;

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    public SchedulerSyncService(
        string connectionString,
        IAmazonScheduler schedulerClient,
        ILogger<SchedulerSyncService> logger)
    {
        _connectionString = connectionString
            ?? throw new ArgumentNullException(nameof(connectionString));
        _schedulerClient = schedulerClient
            ?? throw new ArgumentNullException(nameof(schedulerClient));
        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    // -----------------------------------------------------------------------
    // Public entry point
    // -----------------------------------------------------------------------

    /// <summary>
    /// Performs a full synchronisation pass:
    /// <list type="number">
    ///   <item>Reads all rows from <c>generic_execution_schedule JOIN generic_job_configuration</c>.</item>
    ///   <item>For each active row: creates the EventBridge rule if it does not exist, or updates it if the schedule expression has changed.</item>
    ///   <item>For each inactive row: disables the corresponding EventBridge rule (does not delete it).</item>
    /// </list>
    /// </summary>
    /// <param name="feedQueueArn">ARN of the <c>ips-feed-queue</c> SQS queue.</param>
    /// <param name="postQueueArn">ARN of the <c>ips-post-queue</c> SQS queue.</param>
    /// <param name="schedulerRoleArn">ARN of the IAM role that EventBridge Scheduler uses to send messages to SQS.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SyncAsync(
        string feedQueueArn,
        string postQueueArn,
        string schedulerRoleArn,
        CancellationToken ct = default)
    {
        _logger.LogInformation("SchedulerSyncService: starting synchronisation pass.");

        var rows = await LoadScheduleRowsAsync(ct);
        _logger.LogInformation("SchedulerSyncService: loaded {Count} schedule rows.", rows.Count);

        // Ensure the schedule group exists before creating rules
        await EnsureScheduleGroupExistsAsync(ct);

        int created = 0, updated = 0, disabled = 0, skipped = 0;

        foreach (var row in rows)
        {
            var ruleName = BuildRuleName(row.JobId, row.ScheduleType);

            if (!row.IsActive)
            {
                var wasDisabled = await DisableRuleIfExistsAsync(ruleName, ct);
                if (wasDisabled) disabled++;
                continue;
            }

            var scheduleExpression = BuildScheduleExpression(row);
            if (scheduleExpression is null)
            {
                _logger.LogWarning(
                    "SchedulerSyncService: skipping schedule row {ScheduleId} for job {JobId} — " +
                    "no valid cron_expression or execution_time configured.",
                    row.ScheduleId, row.JobId);
                skipped++;
                continue;
            }

            var targetArn = row.ScheduleType.Equals("DOWNLOAD", StringComparison.OrdinalIgnoreCase)
                ? feedQueueArn
                : postQueueArn;

            var messageBody = BuildSqsMessageBody(row);

            var existingRule = await GetExistingRuleAsync(ruleName, ct);

            if (existingRule is null)
            {
                await CreateRuleAsync(ruleName, scheduleExpression, targetArn, schedulerRoleArn, messageBody, row, ct);
                created++;
                _logger.LogInformation(
                    "SchedulerSyncService: created rule '{RuleName}' with expression '{Expression}'.",
                    ruleName, scheduleExpression);
            }
            else if (HasScheduleChanged(existingRule, scheduleExpression))
            {
                await UpdateRuleAsync(ruleName, scheduleExpression, targetArn, schedulerRoleArn, messageBody, row, ct);
                updated++;
                _logger.LogInformation(
                    "SchedulerSyncService: updated rule '{RuleName}' — expression changed to '{Expression}'.",
                    ruleName, scheduleExpression);
            }
            else
            {
                _logger.LogDebug(
                    "SchedulerSyncService: rule '{RuleName}' is up-to-date, no changes needed.",
                    ruleName);
            }
        }

        _logger.LogInformation(
            "SchedulerSyncService: synchronisation complete — created={Created}, updated={Updated}, disabled={Disabled}, skipped={Skipped}.",
            created, updated, disabled, skipped);
    }

    // -----------------------------------------------------------------------
    // Database — load schedule rows
    // -----------------------------------------------------------------------

    /// <summary>
    /// Loads all rows from <c>generic_execution_schedule</c> joined with
    /// <c>generic_job_configuration</c>. Includes both active and inactive rows
    /// so that inactive rules can be disabled.
    /// </summary>
    protected virtual async Task<IReadOnlyList<ScheduleSyncRow>> LoadScheduleRowsAsync(
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                s.id                AS schedule_id,
                s.job_config_id,
                j.job_id,
                j.job_name,
                j.client_type,
                s.schedule_type,
                s.cron_expression,
                s.execution_time,
                s.is_active         AS schedule_is_active,
                j.is_active         AS job_is_active
            FROM generic_execution_schedule s
            INNER JOIN generic_job_configuration j
                ON s.job_config_id = j.id
            """;

        var ds = await SqlHelper.ExecuteDatasetAsync(
            _connectionString,
            CommandType.Text,
            sql,
            ct);

        if (ds.Tables.Count == 0)
            return Array.Empty<ScheduleSyncRow>();

        var results = new List<ScheduleSyncRow>(ds.Tables[0].Rows.Count);
        foreach (DataRow row in ds.Tables[0].Rows)
        {
            // A schedule row is considered active only when BOTH the schedule
            // row itself AND the parent job configuration are active.
            var scheduleIsActive = Convert.ToBoolean(row["schedule_is_active"]);
            var jobIsActive = Convert.ToBoolean(row["job_is_active"]);

            results.Add(new ScheduleSyncRow
            {
                ScheduleId     = Convert.ToInt32(row["schedule_id"]),
                JobConfigId    = Convert.ToInt32(row["job_config_id"]),
                JobId          = Convert.ToInt32(row["job_id"]),
                JobName        = row["job_name"]        as string ?? string.Empty,
                ClientType     = row["client_type"]     as string ?? string.Empty,
                ScheduleType   = row["schedule_type"]   as string ?? string.Empty,
                CronExpression = row["cron_expression"] == DBNull.Value
                                     ? null
                                     : row["cron_expression"] as string,
                ExecutionTime  = row["execution_time"]  == DBNull.Value
                                     ? null
                                     : row["execution_time"] as string,
                IsActive       = scheduleIsActive && jobIsActive
            });
        }

        return results;
    }

    // -----------------------------------------------------------------------
    // EventBridge Scheduler — group management
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates the <see cref="ScheduleGroupName"/> schedule group if it does not already exist.
    /// </summary>
    private async Task EnsureScheduleGroupExistsAsync(CancellationToken ct)
    {
        try
        {
            await _schedulerClient.GetScheduleGroupAsync(
                new GetScheduleGroupRequest { Name = ScheduleGroupName }, ct);
        }
        catch (ResourceNotFoundException)
        {
            _logger.LogInformation(
                "SchedulerSyncService: schedule group '{GroupName}' not found — creating it.",
                ScheduleGroupName);

            await _schedulerClient.CreateScheduleGroupAsync(
                new CreateScheduleGroupRequest { Name = ScheduleGroupName }, ct);
        }
    }

    // -----------------------------------------------------------------------
    // EventBridge Scheduler — rule CRUD
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the existing <see cref="GetScheduleResponse"/> for <paramref name="ruleName"/>,
    /// or <c>null</c> if the rule does not exist.
    /// </summary>
    private async Task<GetScheduleResponse?> GetExistingRuleAsync(
        string ruleName, CancellationToken ct)
    {
        try
        {
            return await _schedulerClient.GetScheduleAsync(
                new GetScheduleRequest
                {
                    Name        = ruleName,
                    GroupName   = ScheduleGroupName
                }, ct);
        }
        catch (ResourceNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a new EventBridge Scheduler rule.
    /// </summary>
    private async Task CreateRuleAsync(
        string ruleName,
        string scheduleExpression,
        string targetArn,
        string schedulerRoleArn,
        string messageBody,
        ScheduleSyncRow row,
        CancellationToken ct)
    {
        await _schedulerClient.CreateScheduleAsync(
            BuildCreateRequest(ruleName, scheduleExpression, targetArn, schedulerRoleArn, messageBody, row),
            ct);
    }

    /// <summary>
    /// Updates an existing EventBridge Scheduler rule with a new schedule expression.
    /// </summary>
    private async Task UpdateRuleAsync(
        string ruleName,
        string scheduleExpression,
        string targetArn,
        string schedulerRoleArn,
        string messageBody,
        ScheduleSyncRow row,
        CancellationToken ct)
    {
        await _schedulerClient.UpdateScheduleAsync(
            BuildUpdateRequest(ruleName, scheduleExpression, targetArn, schedulerRoleArn, messageBody, row),
            ct);
    }

    /// <summary>
    /// Disables an existing EventBridge Scheduler rule if it exists.
    /// Returns <c>true</c> if the rule was found and disabled, <c>false</c> if it did not exist.
    /// </summary>
    private async Task<bool> DisableRuleIfExistsAsync(string ruleName, CancellationToken ct)
    {
        var existing = await GetExistingRuleAsync(ruleName, ct);
        if (existing is null)
            return false;

        // Already disabled — nothing to do
        if (existing.State == ScheduleState.DISABLED)
        {
            _logger.LogDebug(
                "SchedulerSyncService: rule '{RuleName}' is already disabled.", ruleName);
            return false;
        }

        await _schedulerClient.UpdateScheduleAsync(
            new UpdateScheduleRequest
            {
                Name               = ruleName,
                GroupName          = ScheduleGroupName,
                ScheduleExpression = existing.ScheduleExpression,
                State              = ScheduleState.DISABLED,
                Target             = existing.Target,
                FlexibleTimeWindow = existing.FlexibleTimeWindow
            }, ct);

        _logger.LogInformation(
            "SchedulerSyncService: disabled rule '{RuleName}'.", ruleName);
        return true;
    }

    // -----------------------------------------------------------------------
    // Request builders
    // -----------------------------------------------------------------------

    private static CreateScheduleRequest BuildCreateRequest(
        string ruleName,
        string scheduleExpression,
        string targetArn,
        string schedulerRoleArn,
        string messageBody,
        ScheduleSyncRow row)
    {
        return new CreateScheduleRequest
        {
            Name               = ruleName,
            GroupName          = ScheduleGroupName,
            Description        = $"IPS AutoPost — {row.JobName} ({row.ScheduleType})",
            ScheduleExpression = scheduleExpression,
            State              = ScheduleState.ENABLED,
            FlexibleTimeWindow = new FlexibleTimeWindow
            {
                Mode                    = FlexibleTimeWindowMode.FLEXIBLE,
                MaximumWindowInMinutes  = FlexibleWindowMinutes
            },
            Target = new Target
            {
                Arn     = targetArn,
                RoleArn = schedulerRoleArn,
                Input   = messageBody
            }
        };
    }

    private static UpdateScheduleRequest BuildUpdateRequest(
        string ruleName,
        string scheduleExpression,
        string targetArn,
        string schedulerRoleArn,
        string messageBody,
        ScheduleSyncRow row)
    {
        return new UpdateScheduleRequest
        {
            Name               = ruleName,
            GroupName          = ScheduleGroupName,
            Description        = $"IPS AutoPost — {row.JobName} ({row.ScheduleType})",
            ScheduleExpression = scheduleExpression,
            State              = ScheduleState.ENABLED,
            FlexibleTimeWindow = new FlexibleTimeWindow
            {
                Mode                    = FlexibleTimeWindowMode.FLEXIBLE,
                MaximumWindowInMinutes  = FlexibleWindowMinutes
            },
            Target = new Target
            {
                Arn     = targetArn,
                RoleArn = schedulerRoleArn,
                Input   = messageBody
            }
        };
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds the EventBridge Scheduler rule name from the job ID and schedule type.
    /// Format: <c>ips-autopost-{jobId}-{scheduleType}</c> (lower-cased schedule type).
    /// </summary>
    public static string BuildRuleName(int jobId, string scheduleType)
        => $"{RuleNamePrefix}{jobId}-{scheduleType.ToLowerInvariant()}";

    /// <summary>
    /// Derives the EventBridge schedule expression from the <see cref="ScheduleSyncRow"/>.
    /// <list type="bullet">
    ///   <item>If <see cref="ScheduleSyncRow.CronExpression"/> is set, it is used as-is.</item>
    ///   <item>
    ///     If <see cref="ScheduleSyncRow.ExecutionTime"/> is set (HH:mm), it is converted to
    ///     a daily EventBridge cron expression: <c>cron(mm HH * * ? *)</c>.
    ///   </item>
    ///   <item>Returns <c>null</c> when neither field is populated.</item>
    /// </list>
    /// </summary>
    public static string? BuildScheduleExpression(ScheduleSyncRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.CronExpression))
            return row.CronExpression;

        if (!string.IsNullOrWhiteSpace(row.ExecutionTime) &&
            TimeOnly.TryParseExact(row.ExecutionTime, "HH:mm", out var time))
        {
            // EventBridge cron format: cron(minutes hours day-of-month month day-of-week year)
            // "? *" means "any day-of-month, any year" with no day-of-week constraint
            return $"cron({time.Minute} {time.Hour} * * ? *)";
        }

        return null;
    }

    /// <summary>
    /// Returns <c>true</c> when the existing rule's schedule expression differs from
    /// <paramref name="newExpression"/> (case-insensitive comparison).
    /// </summary>
    private static bool HasScheduleChanged(GetScheduleResponse existing, string newExpression)
        => !string.Equals(
            existing.ScheduleExpression,
            newExpression,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the JSON message body that EventBridge Scheduler sends to the SQS queue.
    /// Matches the <c>SqsMessagePayload</c> contract expected by the Feed/Post workers.
    /// </summary>
    private static string BuildSqsMessageBody(ScheduleSyncRow row)
    {
        var pipeline = row.ScheduleType.Equals("DOWNLOAD", StringComparison.OrdinalIgnoreCase)
            ? "Feed"
            : "Post";

        var payload = new
        {
            JobId       = row.JobId,
            ClientType  = row.ClientType,
            Pipeline    = pipeline,
            TriggerType = "Scheduled"
        };

        return JsonSerializer.Serialize(payload);
    }
}
