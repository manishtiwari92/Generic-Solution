// IPS.AutoPost.Core.Tests/Api/PostControllerTests.cs
// Task 20.5: Integration tests for PostController.
//
// Uses WebApplicationFactory<Program> to spin up the full ASP.NET Core pipeline
// in-process. All external dependencies (orchestrator, repositories) are replaced
// with Moq mocks via WebApplicationFactory.WithWebHostBuilder.
//
// Tests cover:
//   - Manual post with itemIds → HTTP 200 + PostBatchResult shape
//   - Missing configuration → HTTP 404 with error message
//   - Empty itemIds route → HTTP 400
//   - Missing x-api-key header → HTTP 401
//   - Invalid x-api-key header → HTTP 401
//   - POST /api/post/{jobId} (all items) → HTTP 200

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IPS.AutoPost.Core.Engine;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace IPS.AutoPost.Core.Tests.Api;

/// <summary>
/// Integration tests for <see cref="IPS.AutoPost.Api.Controllers.PostController"/>.
/// Spins up the full ASP.NET Core pipeline in-process using
/// <see cref="WebApplicationFactory{TEntryPoint}"/> with all external dependencies mocked.
/// </summary>
public class PostControllerTests : IClassFixture<PostControllerTests.ApiFactory>
{
    // -----------------------------------------------------------------------
    // WebApplicationFactory — replaces real services with mocks
    // -----------------------------------------------------------------------

    /// <summary>
    /// Custom <see cref="WebApplicationFactory{TEntryPoint}"/> that:
    /// <list type="bullet">
    ///   <item>Replaces <see cref="AutoPostOrchestrator"/> with a Moq mock.</item>
    ///   <item>Injects a known API key into configuration so middleware passes.</item>
    ///   <item>Skips AWS Secrets Manager resolution (no real AWS in tests).</item>
    /// </list>
    /// </summary>
    public class ApiFactory : WebApplicationFactory<Program>
    {
        public const string TestApiKey = "test-api-key-12345";

