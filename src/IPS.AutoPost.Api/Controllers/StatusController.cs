// IPS.AutoPost.Api/Controllers/StatusController.cs
// Task 20.3: Execution status endpoint — reads generic_execution_history by ID.

using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace IPS.AutoPost.Api.Controllers;

/// <summary>
/// Provides read-only access to execution history records stored in
/// <c>generic_execution_history</c>.
/// <para>
/// Allows the Workflow UI and monitoring tools to poll the outcome of a
/// specific execution run by its database-assigned ID.
/// </para>
/// <para>
/// Authentication is enforced by <c>ApiKeyMiddleware</c> before the request
/// reaches this controller.
/// </para>
/// </summary>
[ApiController]
[Route("api/status")]
[Produces("application/json")]
public class StatusController : ControllerBase
{
    private readonly IAuditRepository _auditRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StatusController> _logger;

    public StatusController(
        IAuditRepository auditRepository,
        IConfiguration configuration,
        ILogger<StatusController> logger)
    {
        _auditRepository = auditRepository;
        _configuration = configuration;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // GET /api/status/{executionId}
    // -----------------------------------------------------------------------

    /// <summary>
    /// Retrieves the execution history record for the specified execution run.
    /// </summary>
    /// <param name="executionId">
    /// Primary key of the <c>generic_execution_history</c> row.
    /// Returned in the response body of a post or feed trigger call.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="GenericExecutionHistory"/> when found.
    /// Returns HTTP 404 when no record exists for <paramref name="executionId"/>.
    /// </returns>
    [HttpGet("{executionId:long}")]
    [ProducesResponseType(typeof(GenericExecutionHistory), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(
        [FromRoute] long executionId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Status query for ExecutionId={ExecutionId}", executionId);

        var connectionString = _configuration.GetConnectionString("Workflow")
            ?? throw new InvalidOperationException(
                "Connection string 'Workflow' is not configured.");

        var history = await _auditRepository.GetExecutionHistoryAsync(
            executionId, connectionString, ct);

        if (history is null)
        {
            _logger.LogWarning("ExecutionId={ExecutionId} not found in generic_execution_history", executionId);
            return NotFound(new { error = $"Execution record {executionId} not found." });
        }

        return Ok(history);
    }
}
