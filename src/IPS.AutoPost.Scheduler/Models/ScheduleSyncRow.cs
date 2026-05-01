namespace IPS.AutoPost.Scheduler.Models;

/// <summary>
/// Represents one row from the JOIN of <c>generic_execution_schedule</c>
/// and <c>generic_job_configuration</c>, used by <see cref="SchedulerSyncService"/>
/// to create or update EventBridge Scheduler rules.
/// </summary>
public sealed class ScheduleSyncRow
{
    /// <summary>Primary key of <c>generic_execution_schedule</c>.</summary>
    public int ScheduleId { get; init; }

    /// <summary>Primary key of <c>generic_job_configuration</c>.</summary>
    public int JobConfigId { get; init; }

    /// <summary>Numeric job identifier (e.g. 1001).</summary>
    public int JobId { get; init; }

    /// <summary>Human-readable job name used in rule descriptions.</summary>
    public string JobName { get; init; } = string.Empty;

    /// <summary>
    /// Schedule type: <c>"POST"</c> (targets <c>ips-post-queue</c>) or
    /// <c>"DOWNLOAD"</c> (targets <c>ips-feed-queue</c>).
    /// </summary>
    public string ScheduleType { get; init; } = string.Empty;

    /// <summary>
    /// EventBridge cron or rate expression (e.g. <c>"cron(0 14 * * ? *)"</c>).
    /// When null, <see cref="ExecutionTime"/> is used to derive a cron expression.
    /// </summary>
    public string? CronExpression { get; init; }

    /// <summary>
    /// HH:mm execution time used when <see cref="CronExpression"/> is null.
    /// The Scheduler Lambda converts this to a daily EventBridge cron expression.
    /// </summary>
    public string? ExecutionTime { get; init; }

    /// <summary>
    /// When <c>false</c>, the corresponding EventBridge rule is disabled (not deleted).
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Client type string (e.g. <c>"INVITEDCLUB"</c>, <c>"SEVITA"</c>).
    /// Included in the SQS message body sent by EventBridge.
    /// </summary>
    public string ClientType { get; init; } = string.Empty;
}