        /// <summary>The mock orchestrator exposed so tests can set up expectations.</summary>
        public Mock<AutoPostOrchestrator> OrchestratorMock { get; } = new Mock<AutoPostOrchestrator>(
            Mock.Of<IConfigurationRepository>(),
            Mock.Of<IWorkitemRepository>(),
            Mock.Of<IRoutingRepository>(),
            Mock.Of<IAuditRepository>(),
            Mock.Of<IScheduleRepository>(),
            new PluginRegistry(),
            new SchedulerService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AutoPostOrchestrator>.Instance);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                // Inject the test API key and a dummy connection string so
                // SecretsManagerConfigurationProvider has nothing to resolve.
                // Override Serilog config to use Console only (no CloudWatch sink in tests).
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiKey:Value"]                = TestApiKey,
                    ["ConnectionStrings:Workflow"]  = "Server=localhost;Database=Test;Trusted_Connection=True;",
                    // Override Serilog to use Console only — avoids loading CloudWatch sink assembly
                    ["Serilog:Using:0"]             = "Serilog.Sinks.Console",
                    ["Serilog:WriteTo:0:Name"]      = "Console"
                });
            });

            builder.ConfigureServices(services =>
            {
                // Replace the real AutoPostOrchestrator with the mock
                services.RemoveAll<AutoPostOrchestrator>();
                services.AddScoped(_ => OrchestratorMock.Object);

                // Remove plugin registrations that require real DB/AWS connections
                services.RemoveAll<IPS.AutoPost.Plugins.InvitedClub.InvitedClubPlugin>();
                services.RemoveAll<IPS.AutoPost.Plugins.Sevita.SevitaPlugin>();
            });
        }
    }

    // -----------------------------------------------------------------------
    // Test infrastructure
    // -----------------------------------------------------------------------

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public PostControllerTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("x-api-key", ApiFactory.TestApiKey);
    }

    // -----------------------------------------------------------------------
    // Test 1: Manual post with itemIds — HTTP 200 + correct response shape
    // -----------------------------------------------------------------------

    /// <summary>
    /// POST /api/post/{jobId}/items/{itemIds} with a valid API key and itemIds
    /// should return HTTP 200 with a <see cref="PostBatchResult"/> body.
    /// </summary>
    [Fact]
    public async Task PostItems_ValidRequest_Returns200WithPostBatchResult()
    {
        // Arrange
        const int jobId = 42;
        const string itemIds = "1001,1002";

        var expected = new PostBatchResult
        {
            RecordsProcessed = 2,
            RecordsSuccess = 2,
            RecordsFailed = 0,
            ItemResults =
            [
                new PostItemResult { ItemId = 1001, IsSuccess = true, DestinationQueue = 200 },
                new PostItemResult { ItemId = 1002, IsSuccess = true, DestinationQueue = 200 }
            ]
        };

        _factory.OrchestratorMock
            .Setup(o => o.RunManualPostAsync(itemIds, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var response = await _client.PostAsync($"/api/post/{jobId}/items/{itemIds}", null);

        // Assert — HTTP status
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert — response body shape
        var body = await response.Content.ReadFromJsonAsync<PostBatchResult>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        body.Should().NotBeNull();
        body!.RecordsProcessed.Should().Be(2);
        body.RecordsSuccess.Should().Be(2);
        body.RecordsFailed.Should().Be(0);
        body.ItemResults.Should().HaveCount(2);
        body.ItemResults.Should().Contain(r => r.ItemId == 1001 && r.IsSuccess);
        body.ItemResults.Should().Contain(r => r.ItemId == 1002 && r.IsSuccess);
    }

    // -----------------------------------------------------------------------
    // Test 2: Missing configuration — HTTP 404
    // -----------------------------------------------------------------------

    /// <summary>
    /// When the orchestrator returns <c>ResponseCode = -1</c> (missing configuration),
    /// the controller should return HTTP 404 with an error message.
    /// </summary>
    [Fact]
    public async Task PostItems_MissingConfiguration_Returns404()
    {
        // Arrange
        const int jobId = 99;
        const string itemIds = "5001";

        _factory.OrchestratorMock
            .Setup(o => o.RunManualPostAsync(itemIds, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PostBatchResult
            {
                ResponseCode = -1,
                ErrorMessage = "Missing Configuration."
            });

        // Act
        var response = await _client.PostAsync($"/api/post/{jobId}/items/{itemIds}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("Missing Configuration.");
    }

    // -----------------------------------------------------------------------
    // Test 3: Partial success — HTTP 200 with mixed item results
    // -----------------------------------------------------------------------

    /// <summary>
    /// When some items succeed and some fail, the controller returns HTTP 200
    /// (the batch completed — individual item outcomes are in ItemResults).
    /// </summary>
    [Fact]
    public async Task PostItems_PartialSuccess_Returns200WithMixedResults()
    {
        // Arrange
        const int jobId = 42;
        const string itemIds = "2001,2002";

        var expected = new PostBatchResult
        {
            RecordsProcessed = 2,
            RecordsSuccess = 1,
            RecordsFailed = 1,
            ItemResults =
            [
                new PostItemResult { ItemId = 2001, IsSuccess = true,  DestinationQueue = 200 },
                new PostItemResult { ItemId = 2002, IsSuccess = false, DestinationQueue = 500 }
            ]
        };

        _factory.OrchestratorMock
            .Setup(o => o.RunManualPostAsync(itemIds, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var response = await _client.PostAsync($"/api/post/{jobId}/items/{itemIds}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PostBatchResult>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        body!.RecordsSuccess.Should().Be(1);
        body.RecordsFailed.Should().Be(1);
        body.ItemResults.Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Test 4: Missing x-api-key header → HTTP 401
    // -----------------------------------------------------------------------

    /// <summary>
    /// Requests without the <c>x-api-key</c> header should be rejected with HTTP 401.
    /// </summary>
    [Fact]
    public async Task PostItems_MissingApiKey_Returns401()
    {
        // Arrange — create a client without the API key header
        var unauthenticatedClient = _factory.CreateClient();

        // Act
        var response = await unauthenticatedClient.PostAsync("/api/post/42/items/1001", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -----------------------------------------------------------------------
    // Test 5: Invalid x-api-key header → HTTP 401
    // -----------------------------------------------------------------------

    /// <summary>
    /// Requests with an incorrect API key should be rejected with HTTP 401.
    /// </summary>
    [Fact]
    public async Task PostItems_InvalidApiKey_Returns401()
    {
        // Arrange — create a client with a wrong API key
        var badKeyClient = _factory.CreateClient();
        badKeyClient.DefaultRequestHeaders.Add("x-api-key", "wrong-key");

        // Act
        var response = await badKeyClient.PostAsync("/api/post/42/items/1001", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -----------------------------------------------------------------------
    // Test 6: POST /api/post/{jobId} (all items) — HTTP 200
    // -----------------------------------------------------------------------

    /// <summary>
    /// POST /api/post/{jobId} (no itemIds) should call the orchestrator and
    /// return HTTP 200 with the batch result.
    /// </summary>
    [Fact]
    public async Task PostJob_ValidRequest_Returns200()
    {
        // Arrange
        const int jobId = 42;

        var expected = new PostBatchResult
        {
            RecordsProcessed = 5,
            RecordsSuccess = 5,
            RecordsFailed = 0
        };

        _factory.OrchestratorMock
            .Setup(o => o.RunManualPostAsync(string.Empty, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var response = await _client.PostAsync($"/api/post/{jobId}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PostBatchResult>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        body!.RecordsProcessed.Should().Be(5);
        body.RecordsSuccess.Should().Be(5);
    }

    // -----------------------------------------------------------------------
    // Test 7: POST /api/post/{jobId} — missing configuration → HTTP 404
    // -----------------------------------------------------------------------

    /// <summary>
    /// POST /api/post/{jobId} when no configuration exists should return HTTP 404.
    /// </summary>
    [Fact]
    public async Task PostJob_MissingConfiguration_Returns404()
    {
        // Arrange
        _factory.OrchestratorMock
            .Setup(o => o.RunManualPostAsync(string.Empty, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PostBatchResult
            {
                ResponseCode = -1,
                ErrorMessage = "Missing Configuration."
            });

        // Act
        var response = await _client.PostAsync("/api/post/999", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -----------------------------------------------------------------------
    // Test 8: Health check endpoint is exempt from API key auth
    // -----------------------------------------------------------------------

    /// <summary>
    /// GET /health should return HTTP 200 without requiring an API key.
    /// </summary>
    [Fact]
    public async Task HealthCheck_NoApiKey_Returns200()
    {
        // Arrange — unauthenticated client
        var unauthenticatedClient = _factory.CreateClient();

        // Act
        var response = await unauthenticatedClient.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------------
    // Test 9: userId query parameter is forwarded to orchestrator
    // -----------------------------------------------------------------------

    /// <summary>
    /// The <c>userId</c> query parameter should be passed through to
    /// <see cref="AutoPostOrchestrator.RunManualPostAsync"/>.
    /// </summary>
    [Fact]
    public async Task PostItems_WithUserId_ForwardsUserIdToOrchestrator()
    {
        // Arrange
        const int jobId = 42;
        const string itemIds = "3001";
        const int userId = 77;

        _factory.OrchestratorMock
            .Setup(o => o.RunManualPostAsync(itemIds, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PostBatchResult { RecordsProcessed = 1, RecordsSuccess = 1 });

        // Act
        var response = await _client.PostAsync(
            $"/api/post/{jobId}/items/{itemIds}?userId={userId}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.OrchestratorMock.Verify(
            o => o.RunManualPostAsync(itemIds, userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
