using IPS.AutoPost.Core.Models;

namespace IPS.AutoPost.Core.Interfaces;

/// <summary>
/// Loads and updates job configuration from <c>generic_job_configuration</c>
/// and related tables.
/// </summary>
public interface IConfigurationRepository
{
    /// <summary>
    /// Loads the job configuration for the given <paramref name="jobId"/>.
    /// Returns <c>null</c> when no matching row exists.
    /// </summary>
    Task<GenericJobConfig?> GetByJobIdAsync(int jobId, CancellationToken ct = default);

    /// <summary>
    /// Loads the job configuration whose <c>source_queue_id</c> contains
    /// <paramref name="statusId"/>. Used by the manual post flow to resolve
    /// the correct config from the first workitem's current queue position.
    /// Returns <c>null</c> when no matching row exists.
    /// </summary>
    Task<GenericJobConfig?> GetBySourceQueueIdAsync(int statusId, CancellationToken ct = default);

    /// <summary>
    /// Loads S3 / Edenred credentials from the <c>EdenredApiUrlConfig</c> table.
    /// Called once per execution run and passed to the plugin via
    /// <see cref="PostContext.S3Config"/> and <see cref="FeedContext.S3Config"/>.
    /// </summary>
    Task<EdenredApiUrlConfig> GetEdenredApiUrlConfigAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates <c>last_post_time</c> to the current UTC time after a successful
    /// scheduled post run. Used by <c>SchedulerService</c> to enforce the
    /// 30-minute execution window.
    /// </summary>
    Task UpdateLastPostTimeAsync(int jobConfigId, CancellationToken ct = default);

    /// <summary>
    /// Updates <c>last_download_time</c> to the current UTC time after a successful
    /// feed download run.
    /// </summary>
    Task UpdateLastDownloadTimeAsync(int jobConfigId, CancellationToken ct = default);
}
