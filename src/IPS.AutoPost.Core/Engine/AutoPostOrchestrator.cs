using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using Microsoft.Extensions.Logging;

namespace IPS.AutoPost.Core.Engine;

/// <summary>
/// Core orchestration engine that drives all common operations for every client:
/// schedule checking, workitem fetching, plugin invocation, queue routing,
/// history recording, and logging.
/// <para>
/// Client-specific ERP logic lives entirely in <see cref="IClientPlugin"/> implementations.
/// The orchestrator never contains client-specific code.
/// </para>
/// </summary>
/// <remarks>
/// Called by MediatR handlers (<c>ExecutePostHandler</c>, <c>ExecuteFeedHandler</c>)
/// which are themselves invoked by the SQS workers and the API controllers.
/// Full implementation is in task 6. This stub exposes the public API surface
/// that the handlers depend on.
/// </remarks>
public class AutoPostOrchestrator
{
    private readonly IConfigurationRepository _configRepo;
    private readonly IWorkitemRepository _workitemRepo;
    private readonly IRoutingRepository _routingRepo;
    private readonly IAuditRepository _auditRepo;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly PluginRegistry _pluginRegistry;
    private readonly SchedulerService _scheduler;
    private readonly ILogger<AutoPostOrchestrator> _logger;

    public AutoPostOrchestrator(
        IConfigurationRepository configRepo,
        IWorkitemRepository workitemRepo,
        IRoutingRepository routingRepo,
        IAuditRepository auditRepo,
        IScheduleRepository scheduleRepo,
        PluginRegistry pluginRegistry,
        SchedulerService scheduler,
        ILogger<AutoPostOrchestrator> logger)
    {
        _configRepo = configRepo;
        _workitemRepo = workitemRepo;
        _routingRepo = routingRepo;
        _auditRepo = auditRepo;
        _scheduleRepo = scheduleRepo;
        _pluginRegistry = pluginRegistry;
        _scheduler = scheduler;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // SCHEDULED POST
    // Called by ExecutePostHandler when ItemIds is empty (EventBridge → SQS trigger).
    // -----------------------------------------------------------------------

    /// <summary>
    /// Executes a scheduled post run for the given job.
    /// Loads configuration, checks the schedule window, invokes the plugin,
    /// and writes execution history.
    /// </summary>
    /// <param name="jobId">Job identifier from the SQS message.</param>
    /// <param name="clientType">Client type from the SQS message.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Aggregated batch result. Returns an empty result (no records processed)
    /// when the schedule window check fails or <c>AllowAutoPost</c> is false.
    /// </returns>
    public virtual async Task<PostBatchResult> RunScheduledPostAsync(
        int jobId,
        string clientType,
        CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        var config = await _configRepo.GetByJobIdAsync(jobId, ct);

        if (config == null || !config.IsActive)
        {
            _logger.LogWarning("[{ClientType}] Job {JobId} - Configuration not found or inactive. Skipping.",
                clientType, jobId);
            return new PostBatchResult();
        }

        var plugin = _pluginRegistry.Resolve(clientType);
        var schedules = await _scheduleRepo.GetSchedulesAsync(config.Id, config.JobId, ct);

        if (!_scheduler.ShouldExecute(config.LastPostTime, schedules))
        {
            _logger.LogInformation("[{ClientType}] Job {JobId} - Outside schedule window. Skipping.",
                clientType, jobId);
            return new PostBatchResult();
        }

        if (!config.AllowAutoPost)
        {
            _logger.LogInformation("[{ClientType}] Job {JobId} - AllowAutoPost=false. Skipping.",
                clientType, jobId);
            return new PostBatchResult();
        }

        _logger.LogInformation("[{ClientType}] Job {JobId} - Scheduled post started.", clientType, jobId);

        await plugin.OnBeforePostAsync(config, ct);

        var context = new PostContext
        {
            TriggerType = "Scheduled",
            UserId = config.DefaultUserId,
            S3Config = await _configRepo.GetEdenredApiUrlConfigAsync(ct),
            CancellationToken = ct
        };

        var result = await ExecutePostBatchAsync(plugin, config, context, startTime, ct);

        await _configRepo.UpdateLastPostTimeAsync(config.Id, ct);

        _logger.LogInformation(
            "[{ClientType}] Job {JobId} - Scheduled post completed: {Success} success, {Failed} failed.",
            clientType, jobId, result.RecordsSuccess, result.RecordsFailed);

        return result;
    }

    // -----------------------------------------------------------------------
    // MANUAL POST
    // Called by ExecutePostHandler when ItemIds is non-empty (API trigger).
    // -----------------------------------------------------------------------

    /// <summary>
    /// Executes a manual post for a specific set of workitems.
    /// Resolves configuration from the first workitem's current queue position,
    /// then invokes the plugin for the requested items.
    /// </summary>
    /// <param name="itemIds">Comma-separated list of ItemId values.</param>
    /// <param name="userId">Authenticated user ID from the API request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Aggregated batch result, or an error result when configuration cannot be resolved.
    /// </returns>
    public virtual async Task<PostBatchResult> RunManualPostAsync(
        string itemIds,
        int userId,
        CancellationToken ct)
    {
        var workitemDs = await _workitemRepo.GetWorkitemsByItemIdsAsync(itemIds, ct);

        if (workitemDs == null || workitemDs.Tables.Count == 0 || workitemDs.Tables[0].Rows.Count == 0)
        {
            _logger.LogWarning("Manual post: no workitems found for ItemIds={ItemIds}", itemIds);
            return new PostBatchResult { ErrorMessage = "No workitems found." };
        }

        var statusId = Convert.ToInt32(workitemDs.Tables[0].Rows[0]["StatusID"]);
        var config = await _configRepo.GetBySourceQueueIdAsync(statusId, ct);

        if (config == null)
        {
            _logger.LogWarning("Manual post: no configuration found for StatusId={StatusId}", statusId);
            return new PostBatchResult { ResponseCode = -1, ErrorMessage = "Missing Configuration." };
        }

        var plugin = _pluginRegistry.Resolve(config.ClientType);
        await plugin.OnBeforePostAsync(config, ct);

        var context = new PostContext
        {
            TriggerType = "Manual",
            ItemIds = itemIds,
            UserId = userId,
            S3Config = await _configRepo.GetEdenredApiUrlConfigAsync(ct),
            CancellationToken = ct
        };

        return await ExecutePostBatchAsync(plugin, config, context, DateTime.UtcNow, ct);
    }

    // -----------------------------------------------------------------------
    // SCHEDULED FEED DOWNLOAD
    // Called by ExecuteFeedHandler (FeedWorker SQS trigger).
    // -----------------------------------------------------------------------

    /// <summary>
    /// Executes a scheduled feed download for the given job.
    /// Skips silently when <c>DownloadFeed</c> is false or the plugin returns
    /// <see cref="FeedResult.NotApplicable()"/>.
    /// </summary>
    /// <param name="jobId">Job identifier from the SQS message.</param>
    /// <param name="clientType">Client type from the SQS message.</param>
    /// <param name="ct">Cancellation token.</param>
    public virtual async Task<FeedResult> RunScheduledFeedAsync(
        int jobId,
        string clientType,
        CancellationToken ct)
    {
        var config = await _configRepo.GetByJobIdAsync(jobId, ct);

        if (config == null || !config.IsActive || !config.DownloadFeed)
        {
            _logger.LogInformation(
                "[{ClientType}] Job {JobId} - Feed download skipped (inactive or DownloadFeed=false).",
                clientType, jobId);
            return FeedResult.NotApplicable();
        }

        var plugin = _pluginRegistry.Resolve(clientType);
        var feedContext = new FeedContext
        {
            TriggerType = "Scheduled",
            S3Config = await _configRepo.GetEdenredApiUrlConfigAsync(ct),
            CancellationToken = ct
        };

        _logger.LogInformation("[{ClientType}] Job {JobId} - Feed download started.", clientType, jobId);

        var result = await plugin.ExecuteFeedDownloadAsync(config, feedContext, ct);

        if (result.IsApplicable && result.Success)
        {
            await _configRepo.UpdateLastDownloadTimeAsync(config.Id, ct);
            _logger.LogInformation(
                "[{ClientType}] Job {JobId} - Feed download completed: {Records} records.",
                clientType, jobId, result.RecordsDownloaded);
        }
        else if (result.IsApplicable && !result.Success)
        {
            _logger.LogError(
                "[{ClientType}] Job {JobId} - Feed download failed: {Error}",
                clientType, jobId, result.ErrorMessage);
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // CORE BATCH EXECUTION — shared by scheduled and manual post
    // -----------------------------------------------------------------------

    /// <summary>
    /// Invokes the plugin's <c>ExecutePostAsync</c> and writes execution history
    /// in a <c>finally</c> block regardless of success or failure.
    /// </summary>
    private async Task<PostBatchResult> ExecutePostBatchAsync(
        IClientPlugin plugin,
        GenericJobConfig config,
        PostContext context,
        DateTime startTime,
        CancellationToken ct)
    {
        var executionHistory = new GenericExecutionHistory
        {
            JobConfigId = config.Id,
            ClientType = config.ClientType,
            JobId = config.JobId,
            ExecutionType = "POST",
            TriggerType = context.TriggerType,
            StartTime = startTime
        };

        PostBatchResult? result = null;

        try
        {
            result = await plugin.ExecutePostAsync(config, context, ct);
            executionHistory.Status = result.RecordsFailed == 0 ? "SUCCESS" : "PARTIAL_SUCCESS";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[{ClientType}] Job {JobId} - Unhandled exception in ExecutePostAsync.",
                config.ClientType, config.JobId);
            result = new PostBatchResult { ErrorMessage = ex.Message };
            executionHistory.Status = "FAILED";
            executionHistory.ErrorDetails = ex.ToString();
        }
        finally
        {
            executionHistory.EndTime = DateTime.UtcNow;
            executionHistory.RecordsProcessed = result?.RecordsProcessed ?? 0;
            executionHistory.RecordsSucceeded = result?.RecordsSuccess ?? 0;
            executionHistory.RecordsFailed = result?.RecordsFailed ?? 0;

            await _auditRepo.SaveExecutionHistoryAsync(executionHistory, config.DbConnectionString, ct);
        }

        return result ?? new PostBatchResult();
    }
}
