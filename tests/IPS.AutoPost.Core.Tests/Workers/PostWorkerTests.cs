using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using IPS.AutoPost.Core.Commands;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Host.PostWorker;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace IPS.AutoPost.Core.Tests.Workers;

/// <summary>
/// Unit tests for <see cref="PostWorker"/>.
/// Verifies the three core behaviours:
/// <list type="number">
///   <item>Successful message processing → message is deleted from the queue.</item>
///   <item>Exception during processing → message is NOT deleted (stays for retry/DLQ).</item>
///   <item>The <c>Mode</c> field from the SQS payload is passed through to <see cref="ExecutePostCommand"/>.</item>
/// </list>
/// </summary>
public class PostWorkerTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private const string TestQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/ips-post-queue-test";

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
    private static string BuildPayloadJson(
        int jobId = 1,
        string clientType = "INVITEDCLUB",
        string triggerType = "Scheduled",
        string? itemIds = null,
        int? userId = null,
        string? mode = null)
        => JsonSerializer.Serialize(new SqsMessagePayload
        {
            JobId       = jobId,
            ClientType  = clientType,
            Pipeline    = "Post",
            TriggerType = triggerType,
            ItemIds     = itemIds,
            UserId      = userId,
            Mode        = mode
        });

    /// <summary>
    /// Creates a mock SQS <see cref="Message"/> with the given body.
    /// </summary>
    private static Message BuildSqsMessage(
        string body,
        string messageId = "msg-001",
        string receiptHandle = "rh-001")
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
        PostBatchResult? postResult = null,
        Exception? mediatorException = null)
    {
        var mediatorMock = new Mock<IMediator>();

        if (mediatorException is not null)
        {
            mediatorMock
                .Setup(m => m.Send(It.IsAny<IRequest<PostBatchResult>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(mediatorException);
        }
        else
        {
            mediatorMock
                .Setup(m => m.Send(It.IsAny<IRequest<PostBatchResult>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(postResult ?? new PostBatchResult
                {
                    RecordsProcessed = 1,
                    RecordsSuccess   = 1,
                    RecordsFailed    = 0
                });
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
        var act = () => new PostWorker(
            provider,
            Mock.Of<IAmazonSQS>(),
            Mock.Of<ILogger<PostWorker>>(),
            emptyConfig);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SQS_QUEUE_URL*");
    }

    // -----------------------------------------------------------------------
    // 19.3a: Successful message processing → message is deleted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_SuccessfulMessage_DeletesMessageFromQueue()
    {
        // Arrange
        var payload = BuildPayloadJson(jobId: 42, clientType: "INVITEDCLUB");
        var message = BuildSqsMessage(payload, messageId: "msg-success-001", receiptHandle: "rh-success-001");

        var sqsMock = BuildSqsMock(message);
        var (provider, mediatorMock) = BuildServiceProvider(new PostBatchResult
        {
            RecordsProcessed = 1,
            RecordsSuccess   = 1,
            RecordsFailed    = 0
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new PostWorker(
            provider,
            sqsMock.Object,
            Mock.Of<ILogger<PostWorker>>(),
            BuildConfiguration());

        // Act — run the worker until the CTS fires
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert — mediator was called with the correct command
        mediatorMock.Verify(
            m => m.Send(
                It.Is<IRequest<PostBatchResult>>(cmd =>
                    cmd.GetType() == typeof(ExecutePostCommand) &&
                    ((ExecutePostCommand)cmd).JobId == 42 &&
                    ((ExecutePostCommand)cmd).ClientType == "INVITEDCLUB"),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "IMediator.Send must be called with the deserialized ExecutePostCommand");

        // Assert — message was deleted after successful processing
        sqsMock.Verify(
            s => s.DeleteMessageAsync(
                TestQueueUrl,
                "rh-success-001",
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "DeleteMessageAsync must be called after successful message processing");
    }

    // -----------------------------------------------------------------------
    // 19.3b: Exception during processing → message is NOT deleted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_MediatorThrows_DoesNotDeleteMessage()
    {
        // Arrange
        var payload = BuildPayloadJson(jobId: 10, clientType: "INVITEDCLUB");
        var message = BuildSqsMessage(payload, messageId: "msg-fail-001", receiptHandle: "rh-fail-001");

        var sqsMock = BuildSqsMock(message);
        var (provider, _) = BuildServiceProvider(
            mediatorException: new InvalidOperationException("Simulated post failure"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new PostWorker(
            provider,
            sqsMock.Object,
            Mock.Of<ILogger<PostWorker>>(),
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

    // -----------------------------------------------------------------------
    // 19.3c: Mode field is passed through to ExecutePostCommand
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ModeFieldPassedThroughToCommand()
    {
        // Arrange — payload carries Mode = "DryRun"
        var payload = BuildPayloadJson(
            jobId: 99,
            clientType: "SEVITA",
            triggerType: "Scheduled",
            mode: "DryRun");
        var message = BuildSqsMessage(payload);

        ExecutePostCommand? capturedCommand = null;
        var mediatorMock = new Mock<IMediator>();
        mediatorMock
            .Setup(m => m.Send(It.IsAny<IRequest<PostBatchResult>>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<PostBatchResult>, CancellationToken>((cmd, _) =>
                capturedCommand = cmd as ExecutePostCommand)
            .ReturnsAsync(new PostBatchResult { RecordsProcessed = 0 });

        var correlationMock = new Mock<ICorrelationIdService>();
        correlationMock.Setup(c => c.SetCorrelationId(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        var services = new ServiceCollection();
        services.AddScoped<IMediator>(_ => mediatorMock.Object);
        services.AddScoped<ICorrelationIdService>(_ => correlationMock.Object);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var sqsMock = BuildSqsMock(message);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new PostWorker(
            provider,
            sqsMock.Object,
            Mock.Of<ILogger<PostWorker>>(),
            BuildConfiguration());

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert — all payload fields are correctly mapped to the command
        capturedCommand.Should().NotBeNull();
        capturedCommand!.JobId.Should().Be(99);
        capturedCommand.ClientType.Should().Be("SEVITA");
        capturedCommand.TriggerType.Should().Be("Scheduled");
        capturedCommand.Mode.Should().Be("DryRun",
            because: "the Mode field from the SQS payload must be passed through to ExecutePostCommand");
    }

    [Fact]
    public async Task ExecuteAsync_ManualPost_ItemIdsAndUserIdPassedThroughToCommand()
    {
        // Arrange — manual post payload with ItemIds and UserId
        var payload = BuildPayloadJson(
            jobId: 5,
            clientType: "INVITEDCLUB",
            triggerType: "Manual",
            itemIds: "101,102,103",
            userId: 42);
        var message = BuildSqsMessage(payload);

        ExecutePostCommand? capturedCommand = null;
        var mediatorMock = new Mock<IMediator>();
        mediatorMock
            .Setup(m => m.Send(It.IsAny<IRequest<PostBatchResult>>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<PostBatchResult>, CancellationToken>((cmd, _) =>
                capturedCommand = cmd as ExecutePostCommand)
            .ReturnsAsync(new PostBatchResult { RecordsProcessed = 3, RecordsSuccess = 3 });

        var correlationMock = new Mock<ICorrelationIdService>();
        correlationMock.Setup(c => c.SetCorrelationId(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        var services = new ServiceCollection();
        services.AddScoped<IMediator>(_ => mediatorMock.Object);
        services.AddScoped<ICorrelationIdService>(_ => correlationMock.Object);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var sqsMock = BuildSqsMock(message);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new PostWorker(
            provider,
            sqsMock.Object,
            Mock.Of<ILogger<PostWorker>>(),
            BuildConfiguration());

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        capturedCommand.Should().NotBeNull();
        capturedCommand!.TriggerType.Should().Be("Manual");
        capturedCommand.ItemIds.Should().Be("101,102,103",
            because: "ItemIds from the SQS payload must be passed through for manual posts");
        capturedCommand.UserId.Should().Be(42,
            because: "UserId from the SQS payload must be passed through for manual posts");
    }

    // -----------------------------------------------------------------------
    // Malformed JSON → deleted to prevent DLQ cycling
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_MalformedJson_DeletesMessageToPreventDlqCycling()
    {
        // Arrange — malformed JSON should be deleted to prevent infinite DLQ cycling
        var message = BuildSqsMessage("{ this is not valid json }", receiptHandle: "rh-malformed-001");

        var sqsMock = BuildSqsMock(message);
        var (provider, mediatorMock) = BuildServiceProvider();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new PostWorker(
            provider,
            sqsMock.Object,
            Mock.Of<ILogger<PostWorker>>(),
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
            m => m.Send(It.IsAny<IRequest<PostBatchResult>>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "IMediator.Send must not be called for malformed messages");
    }

    // -----------------------------------------------------------------------
    // New DI scope created per message
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CreatesNewScopePerMessage()
    {
        // Arrange — track how many scopes are created by using a custom service provider
        var scopeCreationCount = 0;

        var mediatorMock = new Mock<IMediator>();
        mediatorMock
            .Setup(m => m.Send(It.IsAny<IRequest<PostBatchResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PostBatchResult { RecordsProcessed = 1, RecordsSuccess = 1 });

        var correlationMock = new Mock<ICorrelationIdService>();
        correlationMock.Setup(c => c.SetCorrelationId(It.IsAny<string>())).Returns(Mock.Of<IDisposable>());

        var innerServices = new ServiceCollection();
        innerServices.AddScoped<IMediator>(_ => mediatorMock.Object);
        innerServices.AddScoped<ICorrelationIdService>(_ => correlationMock.Object);
        innerServices.AddLogging();
        var innerProvider = innerServices.BuildServiceProvider();

        var spyProvider = new PostScopeCountingServiceProvider(innerProvider, () => scopeCreationCount++);

        // Two messages in the batch
        var msg1 = BuildSqsMessage(BuildPayloadJson(1), "msg-1", "rh-1");
        var msg2 = BuildSqsMessage(BuildPayloadJson(2), "msg-2", "rh-2");
        var sqsMock = BuildSqsMock(msg1, msg2);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new PostWorker(
            spyProvider,
            sqsMock.Object,
            Mock.Of<ILogger<PostWorker>>(),
            BuildConfiguration());

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(600), CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert — one scope per message (2 messages → at least 2 scopes)
        scopeCreationCount.Should().BeGreaterThanOrEqualTo(2,
            because: "a new DI scope must be created for each SQS message to isolate scoped services");
    }

    // -----------------------------------------------------------------------
    // Multiple messages — each processed independently
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_MultipleMessages_EachProcessedIndependently()
    {
        // Arrange — two messages, both should be deleted on success
        var msg1 = BuildSqsMessage(BuildPayloadJson(1, "INVITEDCLUB"), "msg-a", "rh-a");
        var msg2 = BuildSqsMessage(BuildPayloadJson(2, "SEVITA"), "msg-b", "rh-b");

        var sqsMock = BuildSqsMock(msg1, msg2);
        var (provider, mediatorMock) = BuildServiceProvider(new PostBatchResult
        {
            RecordsProcessed = 1,
            RecordsSuccess   = 1
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new PostWorker(
            provider,
            sqsMock.Object,
            Mock.Of<ILogger<PostWorker>>(),
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
        var message = BuildSqsMessage(payload, messageId: "unique-post-message-id-xyz");

        var sqsMock = BuildSqsMock(message);

        string? capturedCorrelationId = null;
        var correlationMock = new Mock<ICorrelationIdService>();
        correlationMock
            .Setup(c => c.SetCorrelationId(It.IsAny<string>()))
            .Callback<string>(id => capturedCorrelationId = id)
            .Returns(Mock.Of<IDisposable>());

        var mediatorMock = new Mock<IMediator>();
        mediatorMock
            .Setup(m => m.Send(It.IsAny<IRequest<PostBatchResult>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PostBatchResult { RecordsProcessed = 0 });

        var services = new ServiceCollection();
        services.AddScoped<IMediator>(_ => mediatorMock.Object);
        services.AddScoped<ICorrelationIdService>(_ => correlationMock.Object);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var worker = new PostWorker(
            provider,
            sqsMock.Object,
            Mock.Of<ILogger<PostWorker>>(),
            BuildConfiguration());

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert — correlation ID is set to the SQS MessageId
        capturedCorrelationId.Should().Be("unique-post-message-id-xyz",
            because: "the SQS MessageId must be used as the correlation ID for log tracing");
    }
}

// -----------------------------------------------------------------------
// Test helpers
// -----------------------------------------------------------------------

/// <summary>
/// A spy <see cref="IServiceProvider"/> that counts how many async scopes are created.
/// Used to verify that <see cref="PostWorker"/> creates one scope per SQS message.
/// </summary>
file sealed class PostScopeCountingServiceProvider : IServiceProvider, IServiceScopeFactory
{
    private readonly IServiceProvider _inner;
    private readonly Action _onScopeCreated;

    public PostScopeCountingServiceProvider(IServiceProvider inner, Action onScopeCreated)
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
