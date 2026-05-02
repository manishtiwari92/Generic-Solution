using System.Data;
using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Plugins.InvitedClub;
using IPS.AutoPost.Plugins.InvitedClub.Constants;
using IPS.AutoPost.Plugins.InvitedClub.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace IPS.AutoPost.Plugins.Tests.PropertyBased;

/// <summary>
/// PBT Property 27.7 — Pagination Completeness Property
///
/// PROPERTY: total records fetched == sum of all items across all pages.
///
/// The InvitedClub feed uses paginated GET requests with offset-based pagination.
/// The pagination loop must:
///   1. Fetch ALL pages until hasMore = false.
///   2. Accumulate ALL items from every page.
///   3. Never skip a page or double-count a page.
///
/// The implementation uses InvitedClubConstants.ApiPageSize (500) as the page size.
/// We test the pagination loop by simulating multi-page responses using hasMore=true/false.
///
/// Tested for: LoadSupplierAsync, LoadCOAAsync.
/// </summary>
public class PaginationCompletenessTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly string _tempDir;

    // The actual page size used by the implementation
    private const int ApiPageSize = InvitedClubConstants.ApiPageSize;

    public PaginationCompletenessTests()
    {
        _server = WireMockServer.Start();
        _tempDir = Path.Combine(Path.GetTempPath(), "PBT_Pagination_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -----------------------------------------------------------------------
    // FsCheck generators
    // -----------------------------------------------------------------------

    /// <summary>Generates page counts from 1 to 5 (to keep tests fast).</summary>
    private static Gen<int> PageCountGen =>
        Gen.Choose(1, 5);

    /// <summary>Generates items per page from 1 to ApiPageSize.</summary>
    private static Gen<int> ItemsPerPageGen =>
        Gen.Choose(1, ApiPageSize);

    // -----------------------------------------------------------------------
    // 27.7a — FsCheck property: LoadSupplierAsync fetches all pages
    // -----------------------------------------------------------------------

    [Fact]
    public void LoadSupplierAsync_FetchesAllPages_TotalCountMatchesAvailable()
    {
        var property = Prop.ForAll(
            PageCountGen.ToArbitrary(),
            ItemsPerPageGen.ToArbitrary(),
            (pageCount, itemsPerPage) =>
            {
                var totalCount = pageCount * itemsPerPage;
                var fetchedCount = RunPaginatedSupplierFetch(pageCount, itemsPerPage)
                    .GetAwaiter().GetResult();

                return fetchedCount == totalCount;
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.7b — FsCheck property: LoadCOAAsync fetches all pages
    // -----------------------------------------------------------------------

    [Fact]
    public void LoadCOAAsync_FetchesAllPages_TotalCountMatchesAvailable()
    {
        var property = Prop.ForAll(
            PageCountGen.ToArbitrary(),
            ItemsPerPageGen.ToArbitrary(),
            (pageCount, itemsPerPage) =>
            {
                var totalCount = pageCount * itemsPerPage;
                var fetchedCount = RunPaginatedCOAFetch(pageCount, itemsPerPage)
                    .GetAwaiter().GetResult();

                return fetchedCount == totalCount;
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.7c — Explicit parametric tests for specific page/count combinations
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1, 1)]    // 1 page, 1 item
    [InlineData(1, 500)]  // 1 page, full page
    [InlineData(2, 500)]  // 2 full pages
    [InlineData(3, 250)]  // 3 pages, 250 items each
    [InlineData(5, 100)]  // 5 pages, 100 items each
    public async Task LoadSupplierAsync_FetchesAllRecords_ForMultiplePages(
        int pageCount,
        int itemsPerPage)
    {
        var totalCount = pageCount * itemsPerPage;
        var fetchedCount = await RunPaginatedSupplierFetch(pageCount, itemsPerPage);

        fetchedCount.Should().Be(totalCount,
            $"LoadSupplierAsync must fetch all {totalCount} records across {pageCount} page(s) of {itemsPerPage} items each");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 500)]
    [InlineData(2, 500)]
    [InlineData(3, 250)]
    [InlineData(5, 100)]
    public async Task LoadCOAAsync_FetchesAllRecords_ForMultiplePages(
        int pageCount,
        int itemsPerPage)
    {
        var totalCount = pageCount * itemsPerPage;
        var fetchedCount = await RunPaginatedCOAFetch(pageCount, itemsPerPage);

        fetchedCount.Should().Be(totalCount,
            $"LoadCOAAsync must fetch all {totalCount} records across {pageCount} page(s) of {itemsPerPage} items each");
    }

    // -----------------------------------------------------------------------
    // 27.7d — Verify page request count matches expected page count
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1)]   // 1 page → 1 request
    [InlineData(2)]   // 2 pages → 2 requests
    [InlineData(3)]   // 3 pages → 3 requests
    [InlineData(5)]   // 5 pages → 5 requests
    public async Task LoadSupplierAsync_MakesCorrectNumberOfPageRequests(int pageCount)
    {
        SetupPaginatedSupplierEndpoint(pageCount, itemsPerPage: 10);

        var dbMock = new Mock<IInvitedClubFeedDataAccess>(MockBehavior.Loose);
        var strategy = BuildStrategy(dbMock.Object);
        var config = BuildConfig();

        await strategy.LoadSupplierAsync(config, CancellationToken.None);

        // Count how many GET requests were made to the supplier endpoint
        var requestCount = _server.LogEntries
            .Count(e => e.RequestMessage.Path.Contains("suppliers") &&
                        e.RequestMessage.Method == "GET");

        requestCount.Should().Be(pageCount,
            $"LoadSupplierAsync must make exactly {pageCount} page request(s)");

        _server.Reset();
    }

    // -----------------------------------------------------------------------
    // 27.7e — Single page with hasMore=false stops after one request
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoadSupplierAsync_WithSinglePage_MakesExactlyOneRequest()
    {
        SetupPaginatedSupplierEndpoint(pageCount: 1, itemsPerPage: 5);

        var dbMock = new Mock<IInvitedClubFeedDataAccess>(MockBehavior.Loose);
        var strategy = BuildStrategy(dbMock.Object);
        var config = BuildConfig();

        var suppliers = await strategy.LoadSupplierAsync(config, CancellationToken.None);

        suppliers.Should().HaveCount(5);

        var requestCount = _server.LogEntries
            .Count(e => e.RequestMessage.Path.Contains("suppliers") && e.RequestMessage.Method == "GET");

        requestCount.Should().Be(1, "a single page with hasMore=false must result in exactly one request");
    }

    [Fact]
    public async Task LoadSupplierAsync_WithMultiplePages_ContinuesUntilHasMoreFalse()
    {
        // 3 pages: first two have hasMore=true, last has hasMore=false
        SetupPaginatedSupplierEndpoint(pageCount: 3, itemsPerPage: 10);

        var dbMock = new Mock<IInvitedClubFeedDataAccess>(MockBehavior.Loose);
        var strategy = BuildStrategy(dbMock.Object);
        var config = BuildConfig();

        var suppliers = await strategy.LoadSupplierAsync(config, CancellationToken.None);

        suppliers.Should().HaveCount(30, "3 pages × 10 items = 30 total suppliers");

        var requestCount = _server.LogEntries
            .Count(e => e.RequestMessage.Path.Contains("suppliers") && e.RequestMessage.Method == "GET");

        requestCount.Should().Be(3, "must make exactly 3 requests for 3 pages");
    }

    // -----------------------------------------------------------------------
    // Core test runners
    // -----------------------------------------------------------------------

    private async Task<int> RunPaginatedSupplierFetch(int pageCount, int itemsPerPage)
    {
        SetupPaginatedSupplierEndpoint(pageCount, itemsPerPage);

        var dbMock = new Mock<IInvitedClubFeedDataAccess>(MockBehavior.Loose);
        var strategy = BuildStrategy(dbMock.Object);
        var config = BuildConfig();

        var suppliers = await strategy.LoadSupplierAsync(config, CancellationToken.None);

        _server.Reset();

        return suppliers.Count;
    }

    private async Task<int> RunPaginatedCOAFetch(int pageCount, int itemsPerPage)
    {
        SetupPaginatedCOAEndpoint(pageCount, itemsPerPage);

        var dbMock = new Mock<IInvitedClubFeedDataAccess>(MockBehavior.Loose);
        dbMock
            .Setup(db => db.TruncateTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.BulkCopyAsync(It.IsAny<string>(), It.IsAny<DataTable>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.GetMissingCOAIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var strategy = BuildStrategy(dbMock.Object);
        var config = BuildConfig();

        var coaCount = await strategy.LoadCOAAsync(config, CancellationToken.None);

        _server.Reset();

        return coaCount;
    }

    // -----------------------------------------------------------------------
    // WireMock setup helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sets up paginated supplier endpoint responses.
    /// Each page returns itemsPerPage items. The last page has hasMore=false.
    /// Pages are matched by offset query parameter.
    /// </summary>
    private void SetupPaginatedSupplierEndpoint(int pageCount, int itemsPerPage)
    {
        for (var page = 0; page < pageCount; page++)
        {
            var offset = page * ApiPageSize;
            var hasMore = page < pageCount - 1;
            var currentPage = page;

            var items = Enumerable.Range(currentPage * itemsPerPage, itemsPerPage)
                .Select(i => new
                {
                    SupplierId = $"S{i:D6}",
                    LastUpdateDate = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss")
                })
                .ToList();

            var responseBody = JsonSerializer.Serialize(new
            {
                items,
                count = itemsPerPage,
                hasMore,
                limit = ApiPageSize,
                offset
            });

            _server
                .Given(Request.Create()
                    .WithPath("/suppliers")
                    .WithParam("offset", offset.ToString())
                    .UsingGet())
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(responseBody));
        }
    }

    /// <summary>
    /// Sets up paginated COA endpoint responses.
    /// Each page returns itemsPerPage items. The last page has hasMore=false.
    /// </summary>
    private void SetupPaginatedCOAEndpoint(int pageCount, int itemsPerPage)
    {
        for (var page = 0; page < pageCount; page++)
        {
            var offset = page * ApiPageSize;
            var hasMore = page < pageCount - 1;
            var currentPage = page;

            var items = Enumerable.Range(currentPage * itemsPerPage, itemsPerPage).Select(i => new Dictionary<string, object>
            {
                ["AccountType"] = "E",
                ["EnabledFlag"] = "Y",
                ["_CODE_COMBINATION_ID"] = (i + 1).ToString(),
                ["_CHART_OF_ACCOUNTS_ID"] = "5237",
                ["entity"] = "100",
                ["department"] = "200",
                ["account"] = "300",
                ["subAccount"] = "0000",
                ["location"] = "000",
                ["future1"] = string.Empty,
                ["future2"] = string.Empty
            }).ToList();

            var responseBody = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                items,
                count = itemsPerPage,
                hasMore,
                limit = ApiPageSize,
                offset
            });

            _server
                .Given(Request.Create()
                    .WithPath("/accountCombinationsLOV")
                    .WithParam("offset", offset.ToString())
                    .UsingGet())
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(responseBody));
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private InvitedClubFeedStrategy BuildStrategy(IInvitedClubFeedDataAccess db)
    {
        return new InvitedClubFeedStrategy(
            db,
            new Mock<IEmailService>().Object,
            NullLogger<InvitedClubFeedStrategy>.Instance);
    }

    private GenericJobConfig BuildConfig() => new()
    {
        Id = 1,
        JobId = 42,
        AuthUsername = "user",
        AuthPassword = "pass",
        PostServiceUrl = _server.Urls[0] + "/",
        FeedDownloadPath = _tempDir
    };
}
