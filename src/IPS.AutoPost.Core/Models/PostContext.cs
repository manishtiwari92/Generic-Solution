namespace IPS.AutoPost.Core.Models;

/// <summary>
/// Carries the runtime context for a post execution batch.
/// Passed from <c>AutoPostOrchestrator</c> to <see cref="Interfaces.IClientPlugin.ExecutePostAsync"/>.
/// </summary>
public class PostContext
{
    /// <summary>
    /// How this batch was triggered.
    /// "Scheduled" — triggered by EventBridge via SQS.
    /// "Manual"    — triggered directly by a user from the Workflow UI via the API.
    /// </summary>
    public string TriggerType { get; init; } = "Scheduled";

    /// <summary>
    /// Comma-separated list of <c>ItemId</c> values for a manual post.
    /// Empty string means "process all eligible workitems" (scheduled mode).
    /// </summary>
    public string ItemIds { get; init; } = string.Empty;

    /// <summary>
    /// The user ID to record in audit logs and workitem routing calls.
    /// Defaults to <c>GenericJobConfig.DefaultUserId</c> (100) for scheduled posts.
    /// For manual posts, this is the authenticated user's ID from the API request.
    /// </summary>
    public int UserId { get; init; }

    /// <summary>
    /// <c>true</c> when this is a manual post (either <see cref="ItemIds"/> is non-empty
    /// or <see cref="TriggerType"/> is "Manual").
    /// Plugins can use this to adjust behaviour (e.g. skip schedule window checks).
    /// </summary>
    public bool ProcessManually => !string.IsNullOrEmpty(ItemIds) || TriggerType == "Manual";

    /// <summary>
    /// S3 / Edenred credentials loaded from <c>EdenredApiUrlConfig</c> at startup.
    /// Used by plugins to initialise the S3 image retrieval service.
    /// </summary>
    public EdenredApiUrlConfig S3Config { get; init; } = new();

    /// <summary>
    /// Cancellation token propagated from the SQS consumer or HTTP request.
    /// Plugins should pass this to all async calls.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}
