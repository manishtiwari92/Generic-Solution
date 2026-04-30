using IPS.AutoPost.Core.Commands;
using IPS.AutoPost.Core.Engine;
using IPS.AutoPost.Core.Models;
using MediatR;

namespace IPS.AutoPost.Core.Handlers;

/// <summary>
/// MediatR handler for <see cref="ExecuteFeedCommand"/>.
/// Delegates to <see cref="AutoPostOrchestrator.RunScheduledFeedAsync"/> —
/// keeps the handler thin and the orchestrator independently testable.
/// </summary>
/// <remarks>
/// Wrapped automatically by <c>LoggingBehavior</c> and <c>ValidationBehavior</c>
/// pipeline behaviors registered in <c>ServiceCollectionExtensions</c>.
/// Plugins that return <see cref="FeedResult.NotApplicable()"/> (e.g. SevitaPlugin)
/// are silently skipped by the orchestrator.
/// </remarks>
public class ExecuteFeedHandler : IRequestHandler<ExecuteFeedCommand, FeedResult>
{
    private readonly AutoPostOrchestrator _orchestrator;

    public ExecuteFeedHandler(AutoPostOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Handles the <see cref="ExecuteFeedCommand"/> by delegating to
    /// <see cref="AutoPostOrchestrator.RunScheduledFeedAsync"/>.
    /// </summary>
    public Task<FeedResult> Handle(ExecuteFeedCommand request, CancellationToken cancellationToken)
        => _orchestrator.RunScheduledFeedAsync(
            request.JobId,
            request.ClientType,
            cancellationToken);
}
