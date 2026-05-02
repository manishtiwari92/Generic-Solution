using System.Data;
using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Core.Services;
using IPS.AutoPost.Plugins.InvitedClub;
using IPS.AutoPost.Plugins.InvitedClub.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WireMock.Server;
using Xunit;

namespace IPS.AutoPost.Plugins.Tests.PropertyBased;

/// <summary>
/// PBT Property 27.8 — Error Condition Routing Property
///
/// PROPERTY 1: image-not-found → routed to EdenredFailPostQueueId with ZERO API calls.
/// PROPERTY 2: RequesterId-empty → routed to InvitedFailPostQueueId with ZERO API calls.
///
/// These are early-exit conditions that must short-circuit before any Oracle Fusion API
/// call is made. Making an API call in these conditions would create orphaned invoices
/// (invoices in Oracle Fusion with no corresponding workitem in the success queue).
///
/// Tested via FsCheck generators that produce arbitrary:
///   - Item IDs
///   - EdenredFailQueueId values (to verify the exact queue ID is used)
///   - InvitedFailQueueId values
/// </summary>
public class ErrorConditionRoutingTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly string _tempDir;

    public ErrorConditionRoutingTests()
    {
        _server = WireMockServer.Start();
        _tempDir = Path.Combine(Path.GetTempPath(), "PBT_ErrorRouting_" + Guid.NewGuid());
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

    private static Gen<long> ItemIdGen =>
        Gen.Choose(1, 999_999).Select(i => (long)i);

    private static Gen<int> QueueIdGen =>
        Gen.Choose(100, 9999);

    // -----------------------------------------------------------------------
    // 27.8a — FsCheck property: image-not-found → EdenredFailQueueId, zero API calls
    // -----------------------------------------------------------------------

    [Fact]
    public void ImageNotFound_RoutesToEdenredFailQueueId_WithZeroApiCalls_ForAnyItemId()
    {
        var property = Prop.ForAll(
            ItemIdGen.ToArbitrary(),
            QueueIdGen.ToArbitrary(),
            (itemId, edenredFailQueueId) =>
            {
                var (destinationQueue, apiCallCount) =
                    RunImageNotFoundScenario(itemId, edenredFailQueueId)
                        .GetAwaiter().GetResult();

                return destinationQueue == edenredFailQueueId && apiCallCount == 0;
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.8b — FsCheck property: RequesterId-empty → InvitedFailQueueId, zero API calls
    // -----------------------------------------------------------------------

    [Fact]
    public void RequesterIdEmpty_RoutesToInvitedFailQueueId_WithZeroApiCalls_ForAnyItemId()
    {
        var property = Prop.ForAll(
            ItemIdGen.ToArbitrary(),
            QueueIdGen.ToArbitrary(),
            (itemId, invitedFailQueueId) =>
            {
                var (destinationQueue, apiCallCount) =
                    RunRequesterIdEmptyScenario(itemId, invitedFailQueueId)
                        .GetAwaiter().GetResult();

                return destinationQueue == invitedFailQueueId && apiCallCount == 0;
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.8c — Explicit parametric tests
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1001L, 888)]
    [InlineData(2002L, 777)]
    [InlineData(999999L, 500)]
    public async Task ImageNotFound_RoutesToEdenredFailQueueId(long itemId, int edenredFailQueueId)
    {
        var (destinationQueue, apiCallCount) =
            await RunImageNotFoundScenario(itemId, edenredFailQueueId);

        destinationQueue.Should().Be(edenredFailQueueId,
            "image-not-found must route to EdenredFailQueueId");
        apiCallCount.Should().Be(0,
            "no Oracle Fusion API calls must be made when image is not found");
    }

    [Theory]
    [InlineData(3003L, 999)]
    [InlineData(4004L, 888)]
    [InlineData(5005L, 600)]
    public async Task RequesterIdEmpty_RoutesToInvitedFailQueueId(long itemId, int invitedFailQueueId)
    {
        var (destinationQueue, apiCallCount) =
            await RunRequesterIdEmptyScenario(itemId, invitedFailQueueId);

        destinationQueue.Should().Be(invitedFailQueueId,
            "empty RequesterId must route to InvitedFailQueueId");
        apiCallCount.Should().Be(0,
            "no Oracle Fusion API calls must be made when RequesterId is empty");
    }

    // -----------------------------------------------------------------------
    // 27.8d — Verify no history is written for early-exit conditions
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ImageNotFound_DoesNotWriteHistory()
    {
        var historySaveCount = 0;
        var dbMock = BuildDbMock(
            itemId: 10_001L,
            edenredFailQueueId: 888,
            invitedFailQueueId: 999,
            onHistorySave: () => historySaveCount++);

        dbMock
            .Setup(db => db.GetHeaderAndDetailDataAsync(10_001L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDataSet(10_001L, imagePath: "missing.pdf", requesterId: "REQ-1"));

        var strategy = BuildStrategy(dbMock.Object);
        var config = BuildConfig(edenredFailQueueId: 888, invitedFailQueueId: 999);
        var context = BuildContext("10001");

        await strategy.ExecuteAsync(config, context, CancellationToken.None);

        historySaveCount.Should().Be(0,
            "history must NOT be written when image is not found (no API call was attempted)");
    }

    [Fact]
    public async Task RequesterIdEmpty_DoesNotWriteHistory()
    {
        var imgPath = "invoice_req_empty.pdf";
        await WriteImageAsync(imgPath);

        var historySaveCount = 0;
        var dbMock = BuildDbMock(
            itemId: 20_001L,
            edenredFailQueueId: 888,
            invitedFailQueueId: 999,
            onHistorySave: () => historySaveCount++);

        dbMock
            .Setup(db => db.GetHeaderAndDetailDataAsync(20_001L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDataSet(20_001L, imagePath: imgPath, requesterId: ""));

        var strategy = BuildStrategy(dbMock.Object);
        var config = BuildConfig(edenredFailQueueId: 888, invitedFailQueueId: 999);
        var context = BuildContext("20001");

        await strategy.ExecuteAsync(config, context, CancellationToken.None);

        historySaveCount.Should().Be(0,
            "history must NOT be written when RequesterId is empty (no API call was attempted)");
    }

    // -----------------------------------------------------------------------
    // 27.8e — Verify PostInProcess is still cleared for early-exit conditions
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ImageNotFound_StillClearsPostInProcess()
    {
        var clearWasCalled = false;
        var dbMock = BuildDbMock(
            itemId: 30_001L,
            edenredFailQueueId: 888,
            invitedFailQueueId: 999,
            onClearPostInProcess: () => clearWasCalled = true);

        dbMock
            .Setup(db => db.GetHeaderAndDetailDataAsync(30_001L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDataSet(30_001L, imagePath: "missing.pdf", requesterId: "REQ-1"));

        var strategy = BuildStrategy(dbMock.Object);
        var config = BuildConfig(edenredFailQueueId: 888, invitedFailQueueId: 999);
        var context = BuildContext("30001");

        await strategy.ExecuteAsync(config, context, CancellationToken.None);

        clearWasCalled.Should().BeTrue(
            "PostInProcess must be cleared even when image is not found");
    }

    // -----------------------------------------------------------------------
    // Core test runners
    // -----------------------------------------------------------------------

    private async Task<(int DestinationQueue, int ApiCallCount)> RunImageNotFoundScenario(
        long itemId,
        int edenredFailQueueId)
    {
        int? capturedQueue = null;

        var dbMock = BuildDbMock(
            itemId,
            edenredFailQueueId,
            invitedFailQueueId: 999,
            onRoute: queueId => capturedQueue = queueId);

        dbMock
            .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDataSet(itemId, imagePath: "missing_image.pdf", requesterId: "REQ-1"));

        var strategy = BuildStrategy(dbMock.Object);
        var config = BuildConfig(edenredFailQueueId, invitedFailQueueId: 999);
        var context = BuildContext(itemId.ToString());

        await strategy.ExecuteAsync(config, context, CancellationToken.None);

        var apiCallCount = _server.LogEntries.Count();
        _server.Reset();

        return (capturedQueue ?? -1, apiCallCount);
    }

    private async Task<(int DestinationQueue, int ApiCallCount)> RunRequesterIdEmptyScenario(
        long itemId,
        int invitedFailQueueId)
    {
        int? capturedQueue = null;
        var imgPath = $"req_empty_{itemId}.pdf";
        await WriteImageAsync(imgPath);

        var dbMock = BuildDbMock(
            itemId,
            edenredFailQueueId: 888,
            invitedFailQueueId,
            onRoute: queueId => capturedQueue = queueId);

        dbMock
            .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDataSet(itemId, imagePath: imgPath, requesterId: ""));

        var strategy = BuildStrategy(dbMock.Object);
        var config = BuildConfig(edenredFailQueueId: 888, invitedFailQueueId);
        var context = BuildContext(itemId.ToString());

        await strategy.ExecuteAsync(config, context, CancellationToken.None);

        var apiCallCount = _server.LogEntries.Count();
        _server.Reset();

        return (capturedQueue ?? -1, apiCallCount);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Mock<IInvitedClubPostDataAccess> BuildDbMock(
        long itemId,
        int edenredFailQueueId,
        int invitedFailQueueId,
        Action<int>? onRoute = null,
        Action? onHistorySave = null,
        Action? onClearPostInProcess = null)
    {
        var dbMock = new Mock<IInvitedClubPostDataAccess>(MockBehavior.Loose);

        dbMock
            .Setup(db => db.SetPostInProcessAsync(itemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        dbMock
            .Setup(db => db.ClearPostInProcessAsync(itemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => onClearPostInProcess?.Invoke())
            .Returns(Task.CompletedTask);

        dbMock
            .Setup(db => db.RouteWorkitemAsync(
                itemId, It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<long, int, int, string, string, CancellationToken>(
                (_, queueId, _, _, _, _) => onRoute?.Invoke(queueId))
            .Returns(Task.CompletedTask);

        dbMock
            .Setup(db => db.InsertGeneralLogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), itemId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        dbMock
            .Setup(db => db.GetApiResponseTypesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<APIResponseType>());

        dbMock
            .Setup(db => db.SavePostHistoryAsync(It.IsAny<PostHistory>(), It.IsAny<CancellationToken>()))
            .Callback(() => onHistorySave?.Invoke())
            .Returns(Task.CompletedTask);

        return dbMock;
    }

    private InvitedClubPostStrategy BuildStrategy(IInvitedClubPostDataAccess db)
    {
        return new InvitedClubPostStrategy(
            db,
            new S3ImageService(NullLogger<S3ImageService>.Instance),
            new Mock<IEmailService>().Object,
            NullLogger<InvitedClubPostStrategy>.Instance);
    }

    private GenericJobConfig BuildConfig(int edenredFailQueueId, int invitedFailQueueId) => new()
    {
        Id = 1,
        JobId = 42,
        HeaderTable = "WFInvitedClubsIndexHeader",
        AuthUsername = "user",
        AuthPassword = "pass",
        PostServiceUrl = _server.Urls[0] + "/invoices/",
        SuccessQueueId = 200,
        PrimaryFailQueueId = 777,
        DefaultUserId = 100,
        IsLegacyJob = true,
        ImageParentPath = _tempDir + Path.DirectorySeparatorChar,
        ClientConfigJson = JsonSerializer.Serialize(new
        {
            EdenredFailQueueId = edenredFailQueueId,
            InvitedFailQueueId = invitedFailQueueId,
            ImagePostRetryLimit = 3
        })
    };

    private static PostContext BuildContext(string itemIds) => new()
    {
        TriggerType = "Manual",
        ItemIds = itemIds,
        UserId = 100,
        S3Config = new EdenredApiUrlConfig()
    };

    private async Task WriteImageAsync(string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        if (!File.Exists(path))
            await File.WriteAllBytesAsync(path, new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }

    private static DataSet BuildDataSet(long itemId, string imagePath, string requesterId)
    {
        var ds = new DataSet();

        var header = new DataTable("Header");
        header.Columns.Add("UID", typeof(long));
        header.Columns.Add("InvoiceNumber", typeof(string));
        header.Columns.Add("InvoiceCurrency", typeof(string));
        header.Columns.Add("PaymentCurrency", typeof(string));
        header.Columns.Add("InvoiceAmount", typeof(string));
        header.Columns.Add("InvoiceDate", typeof(string));
        header.Columns.Add("BusinessUnit", typeof(string));
        header.Columns.Add("Supplier", typeof(string));
        header.Columns.Add("SupplierSite", typeof(string));
        header.Columns.Add("RequesterId", typeof(string));
        header.Columns.Add("AccountingDate", typeof(string));
        header.Columns.Add("Description", typeof(string));
        header.Columns.Add("InvoiceType", typeof(string));
        header.Columns.Add("LegalEntity", typeof(string));
        header.Columns.Add("LegalEntityIdentifier", typeof(string));
        header.Columns.Add("LiabilityDistribution", typeof(string));
        header.Columns.Add("RoutingAttribute2", typeof(string));
        header.Columns.Add("InvoiceSource", typeof(string));
        header.Columns.Add("Payor", typeof(string));
        header.Columns.Add("ImagePath", typeof(string));
        header.Columns.Add("UseTax", typeof(string));
        header.Rows.Add(
            itemId, $"INV-{itemId}", "USD", "USD", "100.00",
            "2026-01-01", "BU1", "TEST-SUPPLIER", "SITE1",
            requesterId, "2026-01-01", "Test", "Standard",
            "LE1", "LE-ID-1", "LIAB-DIST", "ATTR2", "SOURCE",
            "PAYOR1", imagePath, "NO");

        var detail = new DataTable("Detail");
        detail.Columns.Add("LineNumber", typeof(string));
        detail.Columns.Add("LineAmount", typeof(string));
        detail.Columns.Add("ShipToLocation", typeof(string));
        detail.Columns.Add("DistributionCombination", typeof(string));
        detail.Columns.Add("DistributionLineNumber", typeof(string));
        detail.Columns.Add("DistributionLineType", typeof(string));
        detail.Columns.Add("DistributionAmount", typeof(string));
        detail.Rows.Add("1", "100.00", "LOC-A", "DIST-COMBO", "1", "Item", "100.00");

        ds.Tables.Add(header);
        ds.Tables.Add(detail);
        return ds;
    }
}
