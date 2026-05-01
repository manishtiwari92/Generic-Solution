using System.Data;
using System.Text.Json;
using FluentAssertions;
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

namespace IPS.AutoPost.Plugins.Tests.InvitedClub;

/// <summary>
/// Unit tests for <see cref="InvitedClubFeedStrategy"/>.
/// Uses WireMock.Net to mock Oracle Fusion REST API responses and
/// Moq to mock <see cref="IInvitedClubFeedDataAccess"/> for DB calls.
/// </summary>
public class InvitedClubFeedStrategyTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly Mock<IInvitedClubFeedDataAccess> _dbMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly InvitedClubFeedStrategy _strategy;
    private readonly GenericJobConfig _config;
    private readonly InvitedClubConfig _clientConfig;

    public InvitedClubFeedStrategyTests()
    {
        _server = WireMockServer.Start();

        _dbMock = new Mock<IInvitedClubFeedDataAccess>();
        _emailServiceMock = new Mock<IEmailService>();

        _strategy = new InvitedClubFeedStrategy(
            _dbMock.Object,
            _emailServiceMock.Object,
            NullLogger<InvitedClubFeedStrategy>.Instance);

        _config = new GenericJobConfig
        {
            Id = 1,
            AuthUsername = "testuser",
            AuthPassword = "testpass",
            PostServiceUrl = _server.Urls[0] + "/",
            FeedDownloadPath = Path.Combine(Path.GetTempPath(), "InvitedClubTests_" + Guid.NewGuid())
        };

        _clientConfig = new InvitedClubConfig
        {
            LastSupplierDownloadTime = DateTime.UtcNow.AddDays(-1)
        };
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();

        // Clean up temp directory if it was created
        if (Directory.Exists(_config.FeedDownloadPath))
            Directory.Delete(_config.FeedDownloadPath, recursive: true);
    }

    // -----------------------------------------------------------------------
    // 10.9 Tests for LoadSupplierAddressAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoadSupplierAddressAsync_InitialCall_FetchesAddressesForAllSupplierIds()
    {
        // Arrange: IsInitialCallAsync returns true (table is empty)
        _dbMock
            .Setup(db => db.GetTableCountAsync(InvitedClubConstants.SupplierAddressTableName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var suppliers = new List<SupplierResponse>
        {
            new() { SupplierId = "S001", LastUpdateDate = "2020-01-01" },
            new() { SupplierId = "S002", LastUpdateDate = "2020-01-01" }
        };

        // Mock address endpoint for S001
        _server
            .Given(Request.Create()
                .WithPath($"/suppliers/S001/child/addresses")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(BuildAddressPageJson("S001", hasMore: false)));

        // Mock address endpoint for S002
        _server
            .Given(Request.Create()
                .WithPath($"/suppliers/S002/child/addresses")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(BuildAddressPageJson("S002", hasMore: false)));

        // Act
        var result = await _strategy.LoadSupplierAddressAsync(
            _config, _clientConfig, suppliers, CancellationToken.None);

        // Assert: both suppliers were fetched (initial call uses all IDs)
        result.Should().HaveCount(2);
        result.Select(a => a.SupplierId).Should().BeEquivalentTo(new[] { "S001", "S002" });
    }

    [Fact]
    public async Task LoadSupplierAddressAsync_IncrementalCall_FiltersToRecentlyUpdatedSuppliers()
    {
        // Arrange: IsInitialCallAsync returns false (table has data)
        _dbMock
            .Setup(db => db.GetTableCountAsync(InvitedClubConstants.SupplierAddressTableName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        // LastSupplierDownloadTime = 5 days ago → cutoff = 7 days ago
        var lastDownload = DateTime.UtcNow.AddDays(-5);
        var cutoff = lastDownload.AddDays(-2);

        var recentDate = cutoff.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ss");   // within window
        var oldDate = cutoff.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss");     // outside window

        var suppliers = new List<SupplierResponse>
        {
            new() { SupplierId = "S001", LastUpdateDate = recentDate },  // should be fetched
            new() { SupplierId = "S002", LastUpdateDate = oldDate }      // should be skipped
        };

        var clientConfigWithDate = new InvitedClubConfig
        {
            LastSupplierDownloadTime = lastDownload
        };

        // Mock address endpoint for S001 only
        _server
            .Given(Request.Create()
                .WithPath($"/suppliers/S001/child/addresses")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(BuildAddressPageJson("S001", hasMore: false)));

        // Act
        var result = await _strategy.LoadSupplierAddressAsync(
            _config, clientConfigWithDate, suppliers, CancellationToken.None);

        // Assert: only S001 was fetched (S002 is too old)
        result.Should().HaveCount(1);
        result[0].SupplierId.Should().Be("S001");
    }

    [Fact]
    public async Task LoadSupplierAddressAsync_InjectsSupplierIdIntoEachItem()
    {
        // Arrange: initial call so all suppliers are fetched
        _dbMock
            .Setup(db => db.GetTableCountAsync(InvitedClubConstants.SupplierAddressTableName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var suppliers = new List<SupplierResponse>
        {
            new() { SupplierId = "S999", LastUpdateDate = "2024-01-01" }
        };

        // Return two address items — neither has SupplierId set in the JSON
        var addressJson = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new { SupplierAddressId = "A1", AddressName = "Main" },
                new { SupplierAddressId = "A2", AddressName = "Branch" }
            },
            count = 2,
            hasMore = false,
            limit = 500,
            offset = 0
        });

        _server
            .Given(Request.Create()
                .WithPath($"/suppliers/S999/child/addresses")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(addressJson));

        // Act
        var result = await _strategy.LoadSupplierAddressAsync(
            _config, _clientConfig, suppliers, CancellationToken.None);

        // Assert: SupplierId was injected into every item
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(a => a.SupplierId.Should().Be("S999"));
    }

    // -----------------------------------------------------------------------
    // 10.10 Tests for LoadCOAAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoadCOAAsync_WhenMissingCOAExists_SendsEmail()
    {
        // Arrange: COA API returns one page of data
        _server
            .Given(Request.Create()
                .WithPath("/accountCombinationsLOV")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(BuildCoaPageJson(hasMore: false, count: 1)));

        // DB: truncate and bulk copy succeed
        _dbMock
            .Setup(db => db.TruncateTableAsync(InvitedClubConstants.CoaTableName, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _dbMock
            .Setup(db => db.BulkCopyAsync(InvitedClubConstants.CoaTableName, It.IsAny<DataTable>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Missing COA IDs exist
        _dbMock
            .Setup(db => db.GetMissingCOAIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "12345", "67890" });

        // Email config
        _dbMock
            .Setup(db => db.GetEmailConfigAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildEmailConfigTable());

        _emailServiceMock
            .Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string[]>(), It.IsAny<string[]>(), It.IsAny<string[]>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var count = await _strategy.LoadCOAAsync(_config, CancellationToken.None);

        // Assert: email was sent because missing IDs exist
        _emailServiceMock.Verify(e => e.SendAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string[]>(), It.IsAny<string[]>(), It.IsAny<string[]>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        count.Should().Be(1);
    }

    [Fact]
    public async Task LoadCOAAsync_WhenNoMissingCOA_DoesNotSendEmail()
    {
        // Arrange: COA API returns one page of data
        _server
            .Given(Request.Create()
                .WithPath("/accountCombinationsLOV")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(BuildCoaPageJson(hasMore: false, count: 1)));

        // DB: truncate and bulk copy succeed
        _dbMock
            .Setup(db => db.TruncateTableAsync(InvitedClubConstants.CoaTableName, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _dbMock
            .Setup(db => db.BulkCopyAsync(InvitedClubConstants.CoaTableName, It.IsAny<DataTable>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // No missing COA IDs
        _dbMock
            .Setup(db => db.GetMissingCOAIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var count = await _strategy.LoadCOAAsync(_config, CancellationToken.None);

        // Assert: email was NOT sent
        _emailServiceMock.Verify(e => e.SendAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string[]>(), It.IsAny<string[]>(), It.IsAny<string[]>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        count.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Helper methods
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a JSON response body for the supplier address endpoint.
    /// The SupplierId field is intentionally omitted to simulate the real API.
    /// </summary>
    private static string BuildAddressPageJson(string supplierId, bool hasMore)
    {
        return JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    SupplierAddressId = $"ADDR_{supplierId}_1",
                    AddressName = "Test Address",
                    LastUpdateDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
                }
            },
            count = 1,
            hasMore,
            limit = 500,
            offset = 0
        });
    }

    /// <summary>
    /// Builds a JSON response body for the COA endpoint using Newtonsoft-compatible field names.
    /// </summary>
    private static string BuildCoaPageJson(bool hasMore, int count)
    {
        var items = Enumerable.Range(1, count).Select(i => new Dictionary<string, object>
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

        var payload = new Dictionary<string, object>
        {
            ["items"] = items,
            ["count"] = count,
            ["hasMore"] = hasMore,
            ["limit"] = 500,
            ["offset"] = 0
        };

        return Newtonsoft.Json.JsonConvert.SerializeObject(payload);
    }

    /// <summary>
    /// Builds a DataTable representing a single EmailConfig row for mocking.
    /// </summary>
    private static DataTable BuildEmailConfigTable()
    {
        var dt = new DataTable();
        dt.Columns.Add("SMTPServer", typeof(string));
        dt.Columns.Add("SMTPServerPort", typeof(int));
        dt.Columns.Add("Username", typeof(string));
        dt.Columns.Add("Password", typeof(string));
        dt.Columns.Add("EmailFrom", typeof(string));
        dt.Columns.Add("EmailFromUser", typeof(string));
        dt.Columns.Add("SMTPUseSSL", typeof(bool));
        dt.Columns.Add("EmailTo", typeof(string));
        dt.Columns.Add("EmailCC", typeof(string));
        dt.Columns.Add("EmailBCC", typeof(string));
        dt.Columns.Add("EmailSubject", typeof(string));
        dt.Columns.Add("EmailTemplate", typeof(string));
        dt.Columns.Add("EmailToHelpDesk", typeof(string));
        dt.Columns.Add("EmailSubjectImageFail", typeof(string));
        dt.Columns.Add("EmailTemplateImageFail", typeof(string));

        dt.Rows.Add(
            "smtp.test.com",
            25,
            "smtpuser",
            "smtppass",
            "noreply@test.com",
            "No Reply",
            false,
            "admin@test.com",
            "",
            "",
            "Missing COA Alert",
            "<html>Missing COA detected</html>",
            "",
            "",
            "");

        return dt;
    }
}
