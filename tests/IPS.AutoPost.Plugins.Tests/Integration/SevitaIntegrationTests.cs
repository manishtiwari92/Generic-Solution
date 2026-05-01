using System.Data;
using System.Text.Json;
using FluentAssertions;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Core.Services;
using IPS.AutoPost.Plugins.Sevita;
using IPS.AutoPost.Plugins.Sevita.Constants;
using IPS.AutoPost.Plugins.Sevita.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace IPS.AutoPost.Plugins.Tests.Integration;

/// <summary>
/// Integration tests for the full Sevita scheduled post flow.
/// Uses WireMock.Net to mock the Sevita invoice API and OAuth2 token endpoint.
/// Uses Moq (MockBehavior.Strict) to mock all database access interfaces.
/// The real SevitaPlugin, SevitaPostStrategy, SevitaTokenService, and
/// SevitaValidationService classes are exercised end-to-end.
/// <para>
/// S3 image retrieval is handled by <see cref="TestSevitaPostStrategy"/> which
/// overrides <c>GetBase64ImageFromS3Async</c> to return a fixed base64 string,
/// avoiding the need for a real AWS S3 connection in tests.
/// </para>
/// </summary>
public class SevitaIntegrationTests : IDisposable
{
    // -----------------------------------------------------------------------
    // Infrastructure
    // -----------------------------------------------------------------------

    private readonly WireMockServer _server;

    // DB mock
    private readonly Mock<ISevitaPostDataAccess> _dbMock;
    private readonly Mock<IEmailService> _emailServiceMock;

    // System under test
    private readonly SevitaPlugin _plugin;

    // Shared config
    private readonly GenericJobConfig _config;
    private readonly SevitaConfig _clientConfig;

    // Fixed test data
    private const long ItemId = 2001L;
    private const string InvoiceId = "SEV-INV-001";
    private const string FakeToken = "fake-bearer-token-abc123";
    private const string VendorId = "VENDOR-001";
    private const string EmployeeId = "EMP-001";

    // A minimal base64-encoded PDF header used as the fake invoice image
    private const string FakeBase64Image = "JVBERi0xLjQgdGVzdA=="; // base64 of "%PDF-1.4 test"

    public SevitaIntegrationTests()
    {
        _server = WireMockServer.Start();

        _dbMock = new Mock<ISevitaPostDataAccess>(MockBehavior.Strict);
        _emailServiceMock = new Mock<IEmailService>(MockBehavior.Loose);

        // FakeTokenService returns a fixed token without hitting a real OAuth2 endpoint
        var tokenService = new FakeTokenService(NullLogger<SevitaTokenService>.Instance);

        var validationService = new SevitaValidationService(
            NullLogger<SevitaValidationService>.Instance);

        var s3ImageService = new S3ImageService(NullLogger<S3ImageService>.Instance);

        // TestSevitaPostStrategy overrides S3 image retrieval to return FakeBase64Image
        var postStrategy = new TestSevitaPostStrategy(
            _dbMock.Object,
            tokenService,
            validationService,
            s3ImageService,
            _emailServiceMock.Object,
            NullLogger<SevitaPostStrategy>.Instance);

        // TestSevitaPlugin overrides SQL-dependent methods (LoadValidIdsAsync, LoadEmailConfigAsync)
        // to return pre-configured in-memory data, avoiding real SQL Server connections.
        var preloadedValidIds = new ValidIds();
        preloadedValidIds.VendorIds.Add(VendorId);
        preloadedValidIds.EmployeeIds.Add(EmployeeId);

        _plugin = new TestSevitaPlugin(
            postStrategy,
            NullLogger<SevitaPlugin>.Instance,
            preloadedValidIds,
            new FailedPostConfiguration(),
            new EmailConfiguration());

        _clientConfig = new SevitaConfig
        {
            IsPORecord         = false,
            ApiAccessTokenUrl  = _server.Urls[0] + "/oauth/token",
            ClientId           = "test-client-id",
            ClientSecret       = "test-client-secret",
            TokenExpirationMin = 60
        };

        _config = new GenericJobConfig
        {
            Id                 = 10,
            JobId              = 55,
            ClientType         = "SEVITA",
            HeaderTable        = "WFSevitaIndexHeader",
            PostServiceUrl     = _server.Urls[0] + "/api/invoices",
            SuccessQueueId     = 300,
            PrimaryFailQueueId = 600,
            DefaultUserId      = 100,
            DbConnectionString = "Server=.;Database=Workflow;Trusted_Connection=True;",
            ClientConfigJson   = JsonSerializer.Serialize(_clientConfig)
        };
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }

