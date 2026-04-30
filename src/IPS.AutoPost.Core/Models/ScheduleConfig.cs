namespace IPS.AutoPost.Core.Models;

/// <summary>
/// Represents one row from <c>generic_execution_schedule</c>.
/// Used by <c>SchedulerService.ShouldExecute</c> to determine whether a job
/// should run based on the current time and the last execution time.
/// </summary>
public class ScheduleConfig
{
    /// <summary>Primary key of the <c>generic_execution_schedule</c> row.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to <c>generic_job_configuration.id</c>.</summary>
    public int JobConfigId { get; set; }

    /// <summary>Foreign key to <c>generic_job_configuration.job_id</c>.</summary>
    public int JobId { get; set; }

    /// <summary>
    /// Schedule type: "POST" (invoice posting) or "DOWNLOAD" (feed download).
    /// Determines which SQS queue EventBridge targets.
    /// </summary>
    public string ScheduleType { get; set; } = string.Empty;

    /// <summary>
    /// Scheduled execution time in HH:mm format (24-hour clock).
    /// The Core_Engine uses a 30-minute window around this time to decide
    /// whether to execute. Example: "14:30" means execute between 14:15 and 14:45.
    /// Null when <see cref="CronExpression"/> is used instead.
    /// </summary>
    public string? ExecutionTime { get; set; }

    /// <summary>
    /// EventBridge cron or rate expression (e.g. "cron(0 14 * * ? *)" or "rate(1 hour)").
    /// Used by the Scheduler Lambda to create/update EventBridge rules.
    /// Null when <see cref="ExecutionTime"/> is used instead.
    /// </summary>
    public string? CronExpression { get; set; }

    /// <summary>
    /// When <c>false</c>, the Scheduler Lambda disables the corresponding EventBridge rule.
    /// </summary>
    public bool IsActive { get; set; }
}
