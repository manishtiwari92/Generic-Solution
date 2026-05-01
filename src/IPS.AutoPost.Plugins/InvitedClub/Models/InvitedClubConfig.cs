namespace IPS.AutoPost.Plugins.InvitedClub.Models;

/// <summary>
/// Client-specific configuration for InvitedClub, deserialized from
/// <c>generic_job_configuration.client_config_json</c>.
/// </summary>
public class InvitedClubConfig
{
    /// <summary>
    /// Maximum number of times an image attachment POST will be retried before giving up.
    /// Maps to <c>post_to_invitedclub_configuration.image_post_retry_limit</c>.
    /// </summary>
    public int ImagePostRetryLimit { get; set; }

    /// <summary>
    /// Queue ID used when an image cannot be retrieved from S3 (Edenred-side failure).
    /// Maps to <c>post_to_invitedclub_configuration.edenred_fail_post_queue_id</c>.
    /// </summary>
    public int EdenredFailQueueId { get; set; }

    /// <summary>
    /// Queue ID used when the Oracle Fusion invoice or calculateTax POST fails (InvitedClub-side failure).
    /// Maps to <c>post_to_invitedclub_configuration.invited_fail_post_queue_id</c>.
    /// </summary>
    public int InvitedFailQueueId { get; set; }

    /// <summary>
    /// HH:mm time string that controls when the feed download is allowed to run.
    /// Feed runs only when current time is past this value and
    /// <c>last_download_time</c> is before today.
    /// Maps to <c>post_to_invitedclub_configuration.feed_download_time</c>.
    /// </summary>
    public string FeedDownloadTime { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the last successful supplier data download.
    /// Used to determine whether to do a full refresh or an incremental update
    /// (incremental uses <c>LastSupplierDownloadTime - 2 days</c> as the filter).
    /// Maps to <c>post_to_invitedclub_configuration.last_supplier_download_time</c>.
    /// </summary>
    public DateTime LastSupplierDownloadTime { get; set; }
}
