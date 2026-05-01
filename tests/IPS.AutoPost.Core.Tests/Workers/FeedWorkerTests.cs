using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Host.FeedWorker;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace IPS.AutoPost.Core.Tests.Workers;

/// <summary>
/// Unit tests for <see cref="FeedWorker"/>.
/// Verifies the three core behaviours:
/// <list type="number">
///   <item>Successful message processing → message is deleted from the queue.</item>
///   <item>Exception during processing → message is NOT deleted (stays for retry/DLQ).</item>
///   <item>A new DI scope is created for each message.</item>
/// </list>
/// </summary>
public class FeedWorkerTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private const string TestQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/ips-feed-queue-test";

    /// <summary>
    /// Builds a minimal <see cref="IConfiguration"/> with the SQS_QUEUE_URL key set.
    /// </summary>
    private static IConfiguration BuildConfiguration(string queueUrl = TestQueueUrl)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SQS_QUEUE_URL"] = queueUrl
            })
            .Build();

    /// <summary>
    /// Serializes a valid <see cref="SqsMessagePayload"/> to JSON.
    /// </summary>
    private static string BuildPayloadJson(int jobId = 1, string clientType = "INVITEDCLUB")
        => JsonSerializer.Serialize(new SqsMessagePayload
        {
            JobId = jobId,
            ClientType = clientType,
            Pipeline = "Feed",
            TriggerType = "Scheduled"
        });

    /// <summary>
    /// Creates a mock SQS <see cref="Message"/> with the given body.
    /// </summary>
    private static Message BuildSqsMessage(string body, string messageId = "msg-001", string receiptHandle = "rh-001")
        => new()
        {
            MessageId     = messageId,
            ReceiptHandle = receiptHandle,
            Body          = body
        };

    /// <summary>
    /// Creates a mock <see cref="IAmazonSQS"/> that returns the given messages on the first
    /// <c>ReceiveMessageAsync</c> call, then returns an empty list on all subsequent calls
    /// (so the worker loop terminates cleanly when the cancellation token fires).
    /// </summary>
    private static Mock<IAmazonSQS> BuildSqsMock(params Message[] messages)
    {
        var sqsMock = new Mock<IAmazonSQS>();
        var callCount = 0;

        sqsMock
            .Setup(s => s.ReceiveMessageAsync(
                It.IsAny<ReceiveMessageRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new ReceiveMessageResponse { Messages = messages.ToList() }
                    : new ReceiveMessageResponse { Messages = [] };
            });

        return sqsMock;
    }

    /// <summary>
    /// Builds a <see cref="ServiceProvider"/> with a mock <see cref="IMediator"/> and
    /// <see cref="ICorrelationIdService"/> registered as scoped services.
    /// Returns the provider and the mediator mock for assertion.
    /// </summary>
    private static (IServiceProvider provider, Mock<IMediator> mediatorMock) BuildServiceProvider(
        FeedResult? feedResult = null,
        Exception? mediatorException = null)
    {
        var mediatorMock = new Mock<IMediator>();

        if (mediatorException is not null)
        {
            mediatorMock
                .Setup(m => m.Send(It.IsAny<IRequest<FeedResult>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(mediatorException);
        }
        else
        {
            mediatorMock
                .Setup(m => m.Send(It.IsAny<IRequest<FeedResult>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(feedResult ?? FeedResult.Succeeded(10));
        }

        var correlationMock = new Mock<ICorrelationIdService>();
        correlationMock
            .Setup(c => c.SetCorrelationId(It.IsAny<string>()))
            .Returns(Mock.Of<IDisposable>());
        correlationMock
            .Setup(c => c.GetOrCreateCorrelationId())
            .Returns("test-correlation-id");

        var services = new ServiceCollection();
        services.AddScoped<IMediator>(_ => mediatorMock.Object);
        services.AddScoped<ICorrelationIdService>(_ => correlationMock.Object);
        services.AddLogging();

        return (services.BuildServiceProvider(), mediatorMock);
    }

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_ThrowsWhenSqsQueueUrlMissing()
    {
        // Arrange
        var emptyConfig = new ConfigurationBuilder().Build();
        var (provider, _) = BuildServiceProvider();

        // Act
        var act = () => new FeedWorker(
            provider,
            Mock.Of<IAmazonSQS>(),
            Mock.Of<ILogger<FeedWorker>>(),
            emptyConfig);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SQS_QUEUE_URL*");
    }

    // -----------------------------------------------------------------------
    // 18.3a: Successful message processing → message is deleted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_SuccessfulMessage_DeletesMessageFromQueue()
    {
        // Arrange
        var payload = BuildPayloadJson(jobId: 42, clientType: "INVITEDCLUB");
        var message = BuildSqsMessage(payload, messageId: "msg-success-001", receiptHandle: "rh-success-001");

        var sqsMock = BuildSqsMock(message);
        var (provider, mediatorMock) = BuildServiceProvider(FeedResult.Succeeded(5));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new FeedWorker(
            provider,
            sqsMock.Object,
            Mock.Of<ILogger<FeedWorker>>(),
            BuildConfiguration());

        // Act — run the worker until the CTS fires
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert — mediator was called with the correct command
        mediatorMock.Verify(
            m => m.Send(
                It.Is<IRequest<FeedResult>>(cmd =>
                    cmd.GetType() == typeof(IPS.AutoPost.Core.Commands.ExecuteFeedCommand) &&
                    ((IPS.AutoPost.Core.Commands.ExecuteFeedCommand)cmd).JobId == 42 &&
                    ((IPS.AutoPost.Core.Commands.ExecuteFeedCommand)cmd).ClientType == "INVITEDCLUB"),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "IMediator.Send must be called with the deserialized ExecuteFeedCommand");

        // Assert — message was deleted after successful processing
        sqsMock.Verify(
            s => s.DeleteMessageAsync(
                TestQueueUrl,
                "rh-success-001",
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "DeleteMessageAsync must be called after successful message processing");
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulMessage_SendsCorrectCommandFields()
    {
        // Arrange — verify all command fields are mapped from the payload
        var payload = JsonSerializer.Serialize(new SqsMessagePayload
        {
            JobId = 99,
            ClientType = "SEVITA",
            Pipeline = "Feed",
            TriggerType = "Scheduled",
            Mode = "DryRun"
        });
        var message = BuildSqsMessage(payload);

        IPS.AutoPost.Core.Commands.ExecuteFeedCommand? capturedCommand = null;
        var mediatorMock = new Mock<IMediator>();
        mediatorMock
            .Setup(m => m.Send(It.IsAny<IRequest<FeedResult>>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<FeedResult>, CancellationToken>((cmd, _) =>
                capturedCommand = cmd as IPS.AutoPost.Core.Commands.ExecuteFeedCommand)
            .ReturnsAsync(FeedResult.Succeeded(0));

        var correlationMock = new Mock<ICorrelationIdService>();
        correlationMock.Setup(c => c.SetCorrelationId(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        var services = new ServiceCollection();
        services.AddScoped<IMediator>(_ => mediatorMock.Object);
        services.AddScoped<ICorrelationIdService>(_ => correlationMock.Object);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var sqsMock = BuildSqsMock(message);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new FeedWorker(
            provider,
            sqsMock.Object,
            Mock.Of<ILogger<FeedWorker>>(),
            BuildConfiguration());

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        capturedCommand.Should().NotBeNull();
        capturedCommand!.JobId.Should().Be(99);
        capturedCommand.ClientType.Should().Be("SEVITA");
        capturedCommand.TriggerType.Should().Be("Scheduled");
        capturedCommand.Mode.Should().Be("DryRun");
    }

    // -----------------------------------------------------------------------
    // 18.3b: Exception during processing → message is NOT deleted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_MediatorThrows_DoesNotDeleteMessage()
    {
        // Arrange
        var payload = BuildPayloadJson(jobId: 10, clientType: "INVITEDCLUB");
        var message = BuildSqsMessage(payload, messageId: "msg-fail-001", receiptHandle: "rh-fail-001");

        var sqsMock = BuildSqsMock(message);
        var (provider, _) = BuildServiceProvider(
            mediatorException: new InvalidOperationException("Simulated feed failure"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new FeedWorker(
            provider,
            sqsMock.Object,
            Mock.Of<ILogger<FeedWorker>>(),
            BuildConfiguration());

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert — message must NOT be deleted when processing fails
        sqsMock.Verify(
            s => s.DeleteMessageAsync(
                TestQueueUrl,
                "rh-fail-001",
                It.IsAny<CancellationToken>()),
            Times.Never,
            "DeleteMessageAsync must NOT be called when message processing throws an exception");
    }

    [Fact]
    public async Task ExecuteAsync_MalformedJson_DeletesMessageToPreventDlqCycling()
    {
        // Arrange — malformed JSON should be deleted to prevent infinite DLQ cycling
        var message = BuildSqsMessage("{ this is not valid json }", receiptHandle: "rh-malformed-001");

        var sqsMock = BuildSqsMock(message);
        var (provider, mediatorMock) = BuildServiceProvider();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new FeedWorker(
            provider,
            sqsMock.Object,
            Mock.Of<ILogger<FeedWorker>>(),
            BuildConfiguration());

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert — malformed message is deleted (not retried)
        sqsMock.Verify(
            s => s.DeleteMessageAsync(
                TestQueueUrl,
                "rh-malformed-001",
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "Malformed messages must be deleted to prevent infinite DLQ cycling");

        // Assert — mediator was never called for a malformed message
        mediatorMock.Verify(
            m => m.Send(It.IsAny<IRequest<FeedResult>>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "IMediator.Send must not be called for malformed messages");
    }

    // -----------------------------------------------------------------------
    // 18.3c: New DI scope created per message
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CreatesNewScopePerMessage()
    {
        // Arrange — track how many scopes are created by using a custom service provider
        var scopeCreationCount = 0;

        var mediatorMock = new Mock<IMediator>();
        mediatorMock
            .Setup(m => m.Send(It.IsAny<IRequest<FeedResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FeedResult.Succeeded(1));

        var correlationMock = new Mock<ICorrelationIdService>();
        correlationMock.Setup(c => c.SetCorrelationId(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        // Use a spy service provider that counts CreateAsyncScope calls
        var innerServices = new ServiceCollection();
        innerServices.AddScoped<IMediator>(_ => mediatorMock.Object);
        innerServices.AddScoped<ICorrelationIdService>(_ => correlationMock.Object);
        innerServices.AddLogging();
        var innerProvider = innerServices.BuildServiceProvider();

        var spyProvider = new ScopeCountingServiceProvider(innerProvider, () => scopeCreationCount++);

        // Two messages in the batch
        var msg1 = BuildSqsMessage(BuildPayloadJson(1), "msg-1", "rh-1");
        var msg2 = BuildSqsMessage(BuildPayloadJson(2), "msg-2", "rh-2");
        var sqsMock = BuildSqsMock(msg1, msg2);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new FeedWorker(
            spyProvider,
            sqsMock.Object,
            Mock.Of<ILogger<FeedWorker>>(),
            BuildConfiguration());

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(600), CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert — one scope per message (2 messages → at least 2 scopes)
        scopeCreationCount.Should().BeGreaterThanOrEqualTo(2,
            because: "a new DI scope must be created for each SQS message to isolate scoped services");
    }

    [Fact]
    public async Task ExecuteAsync_MultipleMessages_EachProcessedIndependently()
    {
        // Arrange — two messages, both should be deleted on success
        var msg1 = BuildSqsMessage(BuildPayloadJson(1, "INVITEDCLUB"), "msg-a", "rh-a");
        var msg2 = BuildSqsMessage(BuildPayloadJson(2, "SEVITA"), "msg-b", "rh-b");

        var sqsMock = BuildSqsMock(msg1, msg2);
        var (provider, mediatorMock) = BuildServiceProvider(FeedResult.Succeeded(3));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new FeedWorker(
            provider,
            sqsMock.Object,
            Mock.Of<ILogger<FeedWorker>>(),
            BuildConfiguration());

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(600), CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert — both messages deleted
        sqsMock.Verify(
            s => s.DeleteMessageAsync(TestQueueUrl, "rh-a", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "First message must be deleted after successful processing");

        sqsMock.Verify(
            s => s.DeleteMessageAsync(TestQueueUrl, "rh-b", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "Second message must be deleted after successful processing");
    }

    // -----------------------------------------------------------------------
    // Correlation ID is set per message
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_SetsCorrelationIdFromMessageId()
    {
        // Arrange
        var payload = BuildPayloadJson();
        var message = BuildSqsMessage(payload, messageId: "unique-message-id-xyz");

        var sqsMock = BuildSqsMock(message);

        string? capturedCorrelationId = null;
        var correlationMock = new Mock<ICorrelationIdService>();
        correlationMock
            .Setup(c => c.SetCorrelationId(It.IsAny<string>()))
            .Callback<string>(id => capturedCorrelationId = id)
            .Returns(Mock.Of<IDisposable>());

        var mediatorMock = new Mock<IMediator>();
        mediatorMock
            .Setup(m => m.Send(It.IsAny<IRequest<FeedResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FeedResult.Succeeded(0));

        var services = new ServiceCollection();
        services.AddScoped<IMediator>(_ => mediatorMock.Object);
        services.AddScoped<ICorrelationIdService>(_ => correlationMock.Object);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new FeedWorker(
            provider,
            sqsMock.Object,
            Mock.Of<ILogger<FeedWorker>>(),
            BuildConfiguration());

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert — correlation ID is set to the SQS MessageId
        capturedCorrelationId.Should().Be("unique-message-id-xyz",
            because: "the SQS MessageId must be used as the correlation ID for log tracing");
    }

    // -----------------------------------------------------------------------
    // FeedResult.NotApplicable — still deletes the message (plugin has no feed)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_FeedResultNotApplicable_StillDeletesMessage()
    {
        // Arrange — Sevita returns NotApplicable; the message should still be deleted
        var payload = BuildPayloadJson(clientType: "SEVITA");
        var message = BuildSqsMessage(payload, receiptHandle: "rh-na-001");

        var sqsMock = BuildSqsMock(message);
        var (provider, _) = BuildServiceProvider(FeedResult.NotApplicable());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new FeedWorker(
            provider,
            sqsMock.Object,
            Mock.Of<ILogger<FeedWorker>>(),
            BuildConfiguration());

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert — message is deleted even when the plugin has no feed download
        sqsMock.Verify(
            s => s.DeleteMessageAsync(TestQueueUrl, "rh-na-001", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "Message must be deleted even when FeedResult.NotApplicable is returned");
    }
}

// -----------------------------------------------------------------------
// Test helpers
// -----------------------------------------------------------------------

/// <summary>
/// A spy <see cref="IServiceProvider"/> that counts how many async scopes are created.
/// Used to verify that <see cref="FeedWorker"/> creates one scope per SQS message.
/// </summary>
file sealed class ScopeCountingServiceProvider : IServiceProvider, IServiceScopeFactory
{
    private readonly IServiceProvider _inner;
    private readonly Action _onScopeCreated;

    public ScopeCountingServiceProvider(IServiceProvider inner, Action onScopeCreated)
    {
        _inner = inner;
        _onScopeCreated = onScopeCreated;
    }

    public object? GetService(Type serviceType)
    {
        // Intercept IServiceScopeFactory requests so we can count scope creations
        if (serviceType == typeof(IServiceScopeFactory))
            return this;

        return _inner.GetService(serviceType);
    }

    // IServiceScopeFactory
    public IServiceScope CreateScope()
    {
        _onScopeCreated();
        return _inner.GetRequiredService<IServiceScopeFactory>().CreateScope();
    }
}
