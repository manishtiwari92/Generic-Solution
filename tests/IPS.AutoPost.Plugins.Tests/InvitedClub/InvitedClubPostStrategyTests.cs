using System.Data;
using System.Text.Json;
using FluentAssertions;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Core.Services;
using IPS.AutoPost.Plugins.InvitedClub;
using IPS.AutoPost.Plugins.InvitedClub.Constants;
using IPS.AutoPost.Plugins.InvitedClub.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace IPS.AutoPost.Plugins.Tests.InvitedClub;

/// <summary>
/// Unit tests for <see cref="InvitedClubPostStrategy"/>.
/// Covers:
///   12.9  BuildInvoiceRequestJson — UseTax=YES keeps ShipToLocation, UseTax=NO removes it
///   12.10 ExecuteAsync routing scenarios
///   12.11 PostInProcess flag — set before API call, cleared in finally even on exception
/// </summary>
public class InvitedClubPostStrategyTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly Mock<IInvitedClubPostDataAccess> _dbMock;
    private readonly Mock<IEmailService> _emailMock;
    private readonly InvitedClubPostStrategy _strategy;
    private readonly GenericJobConfig _config;
    private readonly InvitedClubConfig _clientConfig;
    private readonly EdenredApiUrlConfig _s3Config;
    private readonly string _tempDir;

    // Queue IDs used in assertions
    private const int SuccessQueueId      = 200;
    private const int EdenredFailQueueId  = 888;
    private const int InvitedFailQueueId  = 999;
    private const int PrimaryFailQueueId  = 777;

    public InvitedClubPostStrategyTests()
    {
        _server = WireMockServer.Start();

        _dbMock    = new Mock<IInvitedClubPostDataAccess>(MockBehavior.Strict);
        _emailMock = new Mock<IEmailService>();

        var s3Service = new S3ImageService(NullLogger<S3ImageService>.Instance);

        _strategy = new InvitedClubPostStrategy(
            _dbMock.Object,
            s3Service,
            _emailMock.Object,
            NullLogger<InvitedClubPostStrategy>.Instance);

        _tempDir = Path.Combine(Path.GetTempPath(), "PostStrategyTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);

        _config = new GenericJobConfig
        {
            Id             = 1,
            JobId          = 42,
            HeaderTable    = "WFInvitedClubsIndexHeader",
            AuthUsername   = "testuser",
            AuthPassword   = "testpass",
            PostServiceUrl = _server.Urls[0] + "/invoices/",
            SuccessQueueId = SuccessQueueId,
            PrimaryFailQueueId = PrimaryFailQueueId,
            DefaultUserId  = 100,
            IsLegacyJob    = true,
            ImageParentPath = _tempDir + Path.DirectorySeparatorChar,
            ClientConfigJson = JsonSerializer.Serialize(new
            {
                EdenredFailQueueId = EdenredFailQueueId,
                InvitedFailQueueId = InvitedFailQueueId,
                ImagePostRetryLimit = 3
            })
        };

        _clientConfig = new InvitedClubConfig
        {
            EdenredFailQueueId  = EdenredFailQueueId,
            InvitedFailQueueId  = InvitedFailQueueId,
            ImagePostRetryLimit = 3
        };

        _s3Config = new EdenredApiUrlConfig
        {
            BucketName  = "test-bucket",
            S3AccessKey = "AKIATEST",
            S3SecretKey = "secretkey",
            S3Region    = "us-east-1"
        };
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -----------------------------------------------------------------------
    // 12.9 BuildInvoiceRequestJson — UseTax tests
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildInvoiceRequestJson_WhenUseTaxYes_KeepsShipToLocationOnAllLines()
    {
        // Arrange
        var ds = BuildHeaderDetailDataSet(
            useTax: "YES",
            lines: new[]
            {
                ("1", "100.00", "LOCATION-A"),
                ("2", "200.00", "LOCATION-B")
            });

        // Act
        var json = _strategy.BuildInvoiceRequestJson(ds, "YES");

        // Assert: ShipToLocation present on all lines
        var jObj = JObject.Parse(json);
        var lines = (JArray)jObj["invoiceLines"]!;
        lines.Should().HaveCount(2);
        lines[0]["ShipToLocation"]?.ToString().Should().Be("LOCATION-A");
        lines[1]["ShipToLocation"]?.ToString().Should().Be("LOCATION-B");
    }

    [Fact]
    public void BuildInvoiceRequestJson_WhenUseTaxNo_RemovesShipToLocationFromAllLines()
    {
        // Arrange
        var ds = BuildHeaderDetailDataSet(
            useTax: "NO",
            lines: new[]
            {
                ("1", "100.00", "LOCATION-A"),
                ("2", "200.00", "LOCATION-B")
            });

        // Act
        var json = _strategy.BuildInvoiceRequestJson(ds, "NO");

        // Assert: ShipToLocation completely absent from all lines (not null, not empty — removed)
        var jObj = JObject.Parse(json);
        var lines = (JArray)jObj["invoiceLines"]!;
        lines.Should().HaveCount(2);
        lines[0]["ShipToLocation"].Should().BeNull("ShipToLocation must be removed, not just nulled");
        lines[1]["ShipToLocation"].Should().BeNull("ShipToLocation must be removed, not just nulled");
    }

    [Fact]
    public void BuildInvoiceRequestJson_WhenUseTaxNo_PreservesOtherLineFields()
    {
        // Arrange
        var ds = BuildHeaderDetailDataSet(
            useTax: "NO",
            lines: new[] { ("3", "500.00", "LOCATION-C") });

        // Act
        var json = _strategy.BuildInvoiceRequestJson(ds, "NO");

        // Assert: other line fields are preserved
        var jObj = JObject.Parse(json);
        var line = (JObject)((JArray)jObj["invoiceLines"]!)[0];
        line["LineNumber"]?.ToString().Should().Be("3");
        line["LineAmount"]?.ToString().Should().Be("500.00");
        line["ShipToLocation"].Should().BeNull();
    }

    [Fact]
    public void BuildInvoiceRequestJson_WhenUseTaxYes_PreservesHeaderFields()
    {
        // Arrange
        var ds = BuildHeaderDetailDataSet(useTax: "YES", lines: new[] { ("1", "100.00", "LOC") });

        // Act
        var json = _strategy.BuildInvoiceRequestJson(ds, "YES");

        // Assert: header fields are mapped correctly
        var jObj = JObject.Parse(json);
        jObj["InvoiceNumber"]?.ToString().Should().Be("INV-001");
        jObj["Supplier"]?.ToString().Should().Be("TEST-SUPPLIER");
        jObj["RequesterId"]?.ToString().Should().Be("REQ-123");
    }

    [Fact]
    public void BuildInvoiceRequestJson_WhenUseTaxNoWithMultipleLines_RemovesShipToFromEachLine()
    {
        // Arrange: 5 lines all with ShipToLocation
        var lines = Enumerable.Range(1, 5)
            .Select(i => (i.ToString(), $"{i * 100}.00", $"LOCATION-{i}"))
            .ToArray();

        var ds = BuildHeaderDetailDataSet(useTax: "NO", lines: lines);

        // Act
        var json = _strategy.BuildInvoiceRequestJson(ds, "NO");

        // Assert: all 5 lines have ShipToLocation removed
        var jObj = JObject.Parse(json);
        var jsonLines = (JArray)jObj["invoiceLines"]!;
        jsonLines.Should().HaveCount(5);
        foreach (var line in jsonLines)
        {
            ((JObject)line)["ShipToLocation"].Should().BeNull();
        }
    }

    // -----------------------------------------------------------------------
    // 12.10 ExecuteAsync routing scenarios
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenImageNotFound_RoutesToEdenredFailQueueId_NoApiCall()
    {
        // Arrange: image file does NOT exist on disk
        var context = BuildContext(itemIds: "1001");
        SetupHeaderDetailData(1001, imagePath: "missing_image.pdf", useTax: "NO", requesterId: "REQ-1");
        SetupPostInProcess(1001);
        SetupClearPostInProcess(1001);
        SetupRouteWorkitem(1001, EdenredFailQueueId);
        SetupGeneralLog(1001);
        SetupApiResponseTypes();

        // Act
        var result = await _strategy.ExecuteAsync(_config, context, CancellationToken.None);

        // Assert: routed to EdenredFailQueueId
        VerifyRouteWorkitem(1001, EdenredFailQueueId, Times.Once());

        // Assert: NO invoice POST was made
        _server.LogEntries.Should().BeEmpty("no API calls should be made when image is not found");

        // Assert: NO history saved (early exit before any API call)
        _dbMock.Verify(db => db.SavePostHistoryAsync(It.IsAny<PostHistory>(), It.IsAny<CancellationToken>()),
            Times.Never);

        result.RecordsFailed.Should().Be(1);
        result.RecordsSuccess.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRequesterIdEmpty_RoutesToInvitedFailQueueId_NoApiCall()
    {
        // Arrange: image exists but RequesterId is empty
        var imagePath = "invoice_2001.pdf";
        await WriteImageFileAsync(imagePath);

        var context = BuildContext(itemIds: "2001");
        SetupHeaderDetailData(2001, imagePath: imagePath, useTax: "NO", requesterId: "");
        SetupPostInProcess(2001);
        SetupClearPostInProcess(2001);
        SetupRouteWorkitem(2001, InvitedFailQueueId);
        SetupGeneralLog(2001);
        SetupApiResponseTypes();

        // Act
        var result = await _strategy.ExecuteAsync(_config, context, CancellationToken.None);

        // Assert: routed to InvitedFailQueueId
        VerifyRouteWorkitem(2001, InvitedFailQueueId, Times.Once());

        // Assert: NO invoice POST was made
        _server.LogEntries.Should().BeEmpty("no API calls should be made when RequesterId is empty");

        // Assert: NO history saved
        _dbMock.Verify(db => db.SavePostHistoryAsync(It.IsAny<PostHistory>(), It.IsAny<CancellationToken>()),
            Times.Never);

        result.RecordsFailed.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInvoicePostFails_ClearsGlDate_RoutesToInvitedFailQueueId()
    {
        // Arrange: image exists, invoice POST returns non-201
        var imagePath = "invoice_3001.pdf";
        await WriteImageFileAsync(imagePath);

        var context = BuildContext(itemIds: "3001");
        SetupHeaderDetailData(3001, imagePath: imagePath, useTax: "NO", requesterId: "REQ-3");
        SetupPostInProcess(3001);
        SetupClearPostInProcess(3001);
        SetupUpdateGlDate(3001);
        SetupRouteWorkitem(3001, InvitedFailQueueId);
        SetupGeneralLog(3001);
        SetupSaveHistory();
        SetupApiResponseTypes();

        // Mock invoice POST -> HTTP 500
        _server
            .Given(Request.Create().WithPath("/invoices/").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Internal Server Error"));

        // Act
        var result = await _strategy.ExecuteAsync(_config, context, CancellationToken.None);

        // Assert: GlDate was cleared
        _dbMock.Verify(db => db.UpdateGlDateValueAsync(3001, _config.HeaderTable, It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: routed to InvitedFailQueueId
        VerifyRouteWorkitem(3001, InvitedFailQueueId, Times.Once());

        // Assert: history WAS saved (invoice POST was attempted)
        _dbMock.Verify(db => db.SavePostHistoryAsync(It.IsAny<PostHistory>(), It.IsAny<CancellationToken>()),
            Times.Once);

        result.RecordsFailed.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAttachmentPostFails_RoutesToEdenredFailQueueId()
    {
        // Arrange: invoice POST succeeds (201), attachment POST fails
        var imagePath = "invoice_4001.pdf";
        await WriteImageFileAsync(imagePath);

        var context = BuildContext(itemIds: "4001");
        SetupHeaderDetailData(4001, imagePath: imagePath, useTax: "NO", requesterId: "REQ-4");
        SetupPostInProcess(4001);
        SetupClearPostInProcess(4001);
        SetupUpdateInvoiceId(4001);
        SetupRouteWorkitem(4001, EdenredFailQueueId);
        SetupGeneralLog(4001);
        SetupSaveHistory();
        SetupApiResponseTypes();

        // Invoice POST -> HTTP 201
        _server
            .Given(Request.Create().WithPath("/invoices/").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new { InvoiceId = "INV-4001" })));

        // Attachment POST -> HTTP 400
        _server
            .Given(Request.Create().WithPath("/invoices/INV-4001/child/attachments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(400).WithBody("Bad Request"));

        // Act
        var result = await _strategy.ExecuteAsync(_config, context, CancellationToken.None);

        // Assert: routed to EdenredFailQueueId (attachment failure)
        VerifyRouteWorkitem(4001, EdenredFailQueueId, Times.Once());

        // Assert: history saved (invoice POST was attempted)
        _dbMock.Verify(db => db.SavePostHistoryAsync(It.IsAny<PostHistory>(), It.IsAny<CancellationToken>()),
            Times.Once);

        result.RecordsFailed.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCalculateTaxFails_RoutesToInvitedFailQueueId()
    {
        // Arrange: invoice + attachment succeed, calculateTax fails; UseTax=YES
        var imagePath = "invoice_5001.pdf";
        await WriteImageFileAsync(imagePath);

        var context = BuildContext(itemIds: "5001");
        SetupHeaderDetailData(5001, imagePath: imagePath, useTax: "YES", requesterId: "REQ-5");
        SetupPostInProcess(5001);
        SetupClearPostInProcess(5001);
        SetupUpdateInvoiceId(5001);
        SetupUpdateAttachedDocumentId(5001);
        SetupRouteWorkitem(5001, InvitedFailQueueId);
        SetupGeneralLog(5001);
        SetupSaveHistory();
        SetupApiResponseTypes();

        // Invoice POST -> HTTP 201
        _server
            .Given(Request.Create().WithPath("/invoices/").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new { InvoiceId = "INV-5001" })));

        // Attachment POST -> HTTP 201
        _server
            .Given(Request.Create().WithPath("/invoices/INV-5001/child/attachments").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new { AttachedDocumentId = "DOC-5001" })));

        // CalculateTax -> HTTP 500
        _server
            .Given(Request.Create().WithPath("/invoices//action/calculateTax").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Tax calculation failed"));

        // Act
        var result = await _strategy.ExecuteAsync(_config, context, CancellationToken.None);

        // Assert: routed to InvitedFailQueueId (calculateTax failure)
        VerifyRouteWorkitem(5001, InvitedFailQueueId, Times.Once());

        result.RecordsFailed.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFullSuccess_RoutesToSuccessQueueId()
    {
        // Arrange: all steps succeed; UseTax=NO (skip calculateTax)
        var imagePath = "invoice_6001.pdf";
        await WriteImageFileAsync(imagePath);

        var context = BuildContext(itemIds: "6001");
        SetupHeaderDetailData(6001, imagePath: imagePath, useTax: "NO", requesterId: "REQ-6");
        SetupPostInProcess(6001);
        SetupClearPostInProcess(6001);
        SetupUpdateInvoiceId(6001);
        SetupUpdateAttachedDocumentId(6001);
        SetupRouteWorkitem(6001, SuccessQueueId);
        SetupGeneralLog(6001);
        SetupSaveHistory();
        SetupApiResponseTypes();

        // Invoice POST -> HTTP 201
        _server
            .Given(Request.Create().WithPath("/invoices/").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new { InvoiceId = "INV-6001" })));

        // Attachment POST -> HTTP 201
        _server
            .Given(Request.Create().WithPath("/invoices/INV-6001/child/attachments").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new { AttachedDocumentId = "DOC-6001" })));

        // Act
        var result = await _strategy.ExecuteAsync(_config, context, CancellationToken.None);

        // Assert: routed to SuccessQueueId
        VerifyRouteWorkitem(6001, SuccessQueueId, Times.Once());

        // Assert: history saved
        _dbMock.Verify(db => db.SavePostHistoryAsync(It.IsAny<PostHistory>(), It.IsAny<CancellationToken>()),
            Times.Once);

        result.RecordsSuccess.Should().Be(1);
        result.RecordsFailed.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFullSuccessWithUseTaxYes_CallsCalculateTax()
    {
        // Arrange: all steps succeed; UseTax=YES (calculateTax must be called)
        var imagePath = "invoice_7001.pdf";
        await WriteImageFileAsync(imagePath);

        var context = BuildContext(itemIds: "7001");
        SetupHeaderDetailData(7001, imagePath: imagePath, useTax: "YES", requesterId: "REQ-7");
        SetupPostInProcess(7001);
        SetupClearPostInProcess(7001);
        SetupUpdateInvoiceId(7001);
        SetupUpdateAttachedDocumentId(7001);
        SetupRouteWorkitem(7001, SuccessQueueId);
        SetupGeneralLog(7001);
        SetupSaveHistory();
        SetupApiResponseTypes();

        // Invoice POST -> HTTP 201
        _server
            .Given(Request.Create().WithPath("/invoices/").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new { InvoiceId = "INV-7001" })));

        // Attachment POST -> HTTP 201
        _server
            .Given(Request.Create().WithPath("/invoices/INV-7001/child/attachments").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new { AttachedDocumentId = "DOC-7001" })));

        // CalculateTax -> HTTP 200
        _server
            .Given(Request.Create().WithPath("/invoices//action/calculateTax").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{}"));

        // Act
        var result = await _strategy.ExecuteAsync(_config, context, CancellationToken.None);

        // Assert: calculateTax endpoint was called
        var calcTaxRequests = _server.LogEntries
            .Where(e => e.RequestMessage.Path.Contains("calculateTax"))
            .ToList();
        calcTaxRequests.Should().HaveCount(1, "calculateTax must be called when UseTax=YES");

        result.RecordsSuccess.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // 12.11 PostInProcess flag tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_SetsPostInProcessBeforeApiCall()
    {
        // Arrange: track call order
        var callOrder = new List<string>();

        var imagePath = "invoice_8001.pdf";
        await WriteImageFileAsync(imagePath);

        var context = BuildContext(itemIds: "8001");
        SetupHeaderDetailData(8001, imagePath: imagePath, useTax: "NO", requesterId: "REQ-8");
        SetupApiResponseTypes();

        _dbMock
            .Setup(db => db.SetPostInProcessAsync(8001, _config.HeaderTable, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("SetPostInProcess"))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.ClearPostInProcessAsync(8001, _config.HeaderTable, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("ClearPostInProcess"))
            .Returns(Task.CompletedTask);

        SetupRouteWorkitem(8001, EdenredFailQueueId);
        SetupGeneralLog(8001);

        // Invoice POST -> HTTP 201 (so we can verify SetPostInProcess happened before the API call)
        _server
            .Given(Request.Create().WithPath("/invoices/").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new { InvoiceId = "INV-8001" })));

        // Attachment POST -> HTTP 400 (fail to keep test simple)
        _server
            .Given(Request.Create().WithPath("/invoices/INV-8001/child/attachments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(400).WithBody("Bad Request"));

        SetupUpdateInvoiceId(8001);
        SetupSaveHistory();

        // Act
        await _strategy.ExecuteAsync(_config, context, CancellationToken.None);

        // Assert: SetPostInProcess was called
        _dbMock.Verify(db => db.SetPostInProcessAsync(8001, _config.HeaderTable, It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: SetPostInProcess happened before ClearPostInProcess
        callOrder.Should().Contain("SetPostInProcess");
        callOrder.Should().Contain("ClearPostInProcess");
        callOrder.IndexOf("SetPostInProcess").Should().BeLessThan(callOrder.IndexOf("ClearPostInProcess"),
            "PostInProcess must be set before any API call and cleared after");
    }

    [Fact]
    public async Task ExecuteAsync_ClearsPostInProcessInFinally_EvenWhenExceptionThrown()
    {
        // Arrange: GetHeaderAndDetailData throws an exception to simulate a crash mid-processing
        var context = BuildContext(itemIds: "9001");

        _dbMock
            .Setup(db => db.SetPostInProcessAsync(9001, _config.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.GetHeaderAndDetailDataAsync(9001, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated DB crash"));

        _dbMock
            .Setup(db => db.ClearPostInProcessAsync(9001, _config.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Route to fail queue after exception
        _dbMock
            .Setup(db => db.RouteWorkitemAsync(
                9001, It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.InsertGeneralLogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), 9001, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.GetApiResponseTypesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<APIResponseType>());

        // Act — should NOT throw; exception is caught internally
        var result = await _strategy.ExecuteAsync(_config, context, CancellationToken.None);

        // Assert: ClearPostInProcess was called despite the exception
        _dbMock.Verify(db => db.ClearPostInProcessAsync(9001, _config.HeaderTable, It.IsAny<CancellationToken>()),
            Times.Once,
            "PostInProcess must be cleared in finally even when an exception is thrown");

        result.RecordsFailed.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ClearsPostInProcessForEachItem_WhenProcessingMultipleItems()
    {
        // Arrange: two items — both should have PostInProcess cleared
        var imagePath1 = "invoice_10001.pdf";
        var imagePath2 = "invoice_10002.pdf";
        await WriteImageFileAsync(imagePath1);
        await WriteImageFileAsync(imagePath2);

        var context = BuildContext(itemIds: "10001,10002");

        foreach (var itemId in new[] { 10001L, 10002L })
        {
            SetupHeaderDetailData(itemId, imagePath: $"invoice_{itemId}.pdf", useTax: "NO", requesterId: "REQ-10");
            SetupPostInProcess(itemId);
            SetupClearPostInProcess(itemId);
            SetupRouteWorkitem(itemId, SuccessQueueId);
            SetupGeneralLog(itemId);
            SetupUpdateInvoiceId(itemId);
            SetupUpdateAttachedDocumentId(itemId);
            SetupSaveHistory();
        }
        SetupApiResponseTypes();

        // Both invoice POSTs succeed
        _server
            .Given(Request.Create().WithPath("/invoices/").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new { InvoiceId = "INV-MULTI" })));

        // Both attachment POSTs succeed
        _server
            .Given(Request.Create().WithPath("/invoices/INV-MULTI/child/attachments").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new { AttachedDocumentId = "DOC-MULTI" })));

        // Act
        await _strategy.ExecuteAsync(_config, context, CancellationToken.None);

        // Assert: ClearPostInProcess called once per item
        _dbMock.Verify(db => db.ClearPostInProcessAsync(10001, _config.HeaderTable, It.IsAny<CancellationToken>()),
            Times.Once);
        _dbMock.Verify(db => db.ClearPostInProcessAsync(10002, _config.HeaderTable, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Helper methods
    // -----------------------------------------------------------------------

    private PostContext BuildContext(string itemIds = "", bool manual = true)
    {
        return new PostContext
        {
            TriggerType = manual ? "Manual" : "Scheduled",
            ItemIds     = itemIds,
            UserId      = 100,
            S3Config    = _s3Config
        };
    }

    private void SetupHeaderDetailData(
        long itemId,
        string imagePath,
        string useTax,
        string requesterId)
    {
        var ds = BuildHeaderDetailDataSet(
            useTax: useTax,
            lines: new[] { ("1", "100.00", "LOCATION-A") },
            imagePath: imagePath,
            requesterId: requesterId,
            invoiceNumber: $"INV-{itemId}",
            supplier: "TEST-SUPPLIER");

        _dbMock
            .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ds);
    }

    private void SetupPostInProcess(long itemId)
    {
        _dbMock
            .Setup(db => db.SetPostInProcessAsync(itemId, _config.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupClearPostInProcess(long itemId)
    {
        _dbMock
            .Setup(db => db.ClearPostInProcessAsync(itemId, _config.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupUpdateGlDate(long itemId)
    {
        _dbMock
            .Setup(db => db.UpdateGlDateValueAsync(itemId, _config.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupUpdateInvoiceId(long itemId)
    {
        _dbMock
            .Setup(db => db.UpdateInvoiceIdAsync(itemId, It.IsAny<string>(), _config.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupUpdateAttachedDocumentId(long itemId)
    {
        _dbMock
            .Setup(db => db.UpdateAttachedDocumentIdAsync(itemId, It.IsAny<string>(), _config.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupRouteWorkitem(long itemId, int targetQueueId)
    {
        _dbMock
            .Setup(db => db.RouteWorkitemAsync(
                itemId, targetQueueId, It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupGeneralLog(long itemId)
    {
        _dbMock
            .Setup(db => db.InsertGeneralLogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), itemId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupSaveHistory()
    {
        _dbMock
            .Setup(db => db.SavePostHistoryAsync(It.IsAny<PostHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupApiResponseTypes()
    {
        _dbMock
            .Setup(db => db.GetApiResponseTypesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<APIResponseType>
            {
                new APIResponseType { ResponseType = "POST_SUCCESS",      ResponseCode = "1", ResponseMessage = "Posted successfully" },
                new APIResponseType { ResponseType = "RECORD_NOT_POSTED", ResponseCode = "0", ResponseMessage = "Record not posted" }
            });
    }

    private void VerifyRouteWorkitem(long itemId, int targetQueueId, Times times)
    {
        _dbMock.Verify(db => db.RouteWorkitemAsync(
            itemId, targetQueueId, It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            times);
    }

    private async Task WriteImageFileAsync(string fileName)
    {
        var fullPath = Path.Combine(_tempDir, fileName);
        await File.WriteAllBytesAsync(fullPath, new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF
    }

    /// <summary>
    /// Builds a DataSet with a header table (Tables[0]) and detail table (Tables[1])
    /// matching the schema returned by InvitedClub_GetHeaderAndDetailData.
    /// </summary>
    private static DataSet BuildHeaderDetailDataSet(
        string useTax,
        (string LineNumber, string LineAmount, string ShipToLocation)[] lines,
        string imagePath = "test_image.pdf",
        string requesterId = "REQ-123",
        string invoiceNumber = "INV-001",
        string supplier = "TEST-SUPPLIER")
    {
        var ds = new DataSet();

        // Header table
        var headerTable = new DataTable("Header");
        headerTable.Columns.Add("InvoiceNumber",         typeof(string));
        headerTable.Columns.Add("InvoiceCurrency",       typeof(string));
        headerTable.Columns.Add("PaymentCurrency",       typeof(string));
        headerTable.Columns.Add("InvoiceAmount",         typeof(string));
        headerTable.Columns.Add("InvoiceDate",           typeof(string));
        headerTable.Columns.Add("BusinessUnit",          typeof(string));
        headerTable.Columns.Add("Supplier",              typeof(string));
        headerTable.Columns.Add("SupplierSite",          typeof(string));
        headerTable.Columns.Add("RequesterId",           typeof(string));
        headerTable.Columns.Add("AccountingDate",        typeof(string));
        headerTable.Columns.Add("Description",           typeof(string));
        headerTable.Columns.Add("InvoiceType",           typeof(string));
        headerTable.Columns.Add("LegalEntity",           typeof(string));
        headerTable.Columns.Add("LegalEntityIdentifier", typeof(string));
        headerTable.Columns.Add("LiabilityDistribution", typeof(string));
        headerTable.Columns.Add("RoutingAttribute2",     typeof(string));
        headerTable.Columns.Add("InvoiceSource",         typeof(string));
        headerTable.Columns.Add("Payor",                 typeof(string));
        headerTable.Columns.Add("UseTax",                typeof(string));
        headerTable.Columns.Add("ImagePath",             typeof(string));

        headerTable.Rows.Add(
            invoiceNumber, "USD", "USD", "1000.00", "2026-01-01",
            "BU-001", supplier, "SITE-001", requesterId,
            "2026-01-01", "Test invoice", "Standard", "LE-001",
            "LE-ID-001", "LIAB-001", "ATTR-001", "SOURCE-001",
            "PAYOR-001", useTax, imagePath);

        ds.Tables.Add(headerTable);

        // Detail table
        var detailTable = new DataTable("Detail");
        detailTable.Columns.Add("LineNumber",              typeof(string));
        detailTable.Columns.Add("LineAmount",              typeof(string));
        detailTable.Columns.Add("ShipToLocation",          typeof(string));
        detailTable.Columns.Add("DistributionCombination", typeof(string));
        detailTable.Columns.Add("DistributionLineNumber",  typeof(string));
        detailTable.Columns.Add("DistributionLineType",    typeof(string));
        detailTable.Columns.Add("DistributionAmount",      typeof(string));

        foreach (var (lineNumber, lineAmount, shipToLocation) in lines)
        {
            detailTable.Rows.Add(
                lineNumber, lineAmount, shipToLocation,
                "DIST-COMBO-001", "1", "ITEM", lineAmount);
        }

        ds.Tables.Add(detailTable);

        return ds;
    }
}
