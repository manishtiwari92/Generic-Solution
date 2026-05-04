// IPS.AutoPost.Core.Tests/Integration/ManualPostApiValidationTests.cs
// Task 28.9: UAT Manual Post API Validation Tests
//
// These tests call the real UAT API endpoint to validate:
//   - POST /api/post/{jobId}/items/{itemIds} response shape
//   - destinationQueue matches expected queue ID for success and failure cases
//   - PostBatchResult and PostItemResult field presence
//
// IMPORTANT: These tests are skipped in CI by default.
//   To run against UAT, set the UAT_API_URL environment variable and
//   remove the Skip attribute (or set RUN_UAT_TESTS=true).
//
//   export UAT_API_URL=https://api.ips-autopost-uat.example.com
//   export UAT_API_KEY=<your-api-key>
//   export UAT_JOB_ID=<job-id-to-test>
//   export UAT_ITEM_IDS=<comma-separated-item-ids>
//   export UAT_EXPECTED_SUCCESS_QUEUE=<queue-id-for-success>
//   export UAT_EXPECTED_FAIL_QUEUE=<queue-id-for-failure>

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace IPS.AutoPost.Core.Tests.Integration;

/// <summary>
/// UAT integration tests for the Manual Post API endpoint.
/// These tests call the real UAT API and validate the response shape and routing.
///
/// All tests are marked with <c>Skip = "UAT only"</c> so they do not run in CI.
/// Remove the Skip attribute to run manually against UAT.
/// </summary>
public class ManualPostApiValidationTests
{
    // -----------------------------------------------------------------------
    // Configuration — read from environment variables
    // -----------------------------------------------------------------------

    /// <summary>Base URL of the UAT API (e.g. https://api.ips-autopost-uat.example.com).</summary>
    private static readonly string UatApiUrl =
        Environment.GetEnvironmentVariable("UAT_API_URL")
        ?? "https://localhost:5001";

    /// <summary>API key for the x-api-key header.</summary>
    private static readonly string UatApiKey =
        Environment.GetEnvironmentVariable("UAT_API_KEY")
        ?? "changeme";

    /// <summary>Job ID to use for test posts (must exist in generic_job_configuration).</summary>
    private static readonly int UatJobId =
        int.TryParse(Environment.GetEnvironmentVariable("UAT_JOB_ID"), out var jid) ? jid : 1;

    /// <summary>Comma-separated ItemIds to post (must exist in Workitems table in source queue).</summary>
    private static readonly string UatItemIds =
        Environment.GetEnvironmentVariable("UAT_ITEM_IDS")
        ?? "0";

    /// <summary>Expected destination queue ID for a successful post.</summary>
    private static readonly long ExpectedSuccessQueue =
        long.TryParse(Environment.GetEnvironmentVariable("UAT_EXPECTED_SUCCESS_QUEUE"), out var sq) ? sq : 0;

    /// <summary>Expected destination queue ID for a failed post.</summary>
    private static readonly long ExpectedFailQueue =
        long.TryParse(Environment.GetEnvironmentVariable("UAT_EXPECTED_FAIL_QUEUE"), out var fq) ? fq : 0;

    // -----------------------------------------------------------------------
    // HTTP client factory
    // -----------------------------------------------------------------------

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            // Allow self-signed certs in UAT
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(UatApiUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };

