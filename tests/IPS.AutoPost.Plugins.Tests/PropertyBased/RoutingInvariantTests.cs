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
/// PBT Property 27.2 — No-Duplicate Routing Invariant
///
/// PROPERTY: FOR ALL processing scenarios, each workitem is routed to exactly ONE
/// destination queue, and that queue is never the source queue.
///
/// This prevents:
///   - Double-routing (workitem appears in two queues simultaneously)
///   - Routing back to the source queue (infinite processing loop)
///
/// Tested via FsCheck generators that produce arbitrary combinations of:
///   - Item IDs
///   - Failure modes (all routing paths)
///   - Source queue IDs (to verify the destination is never the source)
/// </summary>
public class RoutingInvariantTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly string _tempDir;

    // Queue IDs used in the test config
    private const int SourceQueueId = 100;
    private const int SuccessQueueId = 200;
    private const int EdenredFailQueueId = 888;
    private const int InvitedFailQueueId = 999;
    private const int PrimaryFailQueueId = 777;

    public RoutingInvariantTests()
    {
        _server = WireMockServer.Start();
        _tempDir = Path.Combine(Path.GetTempPath(), "PBT_Routing_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    public enum RoutingScenario
    {
        FullSuccess,
        ImageNotFound,
        RequesterIdEmpty,
        InvoiceApiFail,
        AttachmentApiFail,
        CalculateTaxFail
    }

    private static Gen<long> ItemIdGen =>
        Gen.Choose(1, 999_999).Select(i => (long)i);

    private static Gen<RoutingScenario> ScenarioGen =>
        Gen.Elements(
            RoutingScenario.FullSuccess,
            RoutingScenario.ImageNotFound,
            RoutingScenario.RequesterIdEmpty,
            RoutingScenario.InvoiceApiFail,
            RoutingScenario.AttachmentApiFail,
            RoutingScenario.CalculateTaxFail);

    // -----------------------------------------------------------------------
    // 27.2a — FsCheck property: exactly one routing call, never to source queue
    // -----------------------------------------------------------------------

    [Fact]
    public void Routing_ExactlyOneDestinationQueue_NeverSourceQueue_ForAllScenarios()
    {
        var property = Prop.ForAll(
            ItemIdGen.ToArbitrary(),
            ScenarioGen.ToArbitrary(),
            (itemId, scenario) =>
            {
                var (routeCount, destinationQueues) =
                    RunAndCaptureRouting(itemId, scenario).GetAwaiter().GetResult();

                // Exactly one routing call
                var exactlyOne = routeCount == 1;

                // Destination is never the source queue
                var notSourceQueue = destinationQueues.All(q => q != SourceQueueId);

                // Destination is one of the known valid queues
                var validDestination = destinationQueues.All(q =>
                    q == SuccessQueueId ||
                    q == EdenredFailQueueId ||
                    q == InvitedFailQueueId ||
                    q == PrimaryFailQueueId);

                return exactlyOne && notSourceQueue && validDestination;
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.2b — Explicit parametric tests for each routing scenario
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(RoutingScenario.FullSuccess, SuccessQueueId)]
    [InlineData(RoutingScenario.ImageNotFound, EdenredFailQueueId)]
    [InlineData(RoutingScenario.RequesterIdEmpty, InvitedFailQueueId)]
    [InlineData(RoutingScenario.InvoiceApiFail, InvitedFailQueueId)]
    [InlineData(RoutingScenario.AttachmentApiFail, EdenredFailQueueId)]
    [InlineData(RoutingScenario.CalculateTaxFail, InvitedFailQueueId)]
    public async Task Routing_CorrectDestinationQueue_ForScenario(
        RoutingScenario scenario,
        int expectedQueue)
    {
        var (routeCount, destinationQueues) = await RunAndCaptureRouting(50_000L, scenario);

        routeCount.Should().Be(1, $"exactly one routing call expected for {scenario}");
        destinationQueues.Should().ContainSingle()
            .Which.Should().Be(expectedQueue, $"wrong destination queue for {scenario}");
    }

    [Theory]
    [InlineData(RoutingScenario.FullSuccess)]
    [InlineData(RoutingScenario.ImageNotFound)]
    [InlineData(RoutingScenario.RequesterIdEmpty)]
    [InlineData(RoutingScenario.InvoiceApiFail)]
    [InlineData(RoutingScenario.AttachmentApiFail)]
    [InlineData(RoutingScenario.CalculateTaxFail)]
    public async Task Routing_NeverRoutesToSourceQueue(RoutingScenario scenario)
    {
        var (_, destinationQueues) = await RunAndCaptureRouting(60_000L, scenario);

        destinationQueues.Should().NotContain(SourceQueueId,
            $"workitem must never be routed back to the source queue ({SourceQueueId}) in scenario {scenario}");
    }

    // -----------------------------------------------------------------------
    // Core test runner
    // -----------------------------------------------------------------------

    private async Task<(int RouteCount, List<int> DestinationQueues)> RunAndCaptureRouting(
        long itemId,
        RoutingScenario scenario)
    {
        var routeCount = 0;
        var destinationQueues = new List<int>();

        var dbMock = new Mock<IInvitedClubPostDataAccess>(MockBehavior.Loose);
        var emailMock = new Mock<IEmailService>();

        // Capture routing calls
        dbMock
            .Setup(db => db.RouteWorkitemAsync(
                itemId, It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<long, int, int, string, string, CancellationToken>(
                (_, queueId, _, _, _, _) =>
                {
                    routeCount++;
                    destinationQueues.Add(queueId);
                })
            .Returns(Task.CompletedTask);

        dbMock
            .Setup(db => db.SetPostInProcessAsync(itemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.ClearPostInProcessAsync(itemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.UpdateGlDateValueAsync(itemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.UpdateInvoiceIdAsync(itemId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.UpdateAttachedDocumentIdAsync(itemId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Configure scenario-specific data and API mocks
        switch (scenario)
        {
            case RoutingScenario.ImageNotFound:
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: "missing.pdf", useTax: "NO", requesterId: "REQ-1"));
                break;

            case RoutingScenario.RequesterIdEmpty:
                var imgPath1 = $"route_{itemId}_req.pdf";
                await WriteImageAsync(imgPath1);
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: imgPath1, useTax: "NO", requesterId: ""));
                break;

            case RoutingScenario.InvoiceApiFail:
                var imgPath2 = $"route_{itemId}_inv.pdf";
                await WriteImageAsync(imgPath2);
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: imgPath2, useTax: "NO", requesterId: "REQ-1"));
                _server
                    .Given(Request.Create().WithPath("/invoices/").UsingPost())
                    .RespondWith(Response.Create().WithStatusCode(500).WithBody("Error"));
                break;

            case RoutingScenario.AttachmentApiFail:
                var imgPath3 = $"route_{itemId}_att.pdf";
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

            case RoutingScenario.CalculateTaxFail:
                var imgPath4 = $"route_{itemId}_tax.pdf";
                await WriteImageAsync(imgPath4);
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: imgPath4, useTax: "YES", requesterId: "REQ-1"));
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
                _server
                    .Given(Request.Create().WithPath("/invoices//action/calculateTax").UsingPost())
                    .RespondWith(Response.Create().WithStatusCode(500).WithBody("Tax Error"));
                break;

            case RoutingScenario.FullSuccess:
                var imgPath5 = $"route_{itemId}_ok.pdf";
                await WriteImageAsync(imgPath5);
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: imgPath5, useTax: "NO", requesterId: "REQ-1"));
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

        return (routeCount, destinationQueues);
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
