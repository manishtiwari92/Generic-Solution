using System.Diagnostics;
using IPS.AutoPost.Core.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IPS.AutoPost.Core.Behaviors;

/// <summary>
/// MediatR pipeline behavior that logs the start and end of every command dispatch,
/// including the correlation ID from <see cref="ICorrelationIdService"/> and the
/// elapsed time in milliseconds.
/// </summary>
/// <remarks>
/// Registered once in <c>ServiceCollectionExtensions</c> as an open-generic behavior:
/// <code>
/// services.AddScoped(typeof(IPipelineBehavior&lt;,&gt;), typeof(LoggingBehavior&lt;,&gt;));
/// </code>
/// This wraps every command automatically — no per-command registration needed.
/// The correlation ID is pushed to Serilog <c>LogContext</c> by
/// <see cref="ICorrelationIdService.SetCorrelationId"/> in the SQS worker,
/// so all log entries within a single message processing scope share the same ID.
/// </remarks>
/// <typeparam name="TRequest">The MediatR request type (e.g. <c>ExecutePostCommand</c>).</typeparam>
/// <typeparam name="TResponse">The MediatR response type (e.g. <c>PostBatchResult</c>).</typeparam>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly ICorrelationIdService _correlationService;

    public LoggingBehavior(
        ILogger<LoggingBehavior<TRequest, TResponse>> logger,
        ICorrelationIdService correlationService)
    {
        _logger = logger;
        _correlationService = correlationService;
    }

    /// <summary>
    /// Logs the command name and correlation ID before invoking the next handler,
    /// then logs the elapsed time after the handler returns.
    /// </summary>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var correlationId = _correlationService.GetOrCreateCorrelationId();

        _logger.LogInformation(
            "[{CorrelationId}] Handling {RequestName}",
            correlationId,
            requestName);

        var sw = Stopwatch.StartNew();

        var response = await next(cancellationToken);

        sw.Stop();

        _logger.LogInformation(
            "[{CorrelationId}] Handled {RequestName} in {ElapsedMs}ms",
            correlationId,
            requestName,
            sw.ElapsedMilliseconds);

        return response;
    }
}
