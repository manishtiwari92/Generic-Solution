using FluentAssertions;
using IPS.AutoPost.Core.Behaviors;
using IPS.AutoPost.Core.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;

namespace IPS.AutoPost.Core.Tests.Behaviors;

// -----------------------------------------------------------------------
// Test request/response types — must be public (top-level) so Moq can
// create proxies for ILogger<LoggingBehavior<TRequest, TResponse>>.
// -----------------------------------------------------------------------

public record LoggingTestRequest : IRequest<LoggingTestResponse>;
public record LoggingTestResponse(string Value);

/// <summary>
/// Unit tests for <see cref="LoggingBehavior{TRequest,TResponse}"/>.
/// Verifies that log entries are written before and after the handler executes,
/// that the correlation ID is included, and that the handler result is returned
/// unmodified.
/// </summary>
public class LoggingBehaviorTests
{
    // -----------------------------------------------------------------------
    // Fake logger — avoids Moq proxy issues with strong-named assemblies
    // -----------------------------------------------------------------------

    /// <summary>
    /// Simple in-process logger that captures log entries for assertion.
    /// Avoids Moq proxy issues with <c>ILogger&lt;T&gt;</c> when T contains
    /// private nested types.
    /// </summary>
    private sealed class FakeLogger : ILogger<LoggingBehavior<LoggingTestRequest, LoggingTestResponse>>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private static ICorrelationIdService CreateCorrelationService(string correlationId = "test-correlation-id")
    {
        var mock = new Mock<ICorrelationIdService>();
        mock.Setup(s => s.GetOrCreateCorrelationId()).Returns(correlationId);
        return mock.Object;
    }

    private static LoggingBehavior<LoggingTestRequest, LoggingTestResponse> CreateBehavior(
        FakeLogger logger,
        string correlationId = "test-id")
        => new(logger, CreateCorrelationService(correlationId));

    // -----------------------------------------------------------------------
    // Log entries written before and after handler
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WritesLogEntryBeforeHandlerExecutes()
    {
        // Arrange
        var logger = new FakeLogger();
        var behavior = CreateBehavior(logger, "abc-123");

        var handlerExecuted = false;
        RequestHandlerDelegate<LoggingTestResponse> next = _ =>
        {
            handlerExecuted = true;
            return Task.FromResult(new LoggingTestResponse("ok"));
        };

        // Act
        await behavior.Handle(new LoggingTestRequest(), next, CancellationToken.None);

        // Assert — a "Handling" entry must exist
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("Handling") &&
            e.Message.Contains("LoggingTestRequest"),
            because: "a log entry must be written before the handler executes");

        handlerExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WritesLogEntryAfterHandlerExecutes()
    {
        // Arrange
        var logger = new FakeLogger();
        var behavior = CreateBehavior(logger, "abc-123");

        RequestHandlerDelegate<LoggingTestResponse> next = _ =>
            Task.FromResult(new LoggingTestResponse("done"));

        // Act
        await behavior.Handle(new LoggingTestRequest(), next, CancellationToken.None);

        // Assert — a "Handled" entry must appear after the handler completes
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("Handled") &&
            e.Message.Contains("LoggingTestRequest"),
            because: "a log entry must be written after the handler completes");
    }

    [Fact]
    public async Task Handle_WritesTwoLogEntries_BeforeAndAfter()
    {
        // Arrange
        var logger = new FakeLogger();
        var behavior = CreateBehavior(logger);

        RequestHandlerDelegate<LoggingTestResponse> next = _ =>
            Task.FromResult(new LoggingTestResponse("result"));

        // Act
        await behavior.Handle(new LoggingTestRequest(), next, CancellationToken.None);

        // Assert — exactly two Information entries (start + end)
        var infoEntries = logger.Entries
            .Where(e => e.Level == LogLevel.Information)
            .ToList();

        infoEntries.Should().HaveCount(2,
            because: "LoggingBehavior writes exactly one log entry before and one after the handler");
    }

    // -----------------------------------------------------------------------
    // Correlation ID included in log entries
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_IncludesCorrelationIdInLogEntries()
    {
        // Arrange
        const string correlationId = "corr-xyz-789";
        var logger = new FakeLogger();
        var behavior = CreateBehavior(logger, correlationId);

        RequestHandlerDelegate<LoggingTestResponse> next = _ =>
            Task.FromResult(new LoggingTestResponse("ok"));

        // Act
        await behavior.Handle(new LoggingTestRequest(), next, CancellationToken.None);

        // Assert — both log entries must contain the correlation ID
        logger.Entries.Should().AllSatisfy(e =>
            e.Message.Should().Contain(correlationId,
                because: "every log entry must include the correlation ID"));
    }

    [Fact]
    public async Task Handle_CallsGetOrCreateCorrelationId_Once()
    {
        // Arrange
        var correlationMock = new Mock<ICorrelationIdService>();
        correlationMock.Setup(s => s.GetOrCreateCorrelationId()).Returns("id-1");

        var behavior = new LoggingBehavior<LoggingTestRequest, LoggingTestResponse>(
            new FakeLogger(),
            correlationMock.Object);

        RequestHandlerDelegate<LoggingTestResponse> next = _ =>
            Task.FromResult(new LoggingTestResponse("ok"));

        // Act
        await behavior.Handle(new LoggingTestRequest(), next, CancellationToken.None);

        // Assert — correlation ID is fetched once per command dispatch
        correlationMock.Verify(s => s.GetOrCreateCorrelationId(), Times.Once);
    }

    // -----------------------------------------------------------------------
    // Handler result is returned unmodified
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_ReturnsHandlerResultUnmodified()
    {
        // Arrange
        var expected = new LoggingTestResponse("expected-value");
        var behavior = CreateBehavior(new FakeLogger());

        RequestHandlerDelegate<LoggingTestResponse> next = _ => Task.FromResult(expected);

        // Act
        var result = await behavior.Handle(new LoggingTestRequest(), next, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
    }

    // -----------------------------------------------------------------------
    // Handler exception propagates (behavior does not swallow exceptions)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_HandlerThrows_ExceptionPropagates()
    {
        // Arrange
        var behavior = CreateBehavior(new FakeLogger());

        RequestHandlerDelegate<LoggingTestResponse> next = _ =>
            throw new InvalidOperationException("handler error");

        // Act
        var act = async () => await behavior.Handle(new LoggingTestRequest(), next, CancellationToken.None);

        // Assert — behavior must not swallow exceptions
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("handler error");
    }

    // -----------------------------------------------------------------------
    // Elapsed time is logged
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_LogsElapsedTimeInCompletionEntry()
    {
        // Arrange
        var logger = new FakeLogger();
        var behavior = CreateBehavior(logger, "timer-test");

        RequestHandlerDelegate<LoggingTestResponse> next = async _ =>
        {
            await Task.Delay(10); // small delay so elapsed > 0
            return new LoggingTestResponse("done");
        };

        // Act
        await behavior.Handle(new LoggingTestRequest(), next, CancellationToken.None);

        // Assert — the completion log entry must contain elapsed time in ms
        var completionEntry = logger.Entries
            .FirstOrDefault(e => e.Message.Contains("Handled"));

        completionEntry.Message.Should().Contain("ms",
            because: "the completion log entry must include elapsed time in milliseconds");
    }
}
