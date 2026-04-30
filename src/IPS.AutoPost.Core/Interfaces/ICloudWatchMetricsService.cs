namespace IPS.AutoPost.Core.Interfaces;

/// <summary>
/// Publishes granular per-client CloudWatch custom metrics to the
/// <c>IPS/AutoPost/{env}</c> namespace, dimensioned by <c>ClientType</c> and <c>JobId</c>.
/// Implemented by <c>CloudWatchMetricsService</c> in the Services folder.
/// </summary>
public interface ICloudWatchMetricsService
{
    // -----------------------------------------------------------------------
    // Post pipeline metrics
    // -----------------------------------------------------------------------

    /// <summary>
    /// Records that a post batch has started.
    /// Published at the beginning of <c>AutoPostOrchestrator.ExecutePostBatchAsync</c>.
    /// </summary>
    Task PostStartedAsync(string clientType, int jobId, CancellationToken ct = default);

    /// <summary>
    /// Records that a post batch has completed (success or partial success).
    /// Published at the end of <c>AutoPostOrchestrator.ExecutePostBatchAsync</c>.
    /// </summary>
    Task PostCompletedAsync(string clientType, int jobId, CancellationToken ct = default);

    /// <summary>
    /// Records that a post batch failed entirely (unhandled exception).
    /// Published when <c>AutoPostOrchestrator.ExecutePostBatchAsync</c> catches an exception.
    /// </summary>
    Task PostFailedAsync(string clientType, int jobId, CancellationToken ct = default);

    /// <summary>
    /// Records the number of workitems successfully posted in a batch.
    /// </summary>
    /// <param name="count">Number of workitems routed to the success queue.</param>
    Task PostSuccessCountAsync(string clientType, int jobId, int count, CancellationToken ct = default);

    /// <summary>
    /// Records the number of workitems that failed to post in a batch.
    /// </summary>
    /// <param name="count">Number of workitems routed to a failure queue.</param>
    Task PostFailedCountAsync(string clientType, int jobId, int count, CancellationToken ct = default);

    /// <summary>
    /// Records the total duration of a post batch execution in seconds.
    /// </summary>
    /// <param name="durationSeconds">Elapsed time from batch start to completion.</param>
    Task PostDurationSecondsAsync(string clientType, int jobId, double durationSeconds, CancellationToken ct = default);

    // -----------------------------------------------------------------------
    // Feed pipeline metrics
    // -----------------------------------------------------------------------

    /// <summary>
    /// Records that a feed download has started.
    /// Published at the beginning of <c>AutoPostOrchestrator.RunScheduledFeedAsync</c>.
    /// </summary>
    Task FeedStartedAsync(string clientType, int jobId, CancellationToken ct = default);

    /// <summary>
    /// Records that a feed download has completed successfully.
    /// Published when <c>FeedResult.Success == true</c>.
    /// </summary>
    Task FeedCompletedAsync(string clientType, int jobId, CancellationToken ct = default);

    /// <summary>
    /// Records the number of records downloaded and persisted in a feed run.
    /// </summary>
    /// <param name="count">Value from <c>FeedResult.RecordsDownloaded</c>.</param>
    Task FeedRecordsDownloadedAsync(string clientType, int jobId, int count, CancellationToken ct = default);

    /// <summary>
    /// Records the total duration of a feed download execution in seconds.
    /// </summary>
    /// <param name="durationSeconds">Elapsed time from feed start to completion.</param>
    Task FeedDurationSecondsAsync(string clientType, int jobId, double durationSeconds, CancellationToken ct = default);

    // -----------------------------------------------------------------------
    // Image retry metrics (InvitedClub)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Records that an image attachment retry was attempted for a failed invoice.
    /// Published by <c>InvitedClubRetryService.RetryOneImageAsync</c>.
    /// </summary>
    Task ImageRetryAttemptedAsync(string clientType, int jobId, CancellationToken ct = default);

    /// <summary>
    /// Records that an image attachment retry succeeded (HTTP 201 received).
    /// Published by <c>InvitedClubRetryService.RetryOneImageAsync</c> on success.
    /// </summary>
    Task ImageRetrySucceededAsync(string clientType, int jobId, CancellationToken ct = default);
}
