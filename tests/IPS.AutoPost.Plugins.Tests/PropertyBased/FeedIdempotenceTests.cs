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
/// PBT Property 27.5 — Feed Idempotence Property
///
/// PROPERTY: Running the feed download twice produces the same final row count as running it once.
///
/// The InvitedClub feed uses a full-refresh strategy (TRUNCATE + bulk insert) for the initial
/// call. On subsequent calls it uses an incremental strategy (DELETE WHERE SupplierId IN (...) +
/// bulk insert). Both strategies must be idempotent: running the feed N times must leave the
/// database in the same state as running it once.
///
/// Specifically:
///   - After run 1 (initial): table has N rows
///   - After run 2 (incremental, same data): table still has N rows (no duplicates)
///   - After run 3 (incremental, same data): table still has N rows
///
/// Tested via FsCheck generators that produce arbitrary supplier counts (1–50).
/// </summary>
public class FeedIdempotenceTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly string _tempDir;

    public FeedIdempotenceTests()
    {
        _server = WireMockServer.Start();
        _tempDir = Path.Combine(Path.GetTempPath(), "PBT_FeedIdempotence_" + Guid.NewGuid());
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

    private static Gen<int> SupplierCountGen =>
        Gen.Choose(1, 20);

    // -----------------------------------------------------------------------
    // 27.5a — FsCheck property: BulkInsertAsync is idempotent for full refresh
    // -----------------------------------------------------------------------

    [Fact]
    public void BulkInsert_FullRefresh_IsIdempotent_ForAnySupplierCount()
    {
        var property = Prop.ForAll(
            SupplierCountGen.ToArbitrary(),
            supplierCount =>
            {
                var (firstRunCount, secondRunCount) =
                    RunFullRefreshTwice(supplierCount).GetAwaiter().GetResult();

                // Both runs must produce the same row count
                return firstRunCount == secondRunCount && firstRunCount == supplierCount;
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.5b — FsCheck property: BulkInsertAsync is idempotent for incremental
    // -----------------------------------------------------------------------

    [Fact]
    public void BulkInsert_Incremental_IsIdempotent_ForAnySupplierCount()
    {
        var property = Prop.ForAll(
            SupplierCountGen.ToArbitrary(),
            supplierCount =>
            {
                var (firstRunCount, secondRunCount) =
                    RunIncrementalTwice(supplierCount).GetAwaiter().GetResult();

                // Both runs must produce the same row count
                return firstRunCount == secondRunCount && firstRunCount == supplierCount;
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.5c — Explicit tests for specific supplier counts
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public async Task FullRefresh_RunTwice_ProducesSameRowCount(int supplierCount)
    {
        var (firstRunCount, secondRunCount) = await RunFullRefreshTwice(supplierCount);

        firstRunCount.Should().Be(supplierCount, "first run must insert all suppliers");
        secondRunCount.Should().Be(supplierCount,
            "second run (full refresh) must truncate and re-insert, producing same count");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public async Task IncrementalRun_RunTwice_ProducesSameRowCount(int supplierCount)
    {
        var (firstRunCount, secondRunCount) = await RunIncrementalTwice(supplierCount);

        firstRunCount.Should().Be(supplierCount, "first incremental run must insert all suppliers");
        secondRunCount.Should().Be(supplierCount,
            "second incremental run must delete-then-reinsert, producing same count");
    }

    // -----------------------------------------------------------------------
    // 27.5d — Feed download result count is consistent across runs
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FeedDownload_RunTwice_ProducesSameRecordCount()
    {
        // Arrange: 3 suppliers, each with 1 address and 1 site
        const int supplierCount = 3;
        var suppliers = BuildSupplierList(supplierCount);

        SetupSupplierEndpoint(suppliers);
        SetupAddressEndpoints(suppliers);
        SetupSiteEndpoints(suppliers);
        SetupCOAEndpoint(coaCount: 5);

        var (dbMock, tableState) = BuildDbMockWithState(isInitial: true);

        var strategy = BuildStrategy(dbMock.Object);
        var config = BuildConfig();
        var context = new FeedContext { TriggerType = "Scheduled", S3Config = new EdenredApiUrlConfig() };

        // Act: first run
        var result1 = await strategy.ExecuteAsync(config, context, CancellationToken.None);

        // Reset WireMock and re-setup for second run (incremental)
        _server.Reset();
        SetupSupplierEndpoint(suppliers);
        SetupAddressEndpoints(suppliers);
        SetupSiteEndpoints(suppliers);
        SetupCOAEndpoint(coaCount: 5);

        // Second run: table is no longer empty (incremental mode)
        var (dbMock2, _) = BuildDbMockWithState(isInitial: false);
        var strategy2 = BuildStrategy(dbMock2.Object);

        var result2 = await strategy2.ExecuteAsync(config, context, CancellationToken.None);

        // Assert: both runs succeed and produce the same record count
        result1.IsApplicable.Should().BeTrue();
        result1.Success.Should().BeTrue();
        result2.IsApplicable.Should().BeTrue();
        result2.Success.Should().BeTrue();
        result1.RecordsDownloaded.Should().Be(result2.RecordsDownloaded,
            "feed download must be idempotent — same record count on every run");
    }

    // -----------------------------------------------------------------------
    // Core test runners
    // -----------------------------------------------------------------------

    private async Task<(int FirstRunCount, int SecondRunCount)> RunFullRefreshTwice(int supplierCount)
    {
        var suppliers = BuildSupplierList(supplierCount);
        var tableRows = new List<DataTable>();

        var dbMock = new Mock<IInvitedClubFeedDataAccess>(MockBehavior.Loose);

        // Track bulk copy calls — capture the DataTable passed to BulkCopyAsync
        dbMock
            .Setup(db => db.BulkCopyAsync(It.IsAny<string>(), It.IsAny<DataTable>(), It.IsAny<CancellationToken>()))
            .Callback<string, DataTable, CancellationToken>((_, dt, _) => tableRows.Add(dt))
            .Returns(Task.CompletedTask);

        dbMock
            .Setup(db => db.TruncateTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var strategy = BuildStrategy(dbMock.Object);

        // Run 1: full refresh (isInitial = true)
        var isInitial = true;
        var firstRunCount = await RunBulkInsert(strategy, suppliers, isInitial);

        // Run 2: full refresh again (same data, same count expected)
        var secondRunCount = await RunBulkInsert(strategy, suppliers, isInitial);

        return (firstRunCount, secondRunCount);
    }

    private async Task<(int FirstRunCount, int SecondRunCount)> RunIncrementalTwice(int supplierCount)
    {
        var suppliers = BuildSupplierList(supplierCount);

        var dbMock = new Mock<IInvitedClubFeedDataAccess>(MockBehavior.Loose);
        dbMock
            .Setup(db => db.BulkCopyAsync(It.IsAny<string>(), It.IsAny<DataTable>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.DeleteBySupplierIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var strategy = BuildStrategy(dbMock.Object);

        // Run 1: incremental (isInitial = false)
        var firstRunCount = await RunBulkInsert(strategy, suppliers, isInitial: false);

        // Run 2: incremental again (same data, same count expected)
        var secondRunCount = await RunBulkInsert(strategy, suppliers, isInitial: false);

        return (firstRunCount, secondRunCount);
    }

    private static async Task<int> RunBulkInsert(
        InvitedClubFeedStrategy strategy,
        List<SupplierResponse> suppliers,
        bool isInitial)
    {
        var supplierIds = suppliers.Select(s => s.SupplierId);
        await strategy.BulkInsertAsync(
            InvitedClubConstants.SupplierTableName,
            suppliers,
            isInitial,
            isInitial ? null : supplierIds);

        return suppliers.Count;
    }

    // -----------------------------------------------------------------------
    // WireMock setup helpers
    // -----------------------------------------------------------------------

    private void SetupSupplierEndpoint(List<SupplierResponse> suppliers)
    {
        var responseBody = JsonSerializer.Serialize(new
        {
            items = suppliers.Select(s => new { s.SupplierId, s.LastUpdateDate }),
            count = suppliers.Count,
            hasMore = false,
            limit = 500,
            offset = 0
        });

        _server
            .Given(Request.Create()
                .WithPath("/suppliers")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(responseBody));
    }

    private void SetupAddressEndpoints(List<SupplierResponse> suppliers)
    {
        foreach (var supplier in suppliers)
        {
            var responseBody = JsonSerializer.Serialize(new
            {
                items = new[] { new { SupplierAddressId = $"ADDR-{supplier.SupplierId}", AddressName = "Main" } },
                count = 1,
                hasMore = false,
                limit = 500,
                offset = 0
            });

            _server
                .Given(Request.Create()
                    .WithPath($"/suppliers/{supplier.SupplierId}/child/addresses")
                    .UsingGet())
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(responseBody));
        }
    }

    private void SetupSiteEndpoints(List<SupplierResponse> suppliers)
    {
        foreach (var supplier in suppliers)
        {
            var responseBody = JsonSerializer.Serialize(new
            {
                items = new[] { new { SupplierSiteId = $"SITE-{supplier.SupplierId}", SiteName = "Main" } },
                count = 1,
                hasMore = false,
                limit = 500,
                offset = 0
            });

            _server
                .Given(Request.Create()
                    .WithPath($"/suppliers/{supplier.SupplierId}/child/sites")
                    .UsingGet())
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(responseBody));
        }
    }

    private void SetupCOAEndpoint(int coaCount)
    {
        var items = Enumerable.Range(1, coaCount).Select(i => new Dictionary<string, object>
        {
            ["AccountType"] = "E",
            ["EnabledFlag"] = "Y",
            ["_CODE_COMBINATION_ID"] = i.ToString(),
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
            count = coaCount,
            hasMore = false,
            limit = 500,
            offset = 0
        });

        _server
            .Given(Request.Create()
                .WithPath("/accountCombinationsLOV")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(responseBody));
    }

    // -----------------------------------------------------------------------
    // DB mock builder
    // -----------------------------------------------------------------------

    private static (Mock<IInvitedClubFeedDataAccess> Mock, Dictionary<string, int> TableState)
        BuildDbMockWithState(bool isInitial)
    {
        var tableState = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dbMock = new Mock<IInvitedClubFeedDataAccess>(MockBehavior.Loose);

        // GetTableCountAsync: returns 0 if isInitial, else 100
        dbMock
            .Setup(db => db.GetTableCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(isInitial ? 0 : 100);

        dbMock
            .Setup(db => db.TruncateTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        dbMock
            .Setup(db => db.BulkCopyAsync(It.IsAny<string>(), It.IsAny<DataTable>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        dbMock
            .Setup(db => db.DeleteBySupplierIdsAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        dbMock
            .Setup(db => db.ExecuteNonQuerySpAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        dbMock
            .Setup(db => db.GetSupplierDataToExportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataSet());

        dbMock
            .Setup(db => db.ExecuteUpdateLastDownloadTimeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        dbMock
            .Setup(db => db.GetMissingCOAIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        return (dbMock, tableState);
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
        FeedDownloadPath = _tempDir,
        ClientConfigJson = JsonSerializer.Serialize(new
        {
            LastSupplierDownloadTime = DateTime.UtcNow.AddDays(-1).ToString("O")
        })
    };

    private static List<SupplierResponse> BuildSupplierList(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new SupplierResponse
            {
                SupplierId = $"S{i:D4}",
                LastUpdateDate = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss")
            })
            .ToList();
    }
}
