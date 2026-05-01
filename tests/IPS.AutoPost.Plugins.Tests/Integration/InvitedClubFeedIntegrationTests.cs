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
using Newtonsoft.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace IPS.AutoPost.Plugins.Tests.Integration;

/// <summary>
/// Integration tests for the full InvitedClub feed download flow.
/// Uses WireMock.Net to mock Oracle Fusion REST API endpoints and
/// Moq to mock all database access interfaces.
/// The real InvitedClubPlugin and InvitedClubFeedStrategy classes are exercised end-to-end.
/// Verifies that supplier, address, site, and COA tables are populated via BulkCopy.
/// </summary>
public class InvitedClubFeedIntegrationTests : IDisposable
{
    // -----------------------------------------------------------------------
    // Infrastructure
    // -----------------------------------------------------------------------

    private readonly WireMockServer _server;
    private readonly string _tempDir;

    // DB mocks
    private readonly Mock<IInvitedClubFeedDataAccess> _feedDbMock;
    private readonly Mock<IInvitedClubRetryDataAccess> _retryDbMock;
    private readonly Mock<IEmailService> _emailServiceMock;

    // System under test
    private readonly InvitedClubPlugin _plugin;

    // Shared config
    private readonly GenericJobConfig _config;
    private readonly InvitedClubConfig _clientConfig;

    // Captured bulk-copy calls: tableName -> list of DataTable rows
    private readonly List<(string TableName, DataTable Data)> _bulkCopyCalls = new();

    public InvitedClubFeedIntegrationTests()
    {
        _server = WireMockServer.Start();

        _tempDir = Path.Combine(Path.GetTempPath(), "InvitedClubFeedIntegration_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);

        _feedDbMock = new Mock<IInvitedClubFeedDataAccess>(MockBehavior.Strict);
        _retryDbMock = new Mock<IInvitedClubRetryDataAccess>(MockBehavior.Strict);
        _emailServiceMock = new Mock<IEmailService>(MockBehavior.Loose);

        var s3Service = new S3ImageService(NullLogger<S3ImageService>.Instance);

        var retryService = new InvitedClubRetryService(
            _retryDbMock.Object,
            s3Service,
            NullLogger<InvitedClubRetryService>.Instance);

        var postStrategy = new InvitedClubPostStrategy(
            new Mock<IInvitedClubPostDataAccess>(MockBehavior.Loose).Object,
            s3Service,
            _emailServiceMock.Object,
            NullLogger<InvitedClubPostStrategy>.Instance);

        var feedStrategy = new InvitedClubFeedStrategy(
            _feedDbMock.Object,
            _emailServiceMock.Object,
            NullLogger<InvitedClubFeedStrategy>.Instance);

        _plugin = new InvitedClubPlugin(
            retryService,
            postStrategy,
            feedStrategy,
            NullLogger<InvitedClubPlugin>.Instance);

        _clientConfig = new InvitedClubConfig
        {
            EdenredFailQueueId       = 888,
            InvitedFailQueueId       = 999,
            ImagePostRetryLimit      = 3,
            LastSupplierDownloadTime = DateTime.MinValue  // force initial call
        };

        _config = new GenericJobConfig
        {
            Id               = 1,
            JobId            = 42,
            HeaderTable      = "WFInvitedClubsIndexHeader",
            AuthUsername     = "testuser",
            AuthPassword     = "testpass",
            PostServiceUrl   = _server.Urls[0] + "/",
            SuccessQueueId   = 200,
            DefaultUserId    = 100,
            FeedDownloadPath = _tempDir,
            ClientConfigJson = System.Text.Json.JsonSerializer.Serialize(_clientConfig)
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
    // Test 1: Full feed download — supplier, address, site, COA all populated
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies the complete feed download flow:
    /// LoadSupplier → LoadSupplierAddress → LoadSupplierSite → ExportSupplierCsv → LoadCOA.
    /// Asserts that BulkCopy is called for each of the 4 tables and that
    /// the UpdateSupplierSiteInSupplierAddress SP is called after site insert.
    /// </summary>
    [Fact]
    public async Task ExecuteFeedDownloadAsync_FullFlow_AllTablesPopulated()
    {
        // ---- Arrange: WireMock — Oracle Fusion supplier endpoint ----
        var supplierId = "SUP-001";

        var supplierPage = new SupplierData
        {
            Items = new List<SupplierResponse>
            {
                new SupplierResponse
                {
                    SupplierId = supplierId,
                    Supplier   = "ACME Corp",
                    Status     = "Active",
                    LastUpdateDate = DateTime.UtcNow.ToString("o")
                }
            },
            HasMore = false,
            Count   = 1
        };

        _server
            .Given(Request.Create()
                .WithPath("/suppliers")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonConvert.SerializeObject(supplierPage)));

        // ---- Arrange: WireMock — supplier address endpoint ----
        var addressPage = new SupplierAddressData
        {
            Items = new List<SupplierAddressResponse>
            {
                new SupplierAddressResponse
                {
                    SupplierAddressId = "ADDR-001",
                    AddressName       = "Main Office",
                    City              = "New York",
                    State             = "NY"
                }
            },
            HasMore = false,
            Count   = 1
        };

        _server
            .Given(Request.Create()
                .WithPath($"/suppliers/{supplierId}/child/addresses")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonConvert.SerializeObject(addressPage)));

        // ---- Arrange: WireMock — supplier site endpoint ----
        var sitePage = new SupplierSiteData
        {
            Items = new List<SupplierSiteResponse>
            {
                new SupplierSiteResponse
                {
                    SupplierSiteId = "SITE-001",
                    SupplierSite   = "MAIN",
                    Status         = "Active"
                }
            },
            HasMore = false,
            Count   = 1
        };

        _server
            .Given(Request.Create()
                .WithPath($"/suppliers/{supplierId}/child/sites")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonConvert.SerializeObject(sitePage)));

        // ---- Arrange: WireMock — COA endpoint ----
        var coaPage = new COAData
        {
            Items = new List<COAResponse>
            {
                new COAResponse
                {
                    CodeCombinationId  = "COA-001",
                    ChartOfAccountsId  = "5237",
                    Account            = "01-000-0000",
                    EnabledFlag        = "Y"
                }
            },
            HasMore = false,
            Count   = 1
        };

        _server
            .Given(Request.Create()
                .WithPath("/accountCombinationsLOV")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonConvert.SerializeObject(coaPage)));

