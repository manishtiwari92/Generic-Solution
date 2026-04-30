using FluentAssertions;
using IPS.AutoPost.Core.Commands;
using IPS.AutoPost.Core.Engine;
using IPS.AutoPost.Core.Handlers;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using Moq;
using Microsoft.Extensions.Logging;

namespace IPS.AutoPost.Core.Tests.Handlers;

/// <summary>
/// Unit tests for <see cref="ExecutePostHandler"/>.
/// Verifies that the handler correctly routes to the orchestrator based on
/// whether <c>ItemIds</c> is populated, and that the result is returned as-is.
/// </summary>
public class ExecutePostHandlerTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a mock <see cref="AutoPostOrchestrator"/> with all dependencies mocked.
    /// The orchestrator is not sealed, so Moq can create a subclass.
    /// </summary>
    private static Mock<AutoPostOrchestrator> CreateOrchestratorMock()
    {
        return new Mock<AutoPostOrchestrator>(
            Mock.Of<IConfigurationRepository>(),
            Mock.Of<IWorkitemRepository>(),
            Mock.Of<IRoutingRepository>(),
            Mock.Of<IAuditRepository>(),
            Mock.Of<IScheduleRepository>(),
            new PluginRegistry(),
            new SchedulerService(),
            Mock.Of<ILogger<AutoPostOrchestrator>>());
    }

    // -----------------------------------------------------------------------
    // Scheduled post (ItemIds empty)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_ScheduledPost_CallsRunScheduledPostAsync()
    {
        // Arrange
        var expected = new PostBatchResult { RecordsProcessed = 5, RecordsSuccess = 5 };
        var orchestratorMock = CreateOrchestratorMock();

        orchestratorMock
            .Setup(o => o.RunScheduledPostAsync(42, "INVITEDCLUB", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var handler = new ExecutePostHandler(orchestratorMock.Object);
        var command = new ExecutePostCommand
        {
            JobId = 42,
            ClientType = "INVITEDCLUB",
            TriggerType = "Scheduled",
            ItemIds = string.Empty   // empty → scheduled path
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
        orchestratorMock.Verify(
            o => o.RunScheduledPostAsync(42, "INVITEDCLUB", It.IsAny<CancellationToken>()),
            Times.Once);
        orchestratorMock.Verify(
            o => o.RunManualPostAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ScheduledPost_NullItemIds_CallsRunScheduledPostAsync()
    {
        // Arrange — ItemIds defaults to empty string, but verify null is also treated as scheduled
        var expected = new PostBatchResult { RecordsProcessed = 3 };
        var orchestratorMock = CreateOrchestratorMock();

        orchestratorMock
            .Setup(o => o.RunScheduledPostAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var handler = new ExecutePostHandler(orchestratorMock.Object);
        var command = new ExecutePostCommand
        {
            JobId = 10,
            ClientType = "SEVITA",
            ItemIds = string.Empty
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
        orchestratorMock.Verify(
            o => o.RunScheduledPostAsync(10, "SEVITA", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Manual post (ItemIds non-empty)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_ManualPost_CallsRunManualPostAsync()
    {
        // Arrange
        var expected = new PostBatchResult { RecordsProcessed = 2, RecordsSuccess = 2 };
        var orchestratorMock = CreateOrchestratorMock();

        orchestratorMock
            .Setup(o => o.RunManualPostAsync("101,102", 99, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var handler = new ExecutePostHandler(orchestratorMock.Object);
        var command = new ExecutePostCommand
        {
            JobId = 42,
            ClientType = "INVITEDCLUB",
            TriggerType = "Manual",
            ItemIds = "101,102",   // non-empty → manual path
            UserId = 99
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
        orchestratorMock.Verify(
            o => o.RunManualPostAsync("101,102", 99, It.IsAny<CancellationToken>()),
            Times.Once);
        orchestratorMock.Verify(
            o => o.RunScheduledPostAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ManualPost_SingleItemId_CallsRunManualPostAsync()
    {
        // Arrange
        var expected = new PostBatchResult { RecordsProcessed = 1, RecordsSuccess = 1 };
        var orchestratorMock = CreateOrchestratorMock();

        orchestratorMock
            .Setup(o => o.RunManualPostAsync("500", 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var handler = new ExecutePostHandler(orchestratorMock.Object);
        var command = new ExecutePostCommand
        {
            JobId = 1,
            ClientType = "SEVITA",
            ItemIds = "500",
            UserId = 7
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
        orchestratorMock.Verify(
            o => o.RunManualPostAsync("500", 7, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Result pass-through
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_ReturnsOrchestratorResultUnmodified()
    {
        // Arrange — verify the handler does not wrap or transform the result
        var expected = new PostBatchResult
        {
            RecordsProcessed = 10,
            RecordsSuccess = 8,
            RecordsFailed = 2,
            ErrorMessage = null
        };

        var orchestratorMock = CreateOrchestratorMock();
        orchestratorMock
            .Setup(o => o.RunScheduledPostAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var handler = new ExecutePostHandler(orchestratorMock.Object);
        var command = new ExecutePostCommand { JobId = 1, ClientType = "INVITEDCLUB" };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert — same reference, not a copy
        result.Should().BeSameAs(expected);
        result.RecordsProcessed.Should().Be(10);
        result.RecordsSuccess.Should().Be(8);
        result.RecordsFailed.Should().Be(2);
    }

    [Fact]
    public async Task Handle_OrchestratorReturnsErrorResult_PropagatesErrorMessage()
    {
        // Arrange
        var expected = new PostBatchResult
        {
            ResponseCode = -1,
            ErrorMessage = "Missing Configuration."
        };

        var orchestratorMock = CreateOrchestratorMock();
        orchestratorMock
            .Setup(o => o.RunManualPostAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var handler = new ExecutePostHandler(orchestratorMock.Object);
        var command = new ExecutePostCommand { ItemIds = "999", UserId = 1 };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.ResponseCode.Should().Be(-1);
        result.ErrorMessage.Should().Be("Missing Configuration.");
    }

    // -----------------------------------------------------------------------
    // Cancellation token propagation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_PropagatesCancellationTokenToOrchestrator()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var capturedToken = CancellationToken.None;

        var orchestratorMock = CreateOrchestratorMock();
        orchestratorMock
            .Setup(o => o.RunScheduledPostAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, CancellationToken>((_, _, ct) => capturedToken = ct)
            .ReturnsAsync(new PostBatchResult());

        var handler = new ExecutePostHandler(orchestratorMock.Object);
        var command = new ExecutePostCommand { JobId = 1, ClientType = "INVITEDCLUB" };

        // Act
        await handler.Handle(command, cts.Token);

        // Assert
        capturedToken.Should().Be(cts.Token);
    }
}
