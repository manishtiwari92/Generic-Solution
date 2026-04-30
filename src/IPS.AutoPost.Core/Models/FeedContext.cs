namespace IPS.AutoPost.Core.Models;

/// <summary>
/// Carries the runtime context for a feed download execution.
/// Passed from <c>AutoPostOrchestrator</c> to
/// <see cref="Interfaces.IClientPlugin.ExecuteFeedDownloadAsync"/>.
/// </summary>
public class FeedContext
{
    /// <summary>
    /// How this feed run was triggered.
    /// "Scheduled" — triggered by EventBridge via SQS.
    /// "Manual"    — triggered directly via the API.
    /// </summary>
    public string TriggerType { get; init; } = "Scheduled";

    /// <summary>
    /// S3 / Edenred credentials loaded from <c>EdenredApiUrlConfig</c> at startup.
    /// Used by feed strategies that need to upload or retrieve files from S3.
    /// </summary>
    public EdenredApiUrlConfig S3Config { get; init; } = new();

    /// <summary>
    /// Cancellation token propagated from the SQS consumer or HTTP request.
    /// Feed strategies should pass this to all async calls.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}
