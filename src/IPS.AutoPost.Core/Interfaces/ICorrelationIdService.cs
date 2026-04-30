namespace IPS.AutoPost.Core.Interfaces;

/// <summary>
/// Manages a per-SQS-message correlation ID stored in <c>AsyncLocal</c> storage.
/// Every log entry for a job run includes the correlation ID automatically via
/// Serilog <c>LogContext.PushProperty("CorrelationId", id)</c>.
/// </summary>
/// <remarks>
/// The correlation ID is scoped to the current async execution context, so concurrent
/// SQS message processing tasks each have their own independent ID.
/// </remarks>
public interface ICorrelationIdService
{
    /// <summary>
    /// Returns the current correlation ID for this async context.
    /// If no ID has been set yet, generates a new <see cref="Guid"/>-based ID,
    /// stores it, and returns it.
    /// </summary>
    /// <returns>
    /// A non-empty string correlation ID (e.g. "a3f2c1d0-...").
    /// </returns>
    string GetOrCreateCorrelationId();

    /// <summary>
    /// Sets a specific correlation ID for this async context and pushes it as a
    /// Serilog <c>LogContext</c> property so all subsequent log entries in this
    /// context include <c>[{CorrelationId}]</c>.
    /// </summary>
    /// <param name="correlationId">
    /// The correlation ID to set. Typically the SQS message ID or a new GUID.
    /// </param>
    /// <returns>
    /// An <see cref="IDisposable"/> that, when disposed, removes the Serilog
    /// <c>LogContext</c> property. Callers should use this in a <c>using</c> block
    /// to ensure the property is cleaned up after the SQS message is processed.
    /// </returns>
    /// <example>
    /// <code>
    /// using (_correlationService.SetCorrelationId(sqsMessage.MessageId))
    /// {
    ///     await _mediator.Send(command, ct);
    /// }
    /// </code>
    /// </example>
    IDisposable SetCorrelationId(string correlationId);
}
