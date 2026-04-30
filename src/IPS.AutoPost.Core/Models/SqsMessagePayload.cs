namespace IPS.AutoPost.Core.Models;

/// <summary>
/// Represents the JSON body of an SQS message consumed by the Feed Worker or Post Worker.
/// EventBridge Scheduler drops messages in this format onto <c>ips-feed-queue</c>
/// or <c>ips-post-queue</c>.
/// </summary>
public class SqsMessagePayload
{
    /// <summary>
    /// Numeric job identifier matching <c>generic_job_configuration.job_id</c>.
    /// </summary>
    public int JobId { get; set; }

    /// <summary>
    /// Client type string matching <c>generic_job_configuration.client_type</c>
    /// (e.g. "INVITEDCLUB", "SEVITA").
    /// </summary>
    public string ClientType { get; set; } = string.Empty;

    /// <summary>
    /// Pipeline type: "Post" or "Feed".
    /// Determines which orchestrator method is called.
    /// </summary>
    public string Pipeline { get; set; } = string.Empty;

    /// <summary>
    /// How this message was produced: "Scheduled" (EventBridge) or "Manual" (API).
    /// </summary>
    public string TriggerType { get; set; } = "Scheduled";

    /// <summary>
    /// Optional comma-separated list of <c>ItemId</c> values for manual post triggers.
    /// Empty or null for scheduled posts (process all eligible workitems).
    /// </summary>
    public string? ItemIds { get; set; }

    /// <summary>
    /// Optional user ID for manual post triggers.
    /// 0 or null for scheduled posts (uses <c>GenericJobConfig.DefaultUserId</c>).
    /// </summary>
    public int? UserId { get; set; }

    /// <summary>
    /// Optional mode override for runtime behaviour changes without redeployment.
    /// Examples: "DryRun", "UAT".
    /// </summary>
    public string? Mode { get; set; }
}
