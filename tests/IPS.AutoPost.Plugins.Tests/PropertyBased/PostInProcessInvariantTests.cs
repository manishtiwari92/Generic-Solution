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
/// PBT Property 27.1 — PostInProcess Invariant
///
/// PROPERTY: FOR ALL workitem processing scenarios (success, imageNotFound, apiFail, exception),
/// PostInProcess is always cleared to 0 after processing completes, regardless of outcome.
///
/// This is the most critical safety invariant in the platform. If PostInProcess is not cleared,
/// the workitem becomes permanently stuck and will never be processed again.
///
/// Tested via FsCheck generators that produce arbitrary combinations of:
///   - Item IDs (positive longs)
///   - Failure modes (success / imageNotFound / requesterIdEmpty / invoiceFail / attachmentFail / exception)
///   - UseTax values (YES / NO)
/// </summary>
public class PostInProcessInvariantTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly string _tempDir;

    public PostInProcessInvariantTests()
    {
        _server = WireMockServer.Start();
        _tempDir = Path.Combine(Path.GetTempPath(), "PBT_PostInProcess_" + Guid.NewGuid());
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
    // Failure mode enum for the generator
    // -----------------------------------------------------------------------

    public enum FailureMode
    {
        FullSuccess,
        ImageNotFound,
        RequesterIdEmpty,
        InvoiceApiFail,
        AttachmentApiFail,
        DbException
    }

    // -----------------------------------------------------------------------
    // FsCheck generators
    // -----------------------------------------------------------------------

    private static Gen<long> ItemIdGen =>
        Gen.Choose(1, 999_999).Select(i => (long)i);

    private static Gen<FailureMode> FailureModeGen =>
        Gen.Elements(
            FailureMode.FullSuccess,
            FailureMode.ImageNotFound,
            FailureMode.RequesterIdEmpty,
            FailureMode.InvoiceApiFail,
            FailureMode.AttachmentApiFail,
            FailureMode.DbException);

    private static Gen<string> UseTaxGen =>
        Gen.Elements("YES", "NO");

    // -----------------------------------------------------------------------
    // 27.1a — FsCheck property: PostInProcess cleared for all failure modes
    // -----------------------------------------------------------------------

    [Fact]
    public void PostInProcess_IsAlwaysClearedAfterProcessing_ForAllFailureModes()
    {
        // Arrange: generate (itemId, failureMode, useTax) triples
        var property = Prop.ForAll(
            ItemIdGen.ToArbitrary(),
            FailureModeGen.ToArbitrary(),
            UseTaxGen.ToArbitrary(),
            (itemId, failureMode, useTax) =>
            {
                // Run synchronously inside the property check
                var cleared = RunAndCheckPostInProcessCleared(itemId, failureMode, useTax).GetAwaiter().GetResult();
                return cleared;
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.1b — Explicit parametric tests for each failure mode
    // (deterministic, always run in CI regardless of FsCheck seed)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(FailureMode.FullSuccess)]
    [InlineData(FailureMode.ImageNotFound)]
    [InlineData(FailureMode.RequesterIdEmpty)]
    [InlineData(FailureMode.InvoiceApiFail)]
    [InlineData(FailureMode.AttachmentApiFail)]
    [InlineData(FailureMode.DbException)]
    public async Task PostInProcess_IsClearedAfterProcessing(FailureMode failureMode)
    {
        var cleared = await RunAndCheckPostInProcessCleared(itemId: 42_000L, failureMode, useTax: "NO");
        cleared.Should().BeTrue($"PostInProcess must be cleared after {failureMode}");
    }

    // -----------------------------------------------------------------------
    // Core test runner
    // -----------------------------------------------------------------------

    private async Task<bool> RunAndCheckPostInProcessCleared(
        long itemId,
        FailureMode failureMode,
        string useTax)
    {
        var clearWasCalled = false;

        var dbMock = new Mock<IInvitedClubPostDataAccess>(MockBehavior.Loose);
        var emailMock = new Mock<IEmailService>();

        // Track ClearPostInProcess call
        dbMock
            .Setup(db => db.ClearPostInProcessAsync(itemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => clearWasCalled = true)
            .Returns(Task.CompletedTask);

        dbMock
            .Setup(db => db.SetPostInProcessAsync(itemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

        // Configure DB data and WireMock based on failure mode
        switch (failureMode)
        {
            case FailureMode.DbException:
                // GetHeaderAndDetailData throws — simulates a DB crash mid-processing
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("Simulated DB crash"));
                break;

            case FailureMode.ImageNotFound:
                // Data exists but image file is missing
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: "missing_image.pdf", useTax: useTax, requesterId: "REQ-1"));
                break;

            case FailureMode.RequesterIdEmpty:
                // Image exists but RequesterId is empty
                var imgPath1 = $"invoice_{itemId}.pdf";
                await WriteImageAsync(imgPath1);
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: imgPath1, useTax: useTax, requesterId: ""));
                break;

            case FailureMode.InvoiceApiFail:
                var imgPath2 = $"invoice_{itemId}_inv.pdf";
                await WriteImageAsync(imgPath2);
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: imgPath2, useTax: useTax, requesterId: "REQ-1"));
                _server
                    .Given(Request.Create().WithPath("/invoices/").UsingPost())
                    .RespondWith(Response.Create().WithStatusCode(500).WithBody("Server Error"));
                break;

            case FailureMode.AttachmentApiFail:
                var imgPath3 = $"invoice_{itemId}_att.pdf";
                await WriteImageAsync(imgPath3);
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: imgPath3, useTax: useTax, requesterId: "REQ-1"));
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

            case FailureMode.FullSuccess:
                var imgPath4 = $"invoice_{itemId}_ok.pdf";
                await WriteImageAsync(imgPath4);
                dbMock
                    .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(BuildDataSet(itemId, imagePath: imgPath4, useTax: useTax, requesterId: "REQ-1"));
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

        // Act — should never throw; exceptions are caught internally
        await strategy.ExecuteAsync(config, context, CancellationToken.None);

        // Reset WireMock for next iteration
        _server.Reset();

        return clearWasCalled;
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
        SuccessQueueId = 200,
        PrimaryFailQueueId = 777,
        DefaultUserId = 100,
        IsLegacyJob = true,
        ImageParentPath = _tempDir + Path.DirectorySeparatorChar,
        ClientConfigJson = JsonSerializer.Serialize(new
        {
            EdenredFailQueueId = 888,
            InvitedFailQueueId = 999,
            ImagePostRetryLimit = 3
        })
    };

    private async Task WriteImageAsync(string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        if (!File.Exists(path))
            await File.WriteAllBytesAsync(path, new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF
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
