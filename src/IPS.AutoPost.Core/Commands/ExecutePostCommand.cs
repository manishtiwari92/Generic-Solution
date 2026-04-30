using IPS.AutoPost.Core.Models;
using MediatR;

namespace IPS.AutoPost.Core.Commands;

/// <summary>
/// MediatR command that triggers invoice posting for a single job.
/// Sent by <c>PostWorker</c> for each SQS message received from <c>ips-post-queue</c>,
/// and by <c>PostController</c> for manual post triggers from the Workflow UI.
/// </summary>
/// <remarks>
/// The command is dispatched via <c>IMediator.Send()</c> and handled by
/// <see cref="Handlers.ExecutePostHandler"/>, which delegates to
/// <c>AutoPostOrchestrator</c>. Pipeline behaviors
/// (<c>LoggingBehavior</c>, <c>ValidationBehavior</c>) wrap every dispatch automatically.
/// </remarks>
public class ExecutePostCommand : IRequest<PostBatchResult>
{
    /// <summary>
    /// Numeric job identifier matching <c>generic_job_configuration.job_id</c>.
    /// Used to load the job configuration and fetch eligible workitems.
    /// </summary>
    public int JobId { get; set; }

    /// <summary>
    /// Client type string matching <c>generic_job_configuration.client_type</c>
    /// (e.g. "INVITEDCLUB", "SEVITA").
    /// Used by <c>PluginRegistry</c> to resolve the correct <see cref="Interfaces.IClientPlugin"/>.
    /// </summary>
    public string ClientType { get; set; } = string.Empty;

    /// <summary>
    /// How this command was triggered.
    /// "Scheduled" — triggered by EventBridge via SQS.
    /// "Manual"    — triggered directly by a user from the Workflow UI via the API.
    /// </summary>
    public string TriggerType { get; set; } = "Scheduled";

    /// <summary>
    /// Optional comma-separated list of <c>ItemId</c> values for a manual post.
    /// Empty string means "process all eligible workitems" (scheduled mode).
    /// When non-empty, the orchestrator calls <c>RunManualPostAsync</c> instead of
    /// <c>RunScheduledPostAsync</c>.
    /// </summary>
    public string ItemIds { get; set; } = string.Empty;

    /// <summary>
    /// The user ID to record in audit logs and workitem routing calls.
    /// For scheduled posts, this is 0 and the orchestrator uses
    /// <c>GenericJobConfig.DefaultUserId</c>.
    /// For manual posts, this is the authenticated user's ID from the API request.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Optional mode override for runtime behaviour changes without redeployment.
    /// Examples: "DryRun" (log only, no API calls), "UAT" (use UAT endpoints).
    /// <c>null</c> means normal production behaviour.
    /// </summary>
    public string? Mode { get; set; }
}
