namespace IPS.AutoPost.Core.Migrations.Entities;

/// <summary>
/// EF Core entity for the <c>generic_execution_schedule</c> table.
/// Supports both HH:mm format (backward-compatible with existing 15 libraries)
/// and cron expressions (for EventBridge Scheduler on AWS).
/// </summary>
public class GenericExecutionScheduleEntity
{
    public int Id { get; set; }

    /// <summary>Foreign key to <c>generic_job_configuration.id</c>.</summary>
    public int JobConfigId { get; set; }

    /// <summary>Schedule type: 'POST' or 'DOWNLOAD'.</summary>
    public string ScheduleType { get; set; } = "POST";

    /// <summary>
    /// HH:mm format execution time (e.g. "08:00").
    /// Used by SchedulerService.ShouldExecute() with 30-minute window logic.
    /// Backward-compatible with all existing Windows Service libraries.
    /// </summary>
    public string? ExecutionTime { get; set; }

    /// <summary>
    /// EventBridge cron/rate expression (e.g. "cron(0 8 * * ? *)", "rate(30 minutes)").
    /// Takes precedence over ExecutionTime when both are set.
    /// </summary>
    public string? CronExpression { get; set; }

    public DateTime? LastExecutionTime { get; set; }
    public bool IsActive { get; set; }

    // Navigation property
    public GenericJobConfigurationEntity JobConfiguration { get; set; } = null!;
}
