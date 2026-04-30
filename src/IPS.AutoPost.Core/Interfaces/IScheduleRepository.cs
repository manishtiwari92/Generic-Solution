using IPS.AutoPost.Core.Models;

namespace IPS.AutoPost.Core.Interfaces;

/// <summary>
/// Loads execution schedule configuration from <c>generic_execution_schedule</c>.
/// Used by <c>SchedulerService</c> to determine whether a job should execute
/// based on the current time and the configured schedule window.
/// </summary>
public interface IScheduleRepository
{
    /// <summary>
    /// Loads all active schedule rows for the given job configuration.
    /// Calls the <c>GetExecutionSchedule</c> stored procedure with
    /// <c>@file_creation_config_id</c> and <c>@job_id</c> parameters,
    /// matching the exact signature used by the legacy Windows Service implementations.
    /// </summary>
    /// <param name="jobConfigId">
    /// Foreign key to <c>generic_job_configuration.id</c>
    /// (maps to <c>@file_creation_config_id</c> in the stored procedure).
    /// </param>
    /// <param name="jobId">
    /// Numeric job identifier (maps to <c>@job_id</c> in the stored procedure).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// List of active schedule configurations for this job.
    /// Returns an empty list when no schedules are configured.
    /// </returns>
    Task<IReadOnlyList<ScheduleConfig>> GetSchedulesAsync(
        int jobConfigId,
        int jobId,
        CancellationToken ct = default);
}