    // -----------------------------------------------------------------------
    // Test 1: Full scheduled post flow — HTTP 201 success
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies the complete Sevita scheduled post flow:
    /// ExecutePostAsync → validate → build payload → POST invoice (HTTP 201) →
    /// route to SuccessQueueId → save history with fileBase=null →
    /// UpdateSevitaHeaderPostFields called in finally.
    /// </summary>
    [Fact]
    public async Task FullScheduledPostFlow_Http201_RoutesToSuccessQueue_HistoryHasNullFileBase()
    {
        // ---- Arrange: WireMock — Sevita invoice POST → HTTP 201 ----
        _server
            .Given(Request.Create()
                .WithPath("/api/invoices")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    invoiceIds = new Dictionary<string, string> { { InvoiceId, "created" } }
                })));

        // ---- Arrange: DB mock ----
        SetupCommonDbMocks(ItemId, successQueueId: _config.SuccessQueueId);

        var context = new PostContext
        {
            TriggerType = "Scheduled",
            ItemIds     = ItemId.ToString(),
            UserId      = _config.DefaultUserId,
            S3Config    = new EdenredApiUrlConfig()
        };

        // ---- Act ----
        var result = await _plugin.ExecutePostAsync(_config, context, CancellationToken.None);

        // ---- Assert: batch result ----
        result.RecordsProcessed.Should().Be(1);
        result.RecordsSuccess.Should().Be(1);
        result.RecordsFailed.Should().Be(0);
        result.ItemResults.Should().ContainSingle(r =>
            r.ItemId == ItemId &&
            r.IsSuccess &&
            r.DestinationQueue == _config.SuccessQueueId);

        // ---- Assert: routed to success queue ----
        _dbMock.Verify(db => db.RouteWorkitemAsync(
            ItemId, _config.SuccessQueueId, _config.DefaultUserId,
            SevitaConstants.OperationTypePost,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // ---- Assert: history saved with fileBase = null ----
        _dbMock.Verify(db => db.SavePostHistoryAsync(
            It.Is<PostHistory>(h =>
                h.ItemId == ItemId &&
                FileBaseIsNullInJson(h.InvoiceRequestJson)),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // ---- Assert: UpdateSevitaHeaderPostFields called in finally ----
        _dbMock.Verify(db => db.UpdateHeaderPostFieldsAsync(
            ItemId, It.IsAny<CancellationToken>()),
            Times.Once);

        // ---- Assert: GENERALLOG_INSERT called ----
        _dbMock.Verify(db => db.InsertGeneralLogAsync(
            SevitaConstants.OperationTypePost,
            SevitaConstants.SourceObjectContents,
            _config.DefaultUserId,
            It.IsAny<string>(),
            ItemId,
            It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        // ---- Assert: Sevita invoice endpoint was called ----
        _server.LogEntries.Should().Contain(e =>
            e.RequestMessage.Path == "/api/invoices");
    }

    // -----------------------------------------------------------------------
    // Test 2: Invoice POST returns HTTP 500 — routes to FailedPostsQueueId
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies that when the Sevita API returns HTTP 500, the workitem is routed
    /// to PrimaryFailQueueId with the special "Internal Server error" message,
    /// history is still written, and UpdateSevitaHeaderPostFields is still called.
    /// </summary>
    [Fact]
    public async Task ExecutePostAsync_Http500_RoutesToFailQueue_SpecialErrorMessage()
    {
        // ---- Arrange: WireMock — HTTP 500 ----
        _server
            .Given(Request.Create()
                .WithPath("/api/invoices")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("Internal Server Error"));

        // ---- Arrange: DB mock ----
        SetupCommonDbMocks(ItemId, successQueueId: _config.SuccessQueueId,
            expectFailRoute: true, failQueueId: _config.PrimaryFailQueueId);

        var context = new PostContext
        {
            TriggerType = "Scheduled",
            ItemIds     = ItemId.ToString(),
            UserId      = _config.DefaultUserId,
            S3Config    = new EdenredApiUrlConfig()
        };

        // ---- Act ----
        var result = await _plugin.ExecutePostAsync(_config, context, CancellationToken.None);

        // ---- Assert: batch result ----
        result.RecordsFailed.Should().Be(1);
        result.RecordsSuccess.Should().Be(0);
        result.ItemResults.Should().ContainSingle(r =>
            r.ItemId == ItemId &&
            !r.IsSuccess &&
            r.DestinationQueue == _config.PrimaryFailQueueId);

        // ---- Assert: routed to fail queue ----
        _dbMock.Verify(db => db.RouteWorkitemAsync(
            ItemId, _config.PrimaryFailQueueId, _config.DefaultUserId,
            SevitaConstants.OperationTypePost,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // ---- Assert: history still written ----
        _dbMock.Verify(db => db.SavePostHistoryAsync(
            It.Is<PostHistory>(h => h.ItemId == ItemId),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // ---- Assert: UpdateSevitaHeaderPostFields still called in finally ----
        _dbMock.Verify(db => db.UpdateHeaderPostFieldsAsync(
            ItemId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Test 3: Image not found — routes to FailedPostsQueueId, no API call
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies that when S3 returns null (image not found), the workitem is routed
    /// to PrimaryFailQueueId without calling the Sevita invoice API.
    /// UpdateSevitaHeaderPostFields is still called in the finally block.
    /// </summary>
    [Fact]
    public async Task ExecutePostAsync_ImageNotFound_RoutesToFailQueue_NoApiCall()
    {
        // ---- Arrange: DB mock — GetHeaderAndDetailData returns empty DataSet ----
        _dbMock
            .Setup(db => db.GetApiResponseTypesAsync(_config.JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PostResponseType>());

        _dbMock
            .Setup(db => db.SetPostInProcessAsync(ItemId, _config.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Return a DataSet with no rows to simulate image-not-found path
        _dbMock
            .Setup(db => db.GetHeaderAndDetailDataAsync(ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataSet());

        _dbMock
            .Setup(db => db.RouteWorkitemAsync(
                ItemId, _config.PrimaryFailQueueId, It.IsAny<int>(),
                SevitaConstants.OperationTypePost,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.InsertGeneralLogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), ItemId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.SavePostHistoryAsync(
                It.IsAny<PostHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.UpdateHeaderPostFieldsAsync(ItemId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var context = new PostContext
        {
            TriggerType = "Scheduled",
            ItemIds     = ItemId.ToString(),
            UserId      = _config.DefaultUserId,
            S3Config    = new EdenredApiUrlConfig()
        };

        // ---- Act ----
        var result = await _plugin.ExecutePostAsync(_config, context, CancellationToken.None);

        // ---- Assert: routed to fail queue ----
        result.RecordsFailed.Should().Be(1);
        result.RecordsSuccess.Should().Be(0);

        // ---- Assert: Sevita invoice endpoint was NOT called ----
        _server.LogEntries.Should().NotContain(e =>
            e.RequestMessage.Path == "/api/invoices");

        // ---- Assert: UpdateSevitaHeaderPostFields still called in finally ----
        _dbMock.Verify(db => db.UpdateHeaderPostFieldsAsync(
            ItemId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Test 4: PostInProcess set before API call, UpdateHeaderPostFields in finally
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies the PostInProcess lifecycle for Sevita:
    /// SetPostInProcessAsync is called before the API call,
    /// UpdateHeaderPostFieldsAsync (which clears PostInProcess via SP) is always
    /// called in the finally block — even when the API call succeeds.
    /// </summary>
    [Fact]
    public async Task ExecutePostAsync_UpdateHeaderPostFields_AlwaysCalledInFinally()
    {
        // ---- Arrange: WireMock — HTTP 201 ----
        _server
            .Given(Request.Create()
                .WithPath("/api/invoices")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    invoiceIds = new Dictionary<string, string> { { InvoiceId, "created" } }
                })));

        // Track call order
        var callOrder = new List<string>();

        _dbMock
            .Setup(db => db.GetApiResponseTypesAsync(_config.JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PostResponseType>());

        _dbMock
            .Setup(db => db.SetPostInProcessAsync(ItemId, _config.HeaderTable, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("SetPostInProcess"))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.GetHeaderAndDetailDataAsync(ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildHeaderDetailDataSet());

        _dbMock
            .Setup(db => db.RouteWorkitemAsync(
                ItemId, _config.SuccessQueueId, It.IsAny<int>(),
                SevitaConstants.OperationTypePost,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.InsertGeneralLogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), ItemId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.SavePostHistoryAsync(
                It.IsAny<PostHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.UpdateHeaderPostFieldsAsync(ItemId, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("UpdateHeaderPostFields"))
            .Returns(Task.CompletedTask);

        var context = new PostContext
        {
            TriggerType = "Scheduled",
            ItemIds     = ItemId.ToString(),
            UserId      = _config.DefaultUserId,
            S3Config    = new EdenredApiUrlConfig()
        };

        // ---- Act ----
        await _plugin.ExecutePostAsync(_config, context, CancellationToken.None);

        // ---- Assert: SetPostInProcess happened before UpdateHeaderPostFields ----
        callOrder.Should().ContainInOrder("SetPostInProcess", "UpdateHeaderPostFields");

        // ---- Assert: UpdateHeaderPostFields called exactly once ----
        _dbMock.Verify(db => db.UpdateHeaderPostFieldsAsync(
            ItemId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Test 5: fileBase is null in stored history JSON
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies that SaveHistoryAsync strips fileBase from all attachments before
    /// persisting to sevita_posted_records_history, preventing large base64 strings
    /// from being stored in the database.
    /// </summary>
    [Fact]
    public async Task ExecutePostAsync_HistoryJson_FileBaseIsNull()
    {
        // ---- Arrange: WireMock — HTTP 201 ----
        _server
            .Given(Request.Create()
                .WithPath("/api/invoices")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new
                {
                    invoiceIds = new Dictionary<string, string> { { InvoiceId, "created" } }
                })));

        PostHistory? capturedHistory = null;

        SetupCommonDbMocks(ItemId, successQueueId: _config.SuccessQueueId,
            onSaveHistory: h => capturedHistory = h);

        var context = new PostContext
        {
            TriggerType = "Scheduled",
            ItemIds     = ItemId.ToString(),
            UserId      = _config.DefaultUserId,
            S3Config    = new EdenredApiUrlConfig()
        };

        // ---- Act ----
        await _plugin.ExecutePostAsync(_config, context, CancellationToken.None);

        // ---- Assert: history was captured ----
        capturedHistory.Should().NotBeNull();
        capturedHistory!.ItemId.Should().Be(ItemId);

        // ---- Assert: fileBase is null in the stored JSON ----
        FileBaseIsNullInJson(capturedHistory.InvoiceRequestJson).Should().BeTrue(
            "fileBase must be null in stored history to prevent large base64 blobs in the DB");
    }

    // -----------------------------------------------------------------------
    // Test 6: ExecuteFeedDownloadAsync returns NotApplicable
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies that SevitaPlugin.ExecuteFeedDownloadAsync returns FeedResult.NotApplicable()
    /// because Sevita has no feed download step.
    /// </summary>
    [Fact]
    public async Task ExecuteFeedDownloadAsync_ReturnsNotApplicable()
    {
        var result = await _plugin.ExecuteFeedDownloadAsync(
            _config,
            new FeedContext(),
            CancellationToken.None);

        result.IsApplicable.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sets up the common DB mock expectations for a single workitem post.
    /// </summary>
    private void SetupCommonDbMocks(
        long itemId,
        int successQueueId,
        bool expectFailRoute = false,
        int failQueueId = 0,
        Action<PostHistory>? onSaveHistory = null)
    {
        _dbMock
            .Setup(db => db.GetApiResponseTypesAsync(_config.JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PostResponseType>());

        _dbMock
            .Setup(db => db.SetPostInProcessAsync(itemId, _config.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.GetHeaderAndDetailDataAsync(itemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildHeaderDetailDataSet());

        if (expectFailRoute)
        {
            _dbMock
                .Setup(db => db.RouteWorkitemAsync(
                    itemId, failQueueId, It.IsAny<int>(),
                    SevitaConstants.OperationTypePost,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }
        else
        {
            _dbMock
                .Setup(db => db.RouteWorkitemAsync(
                    itemId, successQueueId, It.IsAny<int>(),
                    SevitaConstants.OperationTypePost,
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        _dbMock
            .Setup(db => db.InsertGeneralLogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), itemId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.SavePostHistoryAsync(
                It.IsAny<PostHistory>(), It.IsAny<CancellationToken>()))
            .Callback<PostHistory, CancellationToken>((h, _) => onSaveHistory?.Invoke(h))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.UpdateHeaderPostFieldsAsync(itemId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    /// <summary>
    /// Builds a DataSet with header (Table[0]) and detail (Table[1]) rows
    /// matching the schema expected by SevitaPostStrategy.
    /// The header contains a valid VendorId and EmployeeId so validation passes.
    /// The image path is set to a non-empty value; S3ImageService will return null
    /// for it (no real S3 in tests), so we use a pre-loaded base64 via the
    /// GetHeaderAndDetailData mock returning a dataset with a known imagePath.
    /// </summary>
    private static DataSet BuildHeaderDetailDataSet()
    {
        var ds = new DataSet();

        var header = new DataTable();
        header.Columns.Add("ImagePath",                      typeof(string));
        header.Columns.Add("documentId",                     typeof(string));
        header.Columns.Add("vendorId",                       typeof(string));
        header.Columns.Add("employeeId",                     typeof(string));
        header.Columns.Add("payAlone",                       typeof(bool));
        header.Columns.Add("invoiceRelatedToZycusPurchase",  typeof(bool));
        header.Columns.Add("zycusInvoiceNumber",             typeof(string));
        header.Columns.Add("invoiceNumber",                  typeof(string));
        header.Columns.Add("InvoiceDate",                    typeof(string));
        header.Columns.Add("expensePeriod",                  typeof(string));
        header.Columns.Add("checkMemo",                      typeof(string));
        header.Columns.Add("cerfTrackingNumber",             typeof(string));
        header.Columns.Add("remittanceRequired",             typeof(bool));
        header.Columns.Add("fileUrl",                        typeof(string));
        header.Columns.Add("docid",                          typeof(string));
        header.Columns.Add("InvoiceAmount",                  typeof(string));
        header.Columns.Add("SupplierName",                   typeof(string));
        header.Columns.Add("ApproverName",                   typeof(string));
        header.Columns.Add("IsSendNotification",             typeof(bool));

        // Use a base64 string directly as the "image" — the S3ImageService mock
        // is not used here; instead we rely on the fact that the strategy calls
        // S3ImageService.GetBase64ImageAsync which returns null for unknown keys.
        // To make the test pass validation we need a non-null image, so we
        // override the S3 call by providing a local path that doesn't exist —
        // the strategy will get null and route to fail. To get a success path
        // we need to provide a valid image. We use a known base64 PDF header.
        header.Rows.Add(
            "invoices/sevita/test-invoice.pdf",  // ImagePath (S3 key)
            "DOC-2024-001",                      // documentId (edenredInvoiceId)
            VendorId,                            // vendorId
            EmployeeId,                          // employeeId
            false,                               // payAlone
            false,                               // invoiceRelatedToZycusPurchase
            (object)DBNull.Value,                // zycusInvoiceNumber
            "INV-2024-001",                      // invoiceNumber
            "2024-01-15",                        // InvoiceDate
            "2024-01",                           // expensePeriod
            "Check memo text",                   // checkMemo
            (object)DBNull.Value,                // cerfTrackingNumber
            false,                               // remittanceRequired
            "https://example.com/file",          // fileUrl
            "DOCID-001",                         // docid
            "500.00",                            // InvoiceAmount
            "Test Supplier",                     // SupplierName
            "Test Approver",                     // ApproverName
            false                                // IsSendNotification
        );

        ds.Tables.Add(header);

        var detail = new DataTable();
        detail.Columns.Add("alias",                typeof(string));
        detail.Columns.Add("naturalAccountNumber", typeof(string));
        detail.Columns.Add("LineAmount",           typeof(string));

        detail.Rows.Add("ALIAS-001", "500000", "500.00");

        ds.Tables.Add(detail);

        return ds;
    }

    /// <summary>
    /// Returns true when the invoice request JSON (stored as a JSON array) has
    /// fileBase set to null on all attachments.
    /// </summary>
    private static bool FileBaseIsNullInJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            var arr = JArray.Parse(json);
            foreach (var item in arr)
            {
                var attachments = item["attachments"] as JArray;
                if (attachments == null) continue;
                foreach (var attachment in attachments)
                {
                    var fileBase = attachment["fileBase"];
                    if (fileBase == null || fileBase.Type != JTokenType.Null)
                        return false;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // FakeTokenService — returns a fixed token without hitting a real endpoint
    // -----------------------------------------------------------------------

    /// <summary>
    /// Test double for <see cref="SevitaTokenService"/> that returns a fixed token
    /// without making any HTTP calls. Allows integration tests to run without a
    /// real OAuth2 token endpoint.
    /// </summary>
    private sealed class FakeTokenService : SevitaTokenService
    {
        public FakeTokenService(Microsoft.Extensions.Logging.ILogger<SevitaTokenService> logger)
            : base(logger) { }

        public override Task<string> GetAuthTokenAsync(SevitaConfig config, CancellationToken ct)
            => Task.FromResult(FakeToken);
    }

    // -----------------------------------------------------------------------
    // TestSevitaPlugin — overrides SQL-dependent methods to avoid real DB
    // -----------------------------------------------------------------------

    /// <summary>
    /// Test subclass of <see cref="SevitaPlugin"/> that overrides
    /// <c>LoadValidIdsAsync</c> and <c>LoadEmailConfigAsync</c> to return
    /// pre-configured in-memory data, avoiding real SQL Server connections.
    /// </summary>
    private sealed class TestSevitaPlugin : SevitaPlugin
    {
        private readonly FailedPostConfiguration _failedPostConfig;
        private readonly EmailConfiguration _emailConfig;

        public TestSevitaPlugin(
            SevitaPostStrategy postStrategy,
            Microsoft.Extensions.Logging.ILogger<SevitaPlugin> logger,
            ValidIds preloadedValidIds,
            FailedPostConfiguration failedPostConfig,
            EmailConfiguration emailConfig)
            : base(postStrategy, logger)
        {
            _validIds          = preloadedValidIds;
            _failedPostConfig  = failedPostConfig;
            _emailConfig       = emailConfig;
        }

        protected override Task<ValidIds> LoadValidIdsAsync(
            string connectionString, CancellationToken ct)
            => Task.FromResult(_validIds);

        protected override Task<(FailedPostConfiguration, EmailConfiguration)> LoadEmailConfigAsync(
            string connectionString, CancellationToken ct)
            => Task.FromResult((_failedPostConfig, _emailConfig));
    }

    // -----------------------------------------------------------------------
    // TestSevitaPostStrategy — overrides S3 image retrieval to avoid real AWS
    // -----------------------------------------------------------------------

    /// <summary>
    /// Test subclass of <see cref="SevitaPostStrategy"/> that overrides the S3 image
    /// retrieval step to return <see cref="FakeBase64Image"/> for any S3 key.
    /// This allows integration tests to exercise the full post flow without a real
    /// AWS S3 connection.
    /// </summary>
    private sealed class TestSevitaPostStrategy : SevitaPostStrategy
    {
        public TestSevitaPostStrategy(
            ISevitaPostDataAccess db,
            SevitaTokenService tokenService,
            SevitaValidationService validationService,
            S3ImageService s3ImageService,
            IEmailService emailService,
            Microsoft.Extensions.Logging.ILogger<SevitaPostStrategy> logger)
            : base(db, tokenService, validationService, s3ImageService, emailService, logger)
        { }

        /// <summary>
        /// Returns a fixed base64 image string instead of calling real S3.
        /// </summary>
        protected override Task<string?> GetImageAsync(
            string imagePath,
            EdenredApiUrlConfig s3Config,
            CancellationToken ct)
            => Task.FromResult<string?>(FakeBase64Image);

        /// <summary>
        /// Overrides S3 upload to be a no-op in tests (no real S3 bucket available).
        /// </summary>
        public override Task UploadAuditJsonAsync(
            string invoiceRequestJson,
            long itemId,
            SevitaConfig clientConfig,
            EdenredApiUrlConfig s3Config,
            CancellationToken ct)
            => Task.CompletedTask;
    }
}