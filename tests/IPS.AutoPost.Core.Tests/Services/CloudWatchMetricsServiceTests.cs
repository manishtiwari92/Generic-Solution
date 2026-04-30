using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using FluentAssertions;
using IPS.AutoPost.Core.Services;
using Microsoft.Extensions.Configuration;
using Moq;

namespace IPS.AutoPost.Core.Tests.Services;

/// <summary>
/// Unit tests for <see cref="CloudWatchMetricsService"/>.
/// Verifies that each metric method publishes the correct namespace, dimensions,
/// metric name, value, and unit to CloudWatch.
/// </summary>
public class CloudWatchMetricsServiceTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IConfiguration BuildConfig(string? env)
    {
        var dict = new Dictionary<string, string?>();
        if (env is not null)
            dict["ASPNETCORE_ENVIRONMENT"] = env;

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    private static (CloudWatchMetricsService Service, Mock<IAmazonCloudWatch> CloudWatchMock)
        CreateSut(string? env = "test")
    {
        var mock = new Mock<IAmazonCloudWatch>();
        mock.Setup(x => x.PutMetricDataAsync(
                It.IsAny<PutMetricDataRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutMetricDataResponse());

        var sut = new CloudWatchMetricsService(mock.Object, BuildConfig(env));
        return (sut, mock);
    }

    /// <summary>
    /// Captures the <see cref="PutMetricDataRequest"/> passed to
    /// <c>PutMetricDataAsync</c> and returns it for assertion.
    /// </summary>
    private static (CloudWatchMetricsService Service, Func<PutMetricDataRequest?> GetCaptured)
        CreateSutWithCapture(string? env = "test")
    {
        PutMetricDataRequest? captured = null;

        var mock = new Mock<IAmazonCloudWatch>();
        mock.Setup(x => x.PutMetricDataAsync(
                It.IsAny<PutMetricDataRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<PutMetricDataRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new PutMetricDataResponse());

        var sut = new CloudWatchMetricsService(mock.Object, BuildConfig(env));
        return (sut, () => captured);
    }

    // -----------------------------------------------------------------------
    // 1. Namespace tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PostStartedAsync_UsesCorrectNamespace()
    {
        // Arrange
        var (sut, getCapture) = CreateSutWithCapture("uat");

        // Act
        await sut.PostStartedAsync("TestClient", 42);

        // Assert
        getCapture()!.Namespace.Should().Be("IPS/AutoPost/uat",
            because: "the namespace must reflect the configured environment");
    }

    [Fact]
    public async Task PostStartedAsync_UsesProductionNamespaceWhenEnvNotSet()
    {
        // Arrange — no ASPNETCORE_ENVIRONMENT key in config
        var (sut, getCapture) = CreateSutWithCapture(env: null);

        // Act
        await sut.PostStartedAsync("TestClient", 1);

        // Assert
        getCapture()!.Namespace.Should().Be("IPS/AutoPost/production",
            because: "when ASPNETCORE_ENVIRONMENT is absent the namespace must default to 'production'");
    }

    // -----------------------------------------------------------------------
    // 2. Dimension tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PostStartedAsync_IncludesClientTypeDimension()
    {
        // Arrange
        var (sut, getCapture) = CreateSutWithCapture();

        // Act
        await sut.PostStartedAsync("InvitedClub", 99);

        // Assert
        var datum = getCapture()!.MetricData.Single();
        datum.Dimensions.Should().Contain(d => d.Name == "ClientType" && d.Value == "InvitedClub",
            because: "every metric must carry a ClientType dimension with the caller-supplied value");
    }

    [Fact]
    public async Task PostStartedAsync_IncludesJobIdDimension()
    {
        // Arrange
        var (sut, getCapture) = CreateSutWithCapture();

        // Act
        await sut.PostStartedAsync("InvitedClub", 123);

        // Assert
        var datum = getCapture()!.MetricData.Single();
        datum.Dimensions.Should().Contain(d => d.Name == "JobId" && d.Value == "123",
            because: "every metric must carry a JobId dimension whose value is the jobId as a string");
    }

    // -----------------------------------------------------------------------
    // 3. Metric name test
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PostStartedAsync_MetricNameIsPostStarted()
    {
        // Arrange
        var (sut, getCapture) = CreateSutWithCapture();

        // Act
        await sut.PostStartedAsync("TestClient", 1);

        // Assert
        getCapture()!.MetricData.Single().MetricName.Should().Be("PostStarted",
            because: "PostStartedAsync must publish a datum with MetricName 'PostStarted'");
    }

    // -----------------------------------------------------------------------
    // 4. Unit and value tests — count metrics
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PostSuccessCountAsync_UsesCountUnit()
    {
        // Arrange
        var (sut, getCapture) = CreateSutWithCapture();

        // Act
        await sut.PostSuccessCountAsync("TestClient", 1, count: 5);

        // Assert
        getCapture()!.MetricData.Single().Unit.Should().Be(StandardUnit.Count,
            because: "PostSuccessCountAsync must use StandardUnit.Count");
    }

    [Fact]
    public async Task PostSuccessCountAsync_UsesCorrectValue()
    {
        // Arrange
        var (sut, getCapture) = CreateSutWithCapture();

        // Act
        await sut.PostSuccessCountAsync("TestClient", 1, count: 17);

        // Assert
        getCapture()!.MetricData.Single().Value.Should().Be(17,
            because: "PostSuccessCountAsync must forward the count argument as the metric value");
    }

    // -----------------------------------------------------------------------
    // 5. Unit and value tests — duration metrics
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PostDurationSecondsAsync_UsesSecondsUnit()
    {
        // Arrange
        var (sut, getCapture) = CreateSutWithCapture();

        // Act
        await sut.PostDurationSecondsAsync("TestClient", 1, durationSeconds: 3.5);

        // Assert
        getCapture()!.MetricData.Single().Unit.Should().Be(StandardUnit.Seconds,
            because: "PostDurationSecondsAsync must use StandardUnit.Seconds");
    }

    [Fact]
    public async Task PostDurationSecondsAsync_UsesCorrectValue()
    {
        // Arrange
        var (sut, getCapture) = CreateSutWithCapture();

        // Act
        await sut.PostDurationSecondsAsync("TestClient", 1, durationSeconds: 12.75);

        // Assert
        getCapture()!.MetricData.Single().Value.Should().Be(12.75,
            because: "PostDurationSecondsAsync must forward the durationSeconds argument as the metric value");
    }

    // -----------------------------------------------------------------------
    // 6. Event metrics all use value 1.0
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AllEventMetrics_UseValueOfOne()
    {
        // Arrange — each event metric is a separate invocation; we collect all captured requests
        var requests = new List<PutMetricDataRequest>();

        var mock = new Mock<IAmazonCloudWatch>();
        mock.Setup(x => x.PutMetricDataAsync(
                It.IsAny<PutMetricDataRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<PutMetricDataRequest, CancellationToken>((req, _) => requests.Add(req))
            .ReturnsAsync(new PutMetricDataResponse());

        var sut = new CloudWatchMetricsService(mock.Object, BuildConfig("test"));

        // Act — invoke all seven event metrics
        await sut.PostStartedAsync("C", 1);
        await sut.PostCompletedAsync("C", 1);
        await sut.PostFailedAsync("C", 1);
        await sut.FeedStartedAsync("C", 1);
        await sut.FeedCompletedAsync("C", 1);
        await sut.ImageRetryAttemptedAsync("C", 1);
        await sut.ImageRetrySucceededAsync("C", 1);

        // Assert — every datum must have Value == 1.0
        requests.Should().HaveCount(7,
            because: "seven event metrics were invoked");

        requests.SelectMany(r => r.MetricData)
            .Should().AllSatisfy(datum =>
                datum.Value.Should().Be(1.0,
                    because: $"event metric '{datum.MetricName}' must always publish value 1.0"));
    }
}
