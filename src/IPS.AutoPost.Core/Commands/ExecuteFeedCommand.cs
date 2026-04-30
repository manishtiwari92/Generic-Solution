using IPS.AutoPost.Core.Models;
using MediatR;

namespace IPS.AutoPost.Core.Commands;

/// <summary>
/// MediatR command that triggers a feed download for a single job.
/// Sent by <c>FeedWorker</c> for each SQS message received from <c>ips-feed-queue</c>.
/// </summary>
/// <remarks>
/// The command is dispatched via <c>IMediator.Send()</c> and handled by
/// <see cref="Handlers.ExecuteFeedHandler"/>, which delegates to
/// <c>AutoPostOrchestrator.RunScheduledFeedAsync</c>. Pipeline behaviors
/// (<c>LoggingBehavior</c>, <c>ValidationBehavior</c>) wrap every dispatch automatically.
/// </remarks>
public class ExecuteFeedCommand : IRequest<FeedResult>
{
    /// <summary>
    /// Numeric job identifier matching <c>generic_job_configuration.job_id</c>.
    /// Used to load the job configuration and invoke the correct feed strategy.
    /// </summary>
    public int JobId { get; set; }

    /// <summary>
    /// Client type string matching <c>generic_job_configuration.client_type</c>
    /// (e.g. "INVITEDCLUB").
    /// Used by <c>PluginRegistry</c> to resolve the correct <see cref="Interfaces.IClientPlugin"/>.
    /// Plugins that return <see cref="FeedResult.NotApplicable()"/> from
    /// <c>ExecuteFeedDownloadAsync</c> (e.g. SevitaPlugin) are silently skipped.
    /// </summary>
    public string ClientType { get; set; } = string.Empty;

    /// <summary>
    /// How this command was triggered.
    /// "Scheduled" — triggered by EventBridge via SQS.
    /// "Manual"    — triggered directly via the API.
    /// </summary>
    public string TriggerType { get; set; } = "Scheduled";

    /// <summary>
    /// Optional mode override for runtime behaviour changes without redeployment.
    /// Examples: "DryRun" (log only, no downloads), "UAT" (use UAT endpoints).
    /// <c>null</c> means normal production behaviour.
    /// </summary>
    public string? Mode { get; set; }
}
