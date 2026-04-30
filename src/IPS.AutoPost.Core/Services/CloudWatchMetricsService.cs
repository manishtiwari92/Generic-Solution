using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using IPS.AutoPost.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace IPS.AutoPost.Core.Services;

/// <summary>
/// Publishes granular per-client CloudWatch custom metrics to the
/// <c>IPS/AutoPost/{env}</c> namespace, dimensioned by <c>ClientType</c> and <c>JobId</c>.
/// </summary>
/// <remarks>
/// The environment segment is read from the <c>ASPNETCORE_ENVIRONMENT</c> environment
/// variable (via <see cref="IConfiguration"/>). Defaults to <c>"production"</c> when
/// the variable is absent or empty.
/// </remarks>
public sealed class CloudWatchMetricsService : ICloudWatchMetricsService
{
    private readonly IAmazonCloudWatch _cloudWatch;
    private readonly string _namespace;

    public CloudWatchMetricsService(IAmazonCloudWatch cloudWatch, IConfiguration configuration)
    {
        _cloudWatch = cloudWatch;

        var env = configuration["ASPNETCORE_ENVIRONMENT"];
        if (string.IsNullOrWhiteSpace(env))
            env = "production";

        _namespace = $"IPS/AutoPost/{env}";
    }

    // -----------------------------------------------------------------------
    // Post pipeline metrics
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public Task PostStartedAsync(string clientType, int jobId, CancellationToken ct = default)
        => PutMetricAsync("PostStarted", clientType, jobId, 1.0, StandardUnit.None, ct);

    /// <inheritdoc />
    public Task PostCompletedAsync(string clientType, int jobId, CancellationToken ct = default)
        => PutMetricAsync("PostCompleted", clientType, jobId, 1.0, StandardUnit.None, ct);

    /// <inheritdoc />
    public Task PostFailedAsync(string clientType, int jobId, CancellationToken ct = default)
        => PutMetricAsync("PostFailed", clientType, jobId, 1.0, StandardUnit.None, ct);

    /// <inheritdoc />
    public Task PostSuccessCountAsync(string clientType, int jobId, int count, CancellationToken ct = default)
        => PutMetricAsync("PostSuccessCount", clientType, jobId, count, StandardUnit.Count, ct);

    /// <inheritdoc />
    public Task PostFailedCountAsync(string clientType, int jobId, int count, CancellationToken ct = default)
        => PutMetricAsync("PostFailedCount", clientType, jobId, count, StandardUnit.Count, ct);

    /// <inheritdoc />
    public Task PostDurationSecondsAsync(string clientType, int jobId, double durationSeconds, CancellationToken ct = default)
        => PutMetricAsync("PostDurationSeconds", clientType, jobId, durationSeconds, StandardUnit.Seconds, ct);

    // -----------------------------------------------------------------------
    // Feed pipeline metrics
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public Task FeedStartedAsync(string clientType, int jobId, CancellationToken ct = default)
        => PutMetricAsync("FeedStarted", clientType, jobId, 1.0, StandardUnit.None, ct);

    /// <inheritdoc />
    public Task FeedCompletedAsync(string clientType, int jobId, CancellationToken ct = default)
        => PutMetricAsync("FeedCompleted", clientType, jobId, 1.0, StandardUnit.None, ct);

    /// <inheritdoc />
    public Task FeedRecordsDownloadedAsync(string clientType, int jobId, int count, CancellationToken ct = default)
        => PutMetricAsync("FeedRecordsDownloaded", clientType, jobId, count, StandardUnit.Count, ct);

    /// <inheritdoc />
    public Task FeedDurationSecondsAsync(string clientType, int jobId, double durationSeconds, CancellationToken ct = default)
        => PutMetricAsync("FeedDurationSeconds", clientType, jobId, durationSeconds, StandardUnit.Seconds, ct);

    // -----------------------------------------------------------------------
    // Image retry metrics
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public Task ImageRetryAttemptedAsync(string clientType, int jobId, CancellationToken ct = default)
        => PutMetricAsync("ImageRetryAttempted", clientType, jobId, 1.0, StandardUnit.None, ct);

    /// <inheritdoc />
    public Task ImageRetrySucceededAsync(string clientType, int jobId, CancellationToken ct = default)
        => PutMetricAsync("ImageRetrySucceeded", clientType, jobId, 1.0, StandardUnit.None, ct);

    // -----------------------------------------------------------------------
    // Private helper
    // -----------------------------------------------------------------------

    private Task PutMetricAsync(
        string metricName,
        string clientType,
        int jobId,
        double value,
        StandardUnit unit,
        CancellationToken ct)
    {
        var request = new PutMetricDataRequest
        {
            Namespace = _namespace,
            MetricData =
            [
                new MetricDatum
                {
                    MetricName = metricName,
                    Value      = value,
                    Unit       = unit,
                    Timestamp  = DateTime.UtcNow,
                    Dimensions =
                    [
                        new Dimension { Name = "ClientType", Value = clientType },
                        new Dimension { Name = "JobId",      Value = jobId.ToString() }
                    ]
                }
            ]
        };

        return _cloudWatch.PutMetricDataAsync(request, ct);
    }
}
