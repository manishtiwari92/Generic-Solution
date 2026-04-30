using IPS.AutoPost.Core.Interfaces;
using Serilog.Context;

namespace IPS.AutoPost.Core.Services;

/// <summary>
/// Manages a per-SQS-message correlation ID stored in <see cref="AsyncLocal{T}"/> storage.
/// Each async execution context (i.e. each SQS message processing task) has its own
/// independent correlation ID, and every log entry in that context automatically includes
/// the ID via Serilog <c>LogContext.PushProperty("CorrelationId", id)</c>.
/// </summary>
public sealed class CorrelationIdService : ICorrelationIdService
{
    // Shared across all instances but scoped per async execution context.
    private static readonly AsyncLocal<string?> _correlationId = new();

    /// <inheritdoc />
    public string GetOrCreateCorrelationId()
    {
        if (string.IsNullOrEmpty(_correlationId.Value))
        {
            _correlationId.Value = Guid.NewGuid().ToString();
        }

        return _correlationId.Value;
    }

    /// <inheritdoc />
    public IDisposable SetCorrelationId(string correlationId)
    {
        _correlationId.Value = correlationId;
        return LogContext.PushProperty("CorrelationId", correlationId);
    }
}
