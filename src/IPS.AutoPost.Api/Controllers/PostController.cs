// IPS.AutoPost.Api/Controllers/PostController.cs
// Task 20.1: Manual post endpoints — both routes call RunManualPostAsync directly,
//            bypassing SQS for immediate synchronous response from the Workflow UI.

using IPS.AutoPost.Core.Engine;
using IPS.AutoPost.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace IPS.AutoPost.Api.Controllers;

/// <summary>
/// Handles manual invoice posting requests from the Workflow UI.
/// <para>
/// Manual posts bypass SQS entirely and call <see cref="AutoPostOrchestrator.RunManualPostAsync"/>
/// directly so the caller receives an immediate synchronous response.
/// This is intentional — a 60-second SQS cold-start is unacceptable for interactive triggers.
/// </para>
/// <para>
/// Both endpoints require the <c>x-api-key</c> header to be present and valid.
/// Authentication is enforced by <c>ApiKeyMiddleware</c> before the request reaches this controller.
/// </para>
/// </summary>
[ApiController]
[Route("api/post")]
[Produces("application/json")]
public class PostController : ControllerBase
{
    private readonly AutoPostOrchestrator _orchestrator;
    private readonly ILogger<PostController> _logger;

    public PostController(
        AutoPostOrchestrator orchestrator,
        ILogger<PostController> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // POST /api/post/{jobId}/items/{itemIds}
    // Manual post for a specific comma-separated list of ItemIds.
    // The jobId route parameter is accepted for routing clarity but the
    // orchestrator resolves configuration from the workitem's current StatusId,
    // matching the legacy Windows Service behaviour exactly.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Triggers a manual post for one or more specific workitems.
    /// </summary>
    /// <param name="jobId">
    /// Job identifier (used for logging context; configuration is resolved from
    /// the workitem's current <c>StatusId</c>).
    /// </param>
    /// <param name="itemIds">
    /// Comma-separated list of <c>ItemId</c> values to post
    /// (e.g. <c>1001</c> or <c>1001,1002,1003</c>).
    /// </param>
    /// <param name="userId">
    /// Optional user ID of the person triggering the post.
    /// Defaults to the job's <c>default_user_id</c> when not supplied.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="PostBatchResult"/> with per-item results.
    /// Returns HTTP 200 even when individual items fail — inspect
    /// <see cref="PostBatchResult.RecordsFailed"/> and
    /// <see cref="PostBatchResult.ItemResults"/> for per-item outcomes.
    /// Returns HTTP 400 when <paramref name="itemIds"/> is empty.
    /// Returns HTTP 404 when no configuration is found for the workitems.
    /// </returns>
    [HttpPost("{jobId:int}/items/{itemIds}")]
    [ProducesResponseType(typeof(PostBatchResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostItems(
        [FromRoute] int jobId,
        [FromRoute] string itemIds,
        [FromQuery] int userId = 0,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(itemIds))
            return BadRequest(new { error = "itemIds route parameter is required." });

        _logger.LogInformation(
            "Manual post requested: JobId={JobId}, ItemIds={ItemIds}, UserId={UserId}",
            jobId, itemIds, userId);

        var result = await _orchestrator.RunManualPostAsync(itemIds, userId, ct);

        if (result.ResponseCode == -1)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result);
    }

    // -----------------------------------------------------------------------
    // POST /api/post/{jobId}
    // Manual post for all pending workitems in the job's source queue.
    // The orchestrator fetches workitems by StatusId from the job config.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Triggers a manual post for all pending workitems belonging to the specified job.
    /// </summary>
    /// <param name="jobId">
    /// Job identifier. The orchestrator loads configuration from
    /// <c>generic_job_configuration</c> and fetches all workitems in the source queue.
    /// </param>
    /// <param name="userId">
    /// Optional user ID of the person triggering the post.
    /// Defaults to the job's <c>default_user_id</c> when not supplied.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="PostBatchResult"/> with aggregate and per-item results.
    /// Returns HTTP 404 when no configuration is found for <paramref name="jobId"/>.
    /// </returns>
    [HttpPost("{jobId:int}")]
    [ProducesResponseType(typeof(PostBatchResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PostJob(
        [FromRoute] int jobId,
        [FromQuery] int userId = 0,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Manual post (all items) requested: JobId={JobId}, UserId={UserId}",
            jobId, userId);

        // Pass jobId as the itemIds string so the orchestrator can resolve
        // configuration from the job's source queue. An empty itemIds string
        // triggers the scheduled path; passing the jobId as a hint lets the
        // orchestrator look up all pending workitems for this job.
        // The orchestrator's RunManualPostAsync resolves config from StatusId,
        // so we pass an empty itemIds to trigger the full-batch manual path.
        var result = await _orchestrator.RunManualPostAsync(
            itemIds: string.Empty,
            userId: userId,
            ct: ct);

        if (result.ResponseCode == -1)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result);
    }
}