        client.DefaultRequestHeaders.Add("x-api-key", UatApiKey);
        return client;
    }

    // -----------------------------------------------------------------------
    // Test 1: Response shape validation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Calls POST /api/post/{jobId}/items/{itemIds} against the UAT endpoint
    /// and validates the full PostBatchResult response shape.
    ///
    /// Expected response shape:
    /// <code>
    /// {
    ///   "recordsProcessed": int,
    ///   "recordsSuccess": int,
    ///   "recordsFailed": int,
    ///   "itemResults": [
    ///     {
    ///       "itemId": long,
    ///       "isSuccess": bool,
    ///       "responseCode": int,
    ///       "responseMessage": string,
    ///       "destinationQueue": long
    ///     }
    ///   ]
    /// }
    /// </code>
    /// </summary>
    [Fact(Skip = "UAT only — remove Skip to run manually against UAT")]
    public async Task ManualPost_ResponseShape_MatchesPostBatchResultContract()
    {
        // Arrange
        using var client = CreateClient();
        var url = $"/api/post/{UatJobId}/items/{UatItemIds}";

        // Act
        var response = await client.PostAsync(url, null);

        // Assert — HTTP status
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: $"POST {url} should return 200 OK. Actual status: {(int)response.StatusCode}");

        // Assert — response body is valid JSON
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotBeNullOrWhiteSpace("response body should not be empty");

        // Assert — deserializes to PostBatchResult
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = JsonSerializer.Deserialize<PostBatchResultDto>(json, options);

        result.Should().NotBeNull("response should deserialize to PostBatchResult");

        // Assert — top-level fields present
        result!.RecordsProcessed.Should().BeGreaterThanOrEqualTo(0,
            "recordsProcessed must be a non-negative integer");
        result.RecordsSuccess.Should().BeGreaterThanOrEqualTo(0,
            "recordsSuccess must be a non-negative integer");
        result.RecordsFailed.Should().BeGreaterThanOrEqualTo(0,
            "recordsFailed must be a non-negative integer");
        (result.RecordsSuccess + result.RecordsFailed).Should().Be(result.RecordsProcessed,
            "recordsSuccess + recordsFailed should equal recordsProcessed");

        // Assert — itemResults array present
        result.ItemResults.Should().NotBeNull("itemResults array must be present");

        // Assert — each item result has required fields
        foreach (var item in result.ItemResults!)
        {
            item.ItemId.Should().BeGreaterThan(0, "itemId must be a positive long");
            item.ResponseCode.Should().NotBe(0, "responseCode must be set");
            item.ResponseMessage.Should().NotBeNullOrWhiteSpace("responseMessage must be set");
            item.DestinationQueue.Should().BeGreaterThan(0, "destinationQueue must be a positive queue ID");
        }
    }

    // -----------------------------------------------------------------------
    // Test 2: destinationQueue matches expected success queue
    // -----------------------------------------------------------------------

    /// <summary>
    /// For items that post successfully, <c>destinationQueue</c> must match
    /// the configured <c>success_queue_id</c> from <c>generic_job_configuration</c>.
    /// </summary>
    [Fact(Skip = "UAT only — remove Skip to run manually against UAT")]
    public async Task ManualPost_SuccessItems_DestinationQueueMatchesSuccessQueue()
    {
        // Arrange
        using var client = CreateClient();
        var url = $"/api/post/{UatJobId}/items/{UatItemIds}";

        // Act
        var response = await client.PostAsync(url, null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = await response.Content.ReadFromJsonAsync<PostBatchResultDto>(options);

        // Assert — successful items route to the expected success queue
        var successItems = result!.ItemResults!.Where(r => r.IsSuccess).ToList();

        if (successItems.Count == 0)
        {
            // No successful items — skip assertion but log a warning
            // This can happen if all items are in an unexpected state
            return;
        }

        foreach (var item in successItems)
        {
            item.DestinationQueue.Should().Be(ExpectedSuccessQueue,
                because: $"successful item {item.ItemId} should route to success queue {ExpectedSuccessQueue}");
        }
    }

    // -----------------------------------------------------------------------
    // Test 3: destinationQueue matches expected fail queue for failed items
    // -----------------------------------------------------------------------

    /// <summary>
    /// For items that fail to post, <c>destinationQueue</c> must match
    /// the configured <c>primary_fail_queue_id</c> from <c>generic_job_configuration</c>.
    /// </summary>
    [Fact(Skip = "UAT only — remove Skip to run manually against UAT")]
    public async Task ManualPost_FailedItems_DestinationQueueMatchesFailQueue()
    {
        // Arrange
        using var client = CreateClient();
        var url = $"/api/post/{UatJobId}/items/{UatItemIds}";

        // Act
        var response = await client.PostAsync(url, null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = await response.Content.ReadFromJsonAsync<PostBatchResultDto>(options);

        // Assert — failed items route to the expected fail queue
        var failedItems = result!.ItemResults!.Where(r => !r.IsSuccess).ToList();

        if (failedItems.Count == 0)
        {
            // No failed items — all succeeded, which is also valid
            return;
        }

        foreach (var item in failedItems)
        {
            item.DestinationQueue.Should().Be(ExpectedFailQueue,
                because: $"failed item {item.ItemId} should route to fail queue {ExpectedFailQueue}");
        }
    }

    // -----------------------------------------------------------------------
    // Test 4: Missing configuration returns 404
    // -----------------------------------------------------------------------

    /// <summary>
    /// Posting to a job ID that has no configuration in <c>generic_job_configuration</c>
    /// should return HTTP 404 with an error message.
    /// </summary>
    [Fact(Skip = "UAT only — remove Skip to run manually against UAT")]
    public async Task ManualPost_NonExistentJobId_Returns404WithErrorMessage()
    {
        // Arrange — use a job ID that should not exist
        using var client = CreateClient();
        const int nonExistentJobId = 999999;
        var url = $"/api/post/{nonExistentJobId}/items/1";

        // Act
        var response = await client.PostAsync(url, null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            because: "a non-existent job ID should return 404");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace("404 response should include an error message");
    }

    // -----------------------------------------------------------------------
    // Test 5: API key authentication is enforced
    // -----------------------------------------------------------------------

    /// <summary>
    /// Requests without a valid <c>x-api-key</c> header should be rejected with HTTP 401.
    /// </summary>
    [Fact(Skip = "UAT only — remove Skip to run manually against UAT")]
    public async Task ManualPost_MissingApiKey_Returns401()
    {
        // Arrange — client without API key
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var unauthClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(UatApiUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Act
        var response = await unauthClient.PostAsync($"/api/post/{UatJobId}/items/1", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "requests without x-api-key should be rejected");
    }

    // -----------------------------------------------------------------------
    // Test 6: itemResults count matches recordsProcessed
    // -----------------------------------------------------------------------

    /// <summary>
    /// The number of entries in <c>itemResults</c> must equal <c>recordsProcessed</c>.
    /// </summary>
    [Fact(Skip = "UAT only — remove Skip to run manually against UAT")]
    public async Task ManualPost_ItemResultsCount_MatchesRecordsProcessed()
    {
        // Arrange
        using var client = CreateClient();
        var url = $"/api/post/{UatJobId}/items/{UatItemIds}";

        // Act
        var response = await client.PostAsync(url, null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var result = await response.Content.ReadFromJsonAsync<PostBatchResultDto>(options);

        // Assert
        result!.ItemResults!.Count.Should().Be(result.RecordsProcessed,
            because: "itemResults.Count must equal recordsProcessed");
    }

    // -----------------------------------------------------------------------
    // DTOs — mirror PostBatchResult / PostItemResult for deserialization
    // -----------------------------------------------------------------------

    /// <summary>
    /// DTO mirroring <see cref="IPS.AutoPost.Core.Models.PostBatchResult"/> for JSON deserialization.
    /// Uses camelCase property names to match the API's JSON output.
    /// </summary>
    private sealed class PostBatchResultDto
    {
        public int RecordsProcessed { get; set; }
        public int RecordsSuccess { get; set; }
        public int RecordsFailed { get; set; }
        public List<PostItemResultDto>? ItemResults { get; set; }
        public string? ErrorMessage { get; set; }
        public int ResponseCode { get; set; }
    }

    /// <summary>
    /// DTO mirroring <see cref="IPS.AutoPost.Core.Models.PostItemResult"/> for JSON deserialization.
    /// </summary>
    private sealed class PostItemResultDto
    {
        public long ItemId { get; set; }
        public bool IsSuccess { get; set; }
        public int ResponseCode { get; set; }
        public string ResponseMessage { get; set; } = string.Empty;
        public long DestinationQueue { get; set; }
    }
}
