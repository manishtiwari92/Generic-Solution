using IPS.AutoPost.Core.Commands;
using IPS.AutoPost.Core.Engine;
using IPS.AutoPost.Core.Models;
using MediatR;

namespace IPS.AutoPost.Core.Handlers;

/// <summary>
/// MediatR handler for <see cref="ExecutePostCommand"/>.
/// Delegates to <see cref="AutoPostOrchestrator"/> — keeps the handler thin
/// and the orchestrator independently testable.
/// </summary>
/// <remarks>
/// Wrapped automatically by <c>LoggingBehavior</c> and <c>ValidationBehavior</c>
/// pipeline behaviors registered in <c>ServiceCollectionExtensions</c>.
/// </remarks>
public class ExecutePostHandler : IRequestHandler<ExecutePostCommand, PostBatchResult>
{
    private readonly AutoPostOrchestrator _orchestrator;

    public ExecutePostHandler(AutoPostOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Handles the <see cref="ExecutePostCommand"/> by routing to the correct
    /// orchestrator method based on whether <c>ItemIds</c> is populated.
    /// <list type="bullet">
    ///   <item>
    ///     <c>ItemIds</c> empty → <see cref="AutoPostOrchestrator.RunScheduledPostAsync"/>
    ///     (EventBridge-triggered scheduled post).
    ///   </item>
    ///   <item>
    ///     <c>ItemIds</c> non-empty → <see cref="AutoPostOrchestrator.RunManualPostAsync"/>
    ///     (user-triggered manual post from the Workflow UI).
    ///   </item>
    /// </list>
    /// </summary>
    public Task<PostBatchResult> Handle(ExecutePostCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.ItemIds))
        {
            return _orchestrator.RunScheduledPostAsync(
                request.JobId,
                request.ClientType,
                cancellationToken);
        }

        return _orchestrator.RunManualPostAsync(
            request.ItemIds,
            request.UserId,
            cancellationToken);
    }
}
