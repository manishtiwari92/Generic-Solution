// IPS.AutoPost.Api/Controllers/FeedController.cs
// Task 20.2: Feed trigger endpoint — calls RunScheduledFeedAsync directly,
//            bypassing SQS for on-demand feed downloads.

using IPS.AutoPost.Core.Engine;
using IPS.AutoPost.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace IPS.AutoPost.Api.Controllers;

/// <summary>
/// Handles on-demand feed download requests.
/// <para>
/// Calls <see cref="AutoPostOrchestrator.RunScheduledFeedAsync"/> directly,
/// bypassing SQS for immediate execution. Useful for triggering a feed refresh
/// outside the normal EventBridge schedule (e.g. after a data correction).
/// </para>
/// <para>
/// Authentication is enforced by <c>ApiKeyMiddleware</c> before the request
/// reaches this controller.
/// </para>
/// </summary>
[ApiController]
[Route("api/feed")]
[Produces("application/json")]
public class FeedController : ControllerBase
{
    private readonly AutoPostOrchestrator _orchestrator;
    private readonly ILogger<FeedController> _logger;

    public FeedController(
        AutoPostOrchestrator orchestrator,
        ILogger<FeedController> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // POST /api/feed/{jobId}
    // Triggers an on-demand feed download for the specified job.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Triggers an on-demand feed download for the specified job.
    /// </summary>
    /// <param name="jobId">
    /// Job identifier. Must match a row in <c>generic_job_configuration</c>
    /// with <c>download_feed = 1</c>.
    /// </param>
    /// <param name="clientType">
    /// Client type string (e.g. <c>INVITEDCLUB</c>). Required to resolve the
    /// correct plugin from the registry.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="FeedResult"/> describing the outcome.
    /// Returns HTTP 200 with <c>isApplicable=false</c> when the plugin has no
    /// feed download step (e.g. Sevita).
    /// Returns HTTP 404 when no active configuration is found for <paramref name="jobId"/>.
    /// Returns HTTP 400 when <paramref name="clientType"/> is missing.
    /// </returns>
    [HttpPost("{jobId:int}")]
    [ProducesResponseType(typeof(FeedResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TriggerFeed(
        [FromRoute] int jobId,
        [FromQuery] string clientType,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientType))
            return BadRequest(new { error = "clientType query parameter is required." });

        _logger.LogInformation(
            "On-demand feed download requested: JobId={JobId}, ClientType={ClientType}",
            jobId, clientType);

        var result = await _orchestrator.RunScheduledFeedAsync(jobId, clientType, ct);

        // NotApplicable with no error means the job config was not found or inactive
        if (!result.IsApplicable && result.ErrorMessage != null)
            return NotFound(new { error = result.ErrorMessage });

        return Ok(result);
    }
}
