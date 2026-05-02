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
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace IPS.AutoPost.Plugins.Tests.PropertyBased;

/// <summary>
/// PBT Property 27.3 — History Completeness Invariant
///
/// PROPERTY: count(history rows saved) == count(workitems where at least one API call was attempted).
///
/// History is saved ONLY when an API call was attempted. Early exits (image not found,
/// RequesterId empty) must NOT write history. All other paths (invoice fail, attachment fail,
/// calculateTax fail, full success) MUST write exactly one history row per workitem.
///
/// This ensures the audit trail is complete and accurate — no phantom history rows,
/// no missing history rows for processed invoices.
/// </summary>
public class HistoryCompletenessTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly string _tempDir;

    private const int SuccessQueueId = 200;
    private const int EdenredFailQueueId = 888;
    private const int InvitedFailQueueId = 999;
    private const int PrimaryFailQueueId = 777;

    public HistoryCompletenessTests()
    {
        _server = WireMockServer.Start();
        _tempDir = Path.Combine(Path.GetTempPath(), "PBT_History_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    public enum HistoryScenario
    {
        /// <summary>No API call attempted — history must NOT be written.</summary>
        ImageNotFound,
        /// <summary>No API call attempted — history must NOT be written.</summary>
        RequesterIdEmpty,
        /// <summary>Invoice POST attempted — history MUST be written.</summary>
        InvoiceApiFail,
        /// <summary>Invoice + Attachment POST attempted — history MUST be written.</summary>
        AttachmentApiFail,
        /// <summary>All steps attempted — history MUST be written.</summary>
        FullSuccess
    }

    private static Gen<long> ItemIdGen =>
        Gen.Choose(1, 999_999).Select(i => (long)i);

    private static Gen<HistoryScenario> ScenarioGen =>
        Gen.Elements(
            HistoryScenario.ImageNotFound,
            HistoryScenario.RequesterIdEmpty,
            HistoryScenario.InvoiceApiFail,
            HistoryScenario.AttachmentApiFail,
            HistoryScenario.FullSuccess);

    // -----------------------------------------------------------------------
    // 27.3a — FsCheck property: history count matches API-attempted count
    // -----------------------------------------------------------------------

    [Fact]
    public void HistoryCount_EqualsApiAttemptedCount_ForAllScenarios()
    {
        var property = Prop.ForAll(
            ItemIdGen.ToArbitrary(),
            ScenarioGen.ToArbitrary(),
            (itemId, scenario) =>
            {
                var (historySaveCount, expectedHistoryCount) =
                    RunAndCaptureHistory(itemId, scenario).GetAwaiter().GetResult();

                return historySaveCount == expectedHistoryCount;
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.3b — Explicit parametric tests
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(HistoryScenario.ImageNotFound, 0)]
    [InlineData(HistoryScenario.RequesterIdEmpty, 0)]
    [InlineData(HistoryScenario.InvoiceApiFail, 1)]
    [InlineData(HistoryScenario.AttachmentApiFail, 1)]
    [InlineData(HistoryScenario.FullSuccess, 1)]
    public async Task HistoryCount_MatchesExpected_ForScenario(
        HistoryScenario scenario,
        int expectedCount)
    {
        var (historySaveCount, _) = await RunAndCaptureHistory(70_000L, scenario);

        historySaveCount.Should().Be(expectedCount,
            $"history must be saved {expectedCount} time(s) for scenario {scenario}");
    }

    // -----------------------------------------------------------------------
    // 27.3c — Batch of N workitems: history count == API-attempted count
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HistoryCount_ForBatch_EqualsApiAttemptedCount()
    {
        // Arrange: 5 workitems with mixed scenarios
        // Items 1,2 → image not found (no API call, no history)
        // Items 3,4,5 → full success (API called, history written)
        var scenarios = new[]
        {
            (ItemId: 80_001L, HasImage: false),
            (ItemId: 80_002L, HasImage: false),
            (ItemId: 80_003L, HasImage: true),
            (ItemId: 80_004L, HasImage: true),
            (ItemId: 80_005L, HasImage: true)
        };

        var historySaveCount = 0;
        var dbMock = new Mock<IInvitedClubPostDataAccess>(MockBehavior.Loose);
        var emailMock = new Mock<IEmailService>();

        dbMock
            .Setup(db => db.SavePostHistoryAsync(It.IsAny<PostHistory>(), It.IsAny<CancellationToken>()))
            .Callback(() => historySaveCount++)
            .Returns(Task.CompletedTask);

        dbMock
            .Setup(db => db.SetPostInProcessAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.ClearPostInProcessAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.RouteWorkitemAsync(
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.InsertGeneralLogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.GetApiResponseTypesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<APIResponseType>());
        dbMock
            .Setup(db => db.UpdateInvoiceIdAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.UpdateAttachedDocumentIdAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        foreach (var (itemId, hasImage) in scenarios)
        {
            if (hasImage)
            {
                var imgPath = $"hist_{itemId}.pdf";
                await WriteImageAsync(imgPath);
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: imgPath, useTax: "NO", requesterId: "REQ-1"));

                _server
                    .Given(Request.Create().WithPath("/invoices/").UsingPost())
                    .RespondWith(Response.Create()
                        .WithStatusCode(201)
                        .WithHeader("Content-Type", "application/json")
                        .WithBody(JsonSerializer.Serialize(new { InvoiceId = $"INV-{itemId}" })));
                _server
                    .Given(Request.Create().WithPath($"/invoices/INV-{itemId}/child/attachments").UsingPost())
                    .RespondWith(Response.Create()
                        .WithStatusCode(201)
                        .WithHeader("Content-Type", "application/json")
                        .WithBody(JsonSerializer.Serialize(new { AttachedDocumentId = $"DOC-{itemId}" })));
            }
            else
            {
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: "missing.pdf", useTax: "NO", requesterId: "REQ-1"));
            }
        }

        var strategy = new InvitedClubPostStrategy(
            dbMock.Object,
            new S3ImageService(NullLogger<S3ImageService>.Instance),
            emailMock.Object,
            NullLogger<InvitedClubPostStrategy>.Instance);

        var config = BuildConfig();
        var itemIds = string.Join(",", scenarios.Select(s => s.ItemId));
        var context = new PostContext
        {
            TriggerType = "Manual",
            ItemIds = itemIds,
            UserId = 100,
            S3Config = new EdenredApiUrlConfig()
        };

        // Act
        var result = await strategy.ExecuteAsync(config, context, CancellationToken.None);

        // Assert: 3 items had API calls → 3 history rows; 2 items had no API call → 0 history rows
        historySaveCount.Should().Be(3,
            "history must be saved exactly once per workitem where an API call was attempted");
        result.RecordsProcessed.Should().Be(5);
        result.RecordsSuccess.Should().Be(3);
        result.RecordsFailed.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Core test runner
    // -----------------------------------------------------------------------

    private async Task<(int HistorySaveCount, int ExpectedHistoryCount)> RunAndCaptureHistory(
        long itemId,
        HistoryScenario scenario)
    {
        var historySaveCount = 0;
        var expectedHistoryCount = scenario switch
        {
            HistoryScenario.ImageNotFound => 0,
            HistoryScenario.RequesterIdEmpty => 0,
            _ => 1
        };

        var dbMock = new Mock<IInvitedClubPostDataAccess>(MockBehavior.Loose);
        var emailMock = new Mock<IEmailService>();

        dbMock
            .Setup(db => db.SavePostHistoryAsync(It.IsAny<PostHistory>(), It.IsAny<CancellationToken>()))
            .Callback(() => historySaveCount++)
            .Returns(Task.CompletedTask);

        dbMock
            .Setup(db => db.SetPostInProcessAsync(itemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.ClearPostInProcessAsync(itemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.RouteWorkitemAsync(
                itemId, It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            .Setup(db => db.UpdateGlDateValueAsync(itemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.UpdateInvoiceIdAsync(itemId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.UpdateAttachedDocumentIdAsync(itemId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        switch (scenario)
        {
            case HistoryScenario.ImageNotFound:
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: "missing.pdf", useTax: "NO", requesterId: "REQ-1"));
                break;

            case HistoryScenario.RequesterIdEmpty:
                var imgPath1 = $"hist_{itemId}_req.pdf";
                await WriteImageAsync(imgPath1);
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: imgPath1, useTax: "NO", requesterId: ""));
                break;

            case HistoryScenario.InvoiceApiFail:
                var imgPath2 = $"hist_{itemId}_inv.pdf";
                await WriteImageAsync(imgPath2);
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: imgPath2, useTax: "NO", requesterId: "REQ-1"));
                _server
                    .Given(Request.Create().WithPath("/invoices/").UsingPost())
                    .RespondWith(Response.Create().WithStatusCode(500).WithBody("Error"));
                break;

            case HistoryScenario.AttachmentApiFail:
                var imgPath3 = $"hist_{itemId}_att.pdf";
                await WriteImageAsync(imgPath3);
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: imgPath3, useTax: "NO", requesterId: "REQ-1"));
                _server
                    .Given(Request.Create().WithPath("/invoices/").UsingPost())
                    .RespondWith(Response.Create()
                        .WithStatusCode(201)
                        .WithHeader("Content-Type", "application/json")
                        .WithBody(JsonSerializer.Serialize(new { InvoiceId = $"INV-{itemId}" })));
                _server
                    .Given(Request.Create().WithPath($"/invoices/INV-{itemId}/child/attachments").UsingPost())
                    .RespondWith(Response.Create().WithStatusCode(400).WithBody("Bad Request"));
                break;

            case HistoryScenario.FullSuccess:
                var imgPath4 = $"hist_{itemId}_ok.pdf";
                await WriteImageAsync(imgPath4);
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: imgPath4, useTax: "NO", requesterId: "REQ-1"));
                _server
                    .Given(Request.Create().WithPath("/invoices/").UsingPost())
                    .RespondWith(Response.Create()
                        .WithStatusCode(201)
                        .WithHeader("Content-Type", "application/json")
                        .WithBody(JsonSerializer.Serialize(new { InvoiceId = $"INV-{itemId}" })));
                _server
                    .Given(Request.Create().WithPath($"/invoices/INV-{itemId}/child/attachments").UsingPost())
                    .RespondWith(Response.Create()
                        .WithStatusCode(201)
                        .WithHeader("Content-Type", "application/json")
                        .WithBody(JsonSerializer.Serialize(new { AttachedDocumentId = $"DOC-{itemId}" })));
                break;
        }

        var strategy = new InvitedClubPostStrategy(
            dbMock.Object,
            new S3ImageService(NullLogger<S3ImageService>.Instance),
            emailMock.Object,
            NullLogger<InvitedClubPostStrategy>.Instance);

        var config = BuildConfig();
        var context = new PostContext
        {
            TriggerType = "Manual",
            ItemIds = itemId.ToString(),
            UserId = 100,
            S3Config = new EdenredApiUrlConfig()
        };

        await strategy.ExecuteAsync(config, context, CancellationToken.None);

        _server.Reset();

        return (historySaveCount, expectedHistoryCount);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private GenericJobConfig BuildConfig() => new()
    {
        Id = 1,
        JobId = 42,
        HeaderTable = "WFInvitedClubsIndexHeader",
        AuthUsername = "user",
        AuthPassword = "pass",
        PostServiceUrl = _server.Urls[0] + "/invoices/",
        SuccessQueueId = SuccessQueueId,
        PrimaryFailQueueId = PrimaryFailQueueId,
        DefaultUserId = 100,
        IsLegacyJob = true,
        ImageParentPath = _tempDir + Path.DirectorySeparatorChar,
        ClientConfigJson = JsonSerializer.Serialize(new
        {
            EdenredFailQueueId,
            InvitedFailQueueId,
            ImagePostRetryLimit = 3
        })
    };

    private async Task WriteImageAsync(string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        if (!File.Exists(path))
            await File.WriteAllBytesAsync(path, new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }

    private static DataSet BuildDataSet(
        long itemId,
        string imagePath,
        string useTax,
        string requesterId)
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
            "PAYOR1", imagePath, useTax);

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