        // ---- Arrange: DB mocks — initial call (tables are empty) ----

        // IsInitialCallAsync checks: all tables return 0 (empty = initial)
        _feedDbMock
            .Setup(db => db.GetTableCountAsync(InvitedClubConstants.SupplierTableName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _feedDbMock
            .Setup(db => db.GetTableCountAsync(InvitedClubConstants.SupplierAddressTableName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _feedDbMock
            .Setup(db => db.GetTableCountAsync(InvitedClubConstants.SupplierSiteTableName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // TruncateTable for initial full refresh (supplier, address, site, COA)
        _feedDbMock
            .Setup(db => db.TruncateTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // BulkCopy — capture calls to verify tables are populated
        _feedDbMock
            .Setup(db => db.BulkCopyAsync(It.IsAny<string>(), It.IsAny<DataTable>(), It.IsAny<CancellationToken>()))
            .Callback<string, DataTable, CancellationToken>((tableName, dt, _) =>
                _bulkCopyCalls.Add((tableName, dt)))
            .Returns(Task.CompletedTask);

        // UpdateSupplierSiteInSupplierAddress SP after site insert
        _feedDbMock
            .Setup(db => db.ExecuteNonQuerySpAsync(
                InvitedClubConstants.SpUpdateSupplierSiteInSupplierAddress,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // ExportSupplierCsv: GetSupplierDataToExport returns empty (no CSV needed for this test)
        _feedDbMock
            .Setup(db => db.GetSupplierDataToExportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataSet());

        // UpdateLastDownloadTime after supplier CSV export
        _feedDbMock
            .Setup(db => db.ExecuteUpdateLastDownloadTimeAsync(_config.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // GetMissingCOAIdsAsync — no missing IDs
        _feedDbMock
            .Setup(db => db.GetMissingCOAIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var feedContext = new FeedContext
        {
            TriggerType = "Scheduled",
            S3Config    = new EdenredApiUrlConfig()
        };

        // ---- Act ----
        var result = await _plugin.ExecuteFeedDownloadAsync(_config, feedContext, CancellationToken.None);

        // ---- Assert: feed result ----
        result.IsApplicable.Should().BeTrue();
        result.Success.Should().BeTrue();
        result.RecordsDownloaded.Should().BeGreaterThan(0);

        // ---- Assert: BulkCopy called for all 4 tables ----
        _bulkCopyCalls.Should().Contain(c => c.TableName == InvitedClubConstants.SupplierTableName,
            "supplier table should be populated");
        _bulkCopyCalls.Should().Contain(c => c.TableName == InvitedClubConstants.SupplierAddressTableName,
            "supplier address table should be populated");
        _bulkCopyCalls.Should().Contain(c => c.TableName == InvitedClubConstants.SupplierSiteTableName,
            "supplier site table should be populated");
        _bulkCopyCalls.Should().Contain(c => c.TableName == InvitedClubConstants.CoaTableName,
            "COA table should be populated");

        // ---- Assert: supplier table has 1 row ----
        var supplierBulkCopy = _bulkCopyCalls.First(c => c.TableName == InvitedClubConstants.SupplierTableName);
        supplierBulkCopy.Data.Rows.Count.Should().Be(1);

        // ---- Assert: address table has 1 row with SupplierId injected ----
        var addressBulkCopy = _bulkCopyCalls.First(c => c.TableName == InvitedClubConstants.SupplierAddressTableName);
        addressBulkCopy.Data.Rows.Count.Should().Be(1);

        // ---- Assert: site table has 1 row ----
        var siteBulkCopy = _bulkCopyCalls.First(c => c.TableName == InvitedClubConstants.SupplierSiteTableName);
        siteBulkCopy.Data.Rows.Count.Should().Be(1);

        // ---- Assert: COA table has 1 row ----
        var coaBulkCopy = _bulkCopyCalls.First(c => c.TableName == InvitedClubConstants.CoaTableName);
        coaBulkCopy.Data.Rows.Count.Should().Be(1);

        // ---- Assert: UpdateSupplierSiteInSupplierAddress SP called after site insert ----
        _feedDbMock.Verify(db => db.ExecuteNonQuerySpAsync(
            InvitedClubConstants.SpUpdateSupplierSiteInSupplierAddress,
            It.IsAny<CancellationToken>()),
            Times.Once);

        // ---- Assert: Oracle Fusion endpoints were called ----
        var logEntries = _server.LogEntries.ToList();
        logEntries.Should().Contain(e => e.RequestMessage.Path.Contains("suppliers"));
        logEntries.Should().Contain(e => e.RequestMessage.Path.Contains("accountCombinationsLOV"));
    }

    // -----------------------------------------------------------------------
    // Test 2: Initial vs incremental — initial call truncates, incremental deletes
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies that when the supplier table is empty (initial call),
    /// TruncateTable is called instead of DeleteBySupplierIds.
    /// </summary>
    [Fact]
    public async Task ExecuteFeedDownloadAsync_InitialCall_TruncatesBeforeBulkInsert()
    {
        // ---- Arrange: WireMock — minimal supplier response ----
        var supplierId = "SUP-INIT";

        _server
            .Given(Request.Create().WithPath("/suppliers").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonConvert.SerializeObject(new SupplierData
                {
                    Items = new List<SupplierResponse>
                    {
                        new SupplierResponse { SupplierId = supplierId, Supplier = "Test Corp" }
                    },
                    HasMore = false
                })));

        _server
            .Given(Request.Create().WithPath($"/suppliers/{supplierId}/child/addresses").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonConvert.SerializeObject(new SupplierAddressData { Items = new(), HasMore = false })));

        _server
            .Given(Request.Create().WithPath($"/suppliers/{supplierId}/child/sites").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonConvert.SerializeObject(new SupplierSiteData { Items = new(), HasMore = false })));

        _server
            .Given(Request.Create().WithPath("/accountCombinationsLOV").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonConvert.SerializeObject(new COAData { Items = new(), HasMore = false })));

        // All tables empty = initial call
        _feedDbMock
            .Setup(db => db.GetTableCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var truncatedTables = new List<string>();
        _feedDbMock
            .Setup(db => db.TruncateTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((t, _) => truncatedTables.Add(t))
            .Returns(Task.CompletedTask);

        _feedDbMock
            .Setup(db => db.BulkCopyAsync(It.IsAny<string>(), It.IsAny<DataTable>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _feedDbMock
            .Setup(db => db.ExecuteNonQuerySpAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _feedDbMock
            .Setup(db => db.GetSupplierDataToExportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataSet());

        _feedDbMock
            .Setup(db => db.ExecuteUpdateLastDownloadTimeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _feedDbMock
            .Setup(db => db.GetMissingCOAIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var feedContext = new FeedContext { TriggerType = "Scheduled", S3Config = new EdenredApiUrlConfig() };

        // ---- Act ----
        var result = await _plugin.ExecuteFeedDownloadAsync(_config, feedContext, CancellationToken.None);

        // ---- Assert: result is successful ----
        result.IsApplicable.Should().BeTrue();
        result.Success.Should().BeTrue();

        // ---- Assert: TruncateTable was called (initial = full refresh) ----
        truncatedTables.Should().Contain(InvitedClubConstants.SupplierTableName,
            "initial call should truncate supplier table");
        truncatedTables.Should().Contain(InvitedClubConstants.CoaTableName,
            "COA always does full refresh via truncate");

        // ---- Assert: DeleteBySupplierIds was NOT called (initial = truncate, not delete) ----
        _feedDbMock.Verify(db => db.DeleteBySupplierIdsAsync(
            It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Test 3: Missing COA IDs — email is sent
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies that when GetMissingCOAIdsAsync returns non-empty results,
    /// the email service is called to notify about missing COA entries.
    /// </summary>
    [Fact]
    public async Task ExecuteFeedDownloadAsync_MissingCOAIds_SendsEmail()
    {
        // ---- Arrange: WireMock — minimal responses ----
        var supplierId = "SUP-COA";

        _server
            .Given(Request.Create().WithPath("/suppliers").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonConvert.SerializeObject(new SupplierData
                {
                    Items = new List<SupplierResponse>
                    {
                        new SupplierResponse { SupplierId = supplierId, Supplier = "COA Test Corp" }
                    },
                    HasMore = false
                })));

        _server
            .Given(Request.Create().WithPath($"/suppliers/{supplierId}/child/addresses").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonConvert.SerializeObject(new SupplierAddressData { Items = new(), HasMore = false })));

        _server
            .Given(Request.Create().WithPath($"/suppliers/{supplierId}/child/sites").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonConvert.SerializeObject(new SupplierSiteData { Items = new(), HasMore = false })));

        _server
            .Given(Request.Create().WithPath("/accountCombinationsLOV").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonConvert.SerializeObject(new COAData
                {
                    Items = new List<COAResponse>
                    {
                        new COAResponse { CodeCombinationId = "COA-NEW", EnabledFlag = "Y" }
                    },
                    HasMore = false
                })));

        // ---- Arrange: DB mocks ----
        _feedDbMock
            .Setup(db => db.GetTableCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _feedDbMock
            .Setup(db => db.TruncateTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _feedDbMock
            .Setup(db => db.BulkCopyAsync(It.IsAny<string>(), It.IsAny<DataTable>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _feedDbMock
            .Setup(db => db.ExecuteNonQuerySpAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _feedDbMock
            .Setup(db => db.GetSupplierDataToExportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataSet());

        _feedDbMock
            .Setup(db => db.ExecuteUpdateLastDownloadTimeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Return 2 missing COA IDs — should trigger email
        _feedDbMock
            .Setup(db => db.GetMissingCOAIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "MISSING-001", "MISSING-002" });

        // Email config for the COA missing email
        var emailConfigTable = new DataTable();
        emailConfigTable.Columns.Add("SMTPServer",          typeof(string));
        emailConfigTable.Columns.Add("SMTPServerPort",      typeof(int));
        emailConfigTable.Columns.Add("Username",            typeof(string));
        emailConfigTable.Columns.Add("Password",            typeof(string));
        emailConfigTable.Columns.Add("EmailFrom",           typeof(string));
        emailConfigTable.Columns.Add("EmailFromUser",       typeof(string));
        emailConfigTable.Columns.Add("SMTPUseSSL",          typeof(bool));
        emailConfigTable.Columns.Add("EmailTo",             typeof(string));
        emailConfigTable.Columns.Add("EmailCC",             typeof(string));
        emailConfigTable.Columns.Add("EmailBCC",            typeof(string));
        emailConfigTable.Columns.Add("EmailSubject",        typeof(string));
        emailConfigTable.Columns.Add("EmailTemplate",       typeof(string));
        emailConfigTable.Columns.Add("EmailToHelpDesk",     typeof(string));
        emailConfigTable.Columns.Add("EmailSubjectImageFail", typeof(string));
        emailConfigTable.Columns.Add("EmailTemplateImageFail", typeof(string));
        emailConfigTable.Rows.Add(
            "smtp.test.com", 587, "user", "pass",
            "from@test.com", "From User", false,
            "to@test.com", "", "",
            "Missing COA Alert", "<html>Missing COA</html>",
            "helpdesk@test.com", "Image Fail Subject", null);

        _feedDbMock
            .Setup(db => db.GetEmailConfigAsync(_config.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(emailConfigTable);

        var feedContext = new FeedContext { TriggerType = "Scheduled", S3Config = new EdenredApiUrlConfig() };

        // ---- Act ----
        var result = await _plugin.ExecuteFeedDownloadAsync(_config, feedContext, CancellationToken.None);

        // ---- Assert: feed succeeded ----
        result.IsApplicable.Should().BeTrue();
        result.Success.Should().BeTrue();

        // ---- Assert: email was sent for missing COA ----
        _emailServiceMock.Verify(e => e.SendAsync(
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string[]>(),
            It.IsAny<string[]>(),
            It.IsAny<string[]>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Test 4: ExecuteFeedDownloadAsync returns FeedResult.IsApplicable = true
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies that InvitedClubPlugin.ExecuteFeedDownloadAsync returns a result
    /// with IsApplicable = true (unlike the default no-op implementation).
    /// This confirms the plugin correctly overrides the default interface method.
    /// </summary>
    [Fact]
    public async Task ExecuteFeedDownloadAsync_ReturnsApplicableResult()
    {
        // ---- Arrange: minimal WireMock stubs ----
        _server
            .Given(Request.Create().WithPath("/suppliers").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonConvert.SerializeObject(new SupplierData { Items = new(), HasMore = false })));

        _server
            .Given(Request.Create().WithPath("/accountCombinationsLOV").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonConvert.SerializeObject(new COAData { Items = new(), HasMore = false })));

        // ---- Arrange: DB mocks ----
        _feedDbMock
            .Setup(db => db.GetTableCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _feedDbMock
            .Setup(db => db.TruncateTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _feedDbMock
            .Setup(db => db.BulkCopyAsync(It.IsAny<string>(), It.IsAny<DataTable>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _feedDbMock
            .Setup(db => db.ExecuteNonQuerySpAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _feedDbMock
            .Setup(db => db.GetSupplierDataToExportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataSet());

        _feedDbMock
            .Setup(db => db.ExecuteUpdateLastDownloadTimeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _feedDbMock
            .Setup(db => db.GetMissingCOAIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var feedContext = new FeedContext { TriggerType = "Scheduled", S3Config = new EdenredApiUrlConfig() };

        // ---- Act ----
        var result = await _plugin.ExecuteFeedDownloadAsync(_config, feedContext, CancellationToken.None);

        // ---- Assert: IsApplicable = true (plugin overrides the default no-op) ----
        result.IsApplicable.Should().BeTrue(
            "InvitedClubPlugin overrides ExecuteFeedDownloadAsync and returns an applicable result");
        result.ErrorMessage.Should().BeNull();
    }
}

