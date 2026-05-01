using Amazon.SQS;
using Amazon.SQS.Model;
using IPS.AutoPost.Core.Commands;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using MediatR;
using System.Text.Json;

namespace IPS.AutoPost.Host.FeedWorker;

/// <summary>
/// ECS Fargate background service that continuously polls <c>ips-feed-queue</c> for
/// feed download jobs and dispatches them via MediatR.
/// </summary>
/// <remarks>
/// <para>
/// Polling behaviour:
/// <list type="bullet">
///   <item>Long polling: <c>WaitTimeSeconds = 20</c> — reduces empty-response API calls.</item>
///   <item>Batch size: <c>MaxNumberOfMessages = 10</c> — processes up to 10 messages per poll.</item>
///   <item>Each message is processed in its own DI scope to ensure scoped services
///         (repositories, orchestrator) are isolated per message.</item>
/// </list>
/// </para>
/// <para>
/// Message lifecycle:
/// <list type="bullet">
///   <item>On success: message is deleted from the queue.</item>
///   <item>On failure: message is NOT deleted — it becomes visible again after the
///         visibility timeout expires and will be retried (up to 3 times before
///         moving to the DLQ).</item>
/// </list>
/// </para>
/// </remarks>
public sealed class FeedWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAmazonSQS _sqsClient;
    private readonly ILogger<FeedWorker> _logger;
    private readonly string _queueUrl;

    // SQS polling constants
    private const int WaitTimeSeconds = 20;          // long polling
    private const int MaxNumberOfMessages = 10;      // batch size per poll

    public FeedWorker(
        IServiceProvider serviceProvider,
        IAmazonSQS sqsClient,
        ILogger<FeedWorker> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _sqsClient = sqsClient;
        _logger = logger;

        _queueUrl = configuration["SQS_QUEUE_URL"]
            ?? throw new InvalidOperationException(
                "SQS_QUEUE_URL environment variable is not set. " +
                "Ensure the ECS task definition includes SQS_QUEUE_URL.");
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FeedWorker started. Polling queue: {QueueUrl}", _queueUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await PollAsync(stoppingToken);

                if (messages.Count == 0)
                    continue;

                _logger.LogDebug("Received {MessageCount} message(s) from feed queue.", messages.Count);

                // Process each message in its own DI scope — scoped services are isolated per message
                var processingTasks = messages.Select(msg => ProcessMessageAsync(msg, stoppingToken));
                await Task.WhenAll(processingTasks);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — exit the loop cleanly
                break;
            }
            catch (Exception ex)
            {
                // Unexpected error in the polling loop itself (not in message processing).
                // Log and continue — do not crash the worker.
                _logger.LogError(ex, "Unexpected error in FeedWorker polling loop. Retrying in 5 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("FeedWorker stopped.");
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Issues a long-poll ReceiveMessage request and returns the list of messages.
    /// Returns an empty list when the queue is empty or the request times out cleanly.
    /// </summary>
    private async Task<List<Message>> PollAsync(CancellationToken ct)
    {
        var request = new ReceiveMessageRequest
        {
            QueueUrl              = _queueUrl,
            WaitTimeSeconds       = WaitTimeSeconds,
            MaxNumberOfMessages   = MaxNumberOfMessages,
            // Request all system attributes (MessageId, etc.) for correlation ID usage
            MessageSystemAttributeNames = ["All"],
            MessageAttributeNames       = ["All"]
        };

        var response = await _sqsClient.ReceiveMessageAsync(request, ct);
        return response.Messages;
    }

    /// <summary>
    /// Processes a single SQS message inside a fresh DI scope.
    /// Deletes the message on success; leaves it on the queue on failure.
    /// </summary>
    private async Task ProcessMessageAsync(Message message, CancellationToken ct)
    {
        // Create a new DI scope per message so scoped services (repositories, orchestrator)
        // are isolated and do not bleed state between concurrent message processing tasks.
        await using var scope = _serviceProvider.CreateAsyncScope();

        var correlationService = scope.ServiceProvider.GetRequiredService<ICorrelationIdService>();

        // Use the SQS MessageId as the correlation ID so all log entries for this
        // message can be correlated across the entire processing pipeline.
        using var correlationScope = correlationService.SetCorrelationId(message.MessageId);

        _logger.LogInformation(
            "Processing feed message. MessageId: {MessageId}, Body: {Body}",
            message.MessageId,
            message.Body);

        try
        {
            var payload = DeserializePayload(message.Body, message.MessageId);
            if (payload is null)
            {
                // Malformed message — delete it to prevent infinite DLQ cycling
                _logger.LogError(
                    "Failed to deserialize SQS message body. MessageId: {MessageId}. " +
                    "Deleting message to prevent DLQ cycling.",
                    message.MessageId);
                await DeleteMessageAsync(message.ReceiptHandle, ct);
                return;
            }

            var command = new ExecuteFeedCommand
            {
                JobId       = payload.JobId,
                ClientType  = payload.ClientType,
                TriggerType = payload.TriggerType,
                Mode        = payload.Mode
            };

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var result = await mediator.Send(command, ct);

            _logger.LogInformation(
                "Feed command completed. MessageId: {MessageId}, JobId: {JobId}, " +
                "ClientType: {ClientType}, IsApplicable: {IsApplicable}, Success: {Success}, " +
                "RecordsDownloaded: {RecordsDownloaded}",
                message.MessageId,
                payload.JobId,
                payload.ClientType,
                result.IsApplicable,
                result.Success,
                result.RecordsDownloaded);

            // Delete the message only after successful processing
            await DeleteMessageAsync(message.ReceiptHandle, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Worker is shutting down — do NOT delete the message so it can be
            // picked up by another task after the visibility timeout expires.
            _logger.LogWarning(
                "Feed message processing cancelled during shutdown. MessageId: {MessageId}. " +
                "Message will become visible again after visibility timeout.",
                message.MessageId);
        }
        catch (Exception ex)
        {
            // Processing failed — do NOT delete the message.
            // SQS will make it visible again after the visibility timeout (7200s).
            // After 3 receive attempts it will move to the DLQ.
            _logger.LogError(
                ex,
                "Failed to process feed message. MessageId: {MessageId}, JobId: {JobId}. " +
                "Message will NOT be deleted and will be retried.",
                message.MessageId,
                TryGetJobId(message.Body));
        }
    }

    /// <summary>
    /// Deletes a processed message from the SQS queue.
    /// </summary>
    private async Task DeleteMessageAsync(string receiptHandle, CancellationToken ct)
    {
        try
        {
            await _sqsClient.DeleteMessageAsync(_queueUrl, receiptHandle, ct);
            _logger.LogDebug("Deleted SQS message. ReceiptHandle: {ReceiptHandle}", receiptHandle);
        }
        catch (Exception ex)
        {
            // Log but do not rethrow — a delete failure is not fatal.
            // The message will become visible again after the visibility timeout.
            _logger.LogWarning(
                ex,
                "Failed to delete SQS message. ReceiptHandle: {ReceiptHandle}. " +
                "Message may be processed again after visibility timeout.",
                receiptHandle);
        }
    }

    /// <summary>
    /// Deserializes the SQS message body JSON into a <see cref="SqsMessagePayload"/>.
    /// Returns <c>null</c> on deserialization failure.
    /// </summary>
    private SqsMessagePayload? DeserializePayload(string body, string messageId)
    {
        try
        {
            return JsonSerializer.Deserialize<SqsMessagePayload>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "JSON deserialization failed for SQS message. MessageId: {MessageId}, Body: {Body}",
                messageId,
                body);
            return null;
        }
    }

    /// <summary>
    /// Attempts to extract the JobId from the raw message body for error logging.
    /// Returns 0 if the body cannot be parsed.
    /// </summary>
    private static int TryGetJobId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("JobId", out var prop))
                return prop.GetInt32();
        }
        catch { /* ignore — best-effort for logging only */ }
        return 0;
    }
}
