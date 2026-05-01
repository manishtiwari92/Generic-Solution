using System.Data;
using System.Text.Json;
using FluentAssertions;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Core.Services;
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
/// Unit tests for <see cref="InvitedClubRetryService"/>.
/// Covers: no-records path, success path, failure path, and retry count increment.
/// Uses WireMock.Net to mock Oracle Fusion attachment API responses and
/// Moq to mock <see cref="IInvitedClubRetryDataAccess"/> for DB calls.
/// </summary>
public class InvitedClubRetryServiceTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly Mock<IInvitedClubRetryDataAccess> _dbMock;
    private readonly InvitedClubRetryService _service;
    private readonly GenericJobConfig _config;
    private readonly InvitedClubConfig _clientConfig;
    private readonly EdenredApiUrlConfig _s3Config;

    // Temp directory for legacy image tests
    private readonly string _tempDir;

    public InvitedClubRetryServiceTests()
    {
        _server = WireMockServer.Start();

        _dbMock = new Mock<IInvitedClubRetryDataAccess>(MockBehavior.Strict);

        // S3ImageService with a null logger — we mock the DB, not S3 directly,
        // but we need a real S3ImageService instance for the constructor.
        // For non-legacy tests we override RetryOneImageAsync via a subclass.
        var s3Logger = NullLogger<S3ImageService>.Instance;
        var s3Service = new S3ImageService(s3Logger);

        _service = new InvitedClubRetryService(
            _dbMock.Object,
            s3Service,
            NullLogger<InvitedClubRetryService>.Instance);

        _tempDir = Path.Combine(Path.GetTempPath(), "RetryServiceTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);

        _config = new GenericJobConfig
        {
            Id              = 1,
            JobId           = 42,
            HeaderTable     = "WFInvitedClubsIndexHeader",
            AuthUsername    = "testuser",
            AuthPassword    = "testpass",
            PostServiceUrl  = _server.Urls[0] + "/invoices/",
            SuccessQueueId  = 200,
            DefaultUserId   = 100,
            IsLegacyJob     = false,
            ImageParentPath = _tempDir + Path.DirectorySeparatorChar
        };

        _clientConfig = new InvitedClubConfig
        {
            ImagePostRetryLimit = 3,
            InvitedFailQueueId  = 999,
            EdenredFailQueueId  = 888
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
    // RetryPostImagesAsync — no records path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RetryPostImagesAsync_WhenNoFailedRecords_DoesNotCallRetryOneImage()
    {
        // Arrange: SP returns empty table
        _dbMock
            .Setup(db => db.GetFailedImagesDataAsync(
                _config.HeaderTable,
                _clientConfig.ImagePostRetryLimit,
                _clientConfig.InvitedFailQueueId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataTable());

        // Act
        await _service.RetryPostImagesAsync(_config, _clientConfig, _s3Config, CancellationToken.None);

        // Assert: no DB mutation calls were made
        _dbMock.Verify(db => db.IncrementImagePostRetryCountAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _dbMock.Verify(db => db.RouteWorkitemAsync(
            It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RetryPostImagesAsync_CallsSpWithCorrectParameters()
    {
        // Arrange
        _dbMock
            .Setup(db => db.GetFailedImagesDataAsync(
                "WFInvitedClubsIndexHeader",
                3,
                999,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataTable())
            .Verifiable();

        // Act
        await _service.RetryPostImagesAsync(_config, _clientConfig, _s3Config, CancellationToken.None);

        // Assert: SP was called with the exact parameters from config
        _dbMock.Verify(db => db.GetFailedImagesDataAsync(
            "WFInvitedClubsIndexHeader",
            3,
            999,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // RetryPostImagesAsync — success path (HTTP 201)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RetryPostImagesAsync_WhenAttachmentSucceeds_UpdatesDocumentIdAndRoutesToSuccess()
    {
        // Arrange: one failed image record
        var failedTable = BuildFailedImagesTable(new[]
        {
            new FailedImagesData
            {
                ItemId              = 1001,
                InvoiceId           = "INV-001",
                ImagePath           = "invoice_1001.pdf",
                ImagePostRetryCount = 1
            }
        });

        _dbMock
            .Setup(db => db.GetFailedImagesDataAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedTable);

        _dbMock
            .Setup(db => db.IncrementImagePostRetryCountAsync(1001, _config.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.UpdateAttachedDocumentIdAsync(1001, "DOC-999", _config.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.RouteWorkitemAsync(
                1001, 200, 100,
                InvitedClubConstants.OperationTypePost,
                It.Is<string>(s => s.StartsWith(InvitedClubConstants.RouteCommentAutomatic)),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Write a real local image file so the legacy path works
        // (we use legacy=true for this test to avoid real S3 calls)
        var legacyConfig = LegacyConfig();
        var imagePath = Path.Combine(_tempDir, "invoice_1001.pdf");
        await File.WriteAllBytesAsync(imagePath, new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF

        // Mock Oracle Fusion attachment endpoint — HTTP 201
        _server
            .Given(Request.Create()
                .WithPath("/invoices/INV-001/child/attachments")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new { AttachedDocumentId = "DOC-999" })));

        // Act
        await _service.RetryPostImagesAsync(legacyConfig, _clientConfig, _s3Config, CancellationToken.None);

        // Assert: retry count incremented
        _dbMock.Verify(db => db.IncrementImagePostRetryCountAsync(
            1001, legacyConfig.HeaderTable, It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: AttachedDocumentId updated
        _dbMock.Verify(db => db.UpdateAttachedDocumentIdAsync(
            1001, "DOC-999", legacyConfig.HeaderTable, It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: routed to success queue
        _dbMock.Verify(db => db.RouteWorkitemAsync(
            1001, 200, 100,
            InvitedClubConstants.OperationTypePost,
            It.Is<string>(s => s.StartsWith(InvitedClubConstants.RouteCommentAutomatic)),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // RetryPostImagesAsync — failure path (non-201 response)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RetryPostImagesAsync_WhenAttachmentFails_IncrementsRetryCountButDoesNotRoute()
    {
        // Arrange: one failed image record
        var failedTable = BuildFailedImagesTable(new[]
        {
            new FailedImagesData
            {
                ItemId              = 2002,
                InvoiceId           = "INV-002",
                ImagePath           = "invoice_2002.pdf",
                ImagePostRetryCount = 2
            }
        });

        _dbMock
            .Setup(db => db.GetFailedImagesDataAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedTable);

        _dbMock
            .Setup(db => db.IncrementImagePostRetryCountAsync(2002, _config.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Write a real local image file
        var legacyConfig = LegacyConfig();
        var imagePath = Path.Combine(_tempDir, "invoice_2002.pdf");
        await File.WriteAllBytesAsync(imagePath, new byte[] { 0x25, 0x50, 0x44, 0x46 });

        // Mock Oracle Fusion attachment endpoint — HTTP 500 (failure)
        _server
            .Given(Request.Create()
                .WithPath("/invoices/INV-002/child/attachments")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("Internal Server Error"));

        // Act
        await _service.RetryPostImagesAsync(legacyConfig, _clientConfig, _s3Config, CancellationToken.None);

        // Assert: retry count was incremented
        _dbMock.Verify(db => db.IncrementImagePostRetryCountAsync(
            2002, legacyConfig.HeaderTable, It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: NOT routed (attachment failed)
        _dbMock.Verify(db => db.RouteWorkitemAsync(
            It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Assert: AttachedDocumentId NOT updated
        _dbMock.Verify(db => db.UpdateAttachedDocumentIdAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Retry count increment — always incremented
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RetryOneImageAsync_AlwaysIncrementsRetryCount_EvenWhenImageNotFound()
    {
        // Arrange: legacy job with a missing image file (file does NOT exist)
        var legacyConfig = LegacyConfig();

        var record = new FailedImagesData
        {
            ItemId              = 3003,
            InvoiceId           = "INV-003",
            ImagePath           = "missing_image.pdf",   // file does not exist
            ImagePostRetryCount = 0
        };

        _dbMock
            .Setup(db => db.IncrementImagePostRetryCountAsync(3003, legacyConfig.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _service.RetryOneImageAsync(legacyConfig, _clientConfig, _s3Config, record, CancellationToken.None);

        // Assert: retry count incremented even though image was not found
        _dbMock.Verify(db => db.IncrementImagePostRetryCountAsync(
            3003, legacyConfig.HeaderTable, It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: no routing or document ID update
        _dbMock.Verify(db => db.RouteWorkitemAsync(
            It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RetryOneImageAsync_AlwaysIncrementsRetryCount_OnSuccessfulPost()
    {
        // Arrange: legacy job with a real image file
        var legacyConfig = LegacyConfig();

        var record = new FailedImagesData
        {
            ItemId              = 4004,
            InvoiceId           = "INV-004",
            ImagePath           = "invoice_4004.pdf",
            ImagePostRetryCount = 0
        };

        var imagePath = Path.Combine(_tempDir, "invoice_4004.pdf");
        await File.WriteAllBytesAsync(imagePath, new byte[] { 0x25, 0x50, 0x44, 0x46 });

        _dbMock
            .Setup(db => db.IncrementImagePostRetryCountAsync(4004, legacyConfig.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.UpdateAttachedDocumentIdAsync(4004, It.IsAny<string>(), legacyConfig.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.RouteWorkitemAsync(
                4004, legacyConfig.SuccessQueueId, legacyConfig.DefaultUserId,
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _server
            .Given(Request.Create()
                .WithPath("/invoices/INV-004/child/attachments")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new { AttachedDocumentId = "DOC-4004" })));

        // Act
        await _service.RetryOneImageAsync(legacyConfig, _clientConfig, _s3Config, record, CancellationToken.None);

        // Assert: retry count incremented exactly once
        _dbMock.Verify(db => db.IncrementImagePostRetryCountAsync(
            4004, legacyConfig.HeaderTable, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Multiple records — each processed independently
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RetryPostImagesAsync_WithMultipleRecords_ProcessesEachIndependently()
    {
        // Arrange: two records — one succeeds, one fails
        var failedTable = BuildFailedImagesTable(new[]
        {
            new FailedImagesData { ItemId = 5001, InvoiceId = "INV-5001", ImagePath = "img_5001.pdf", ImagePostRetryCount = 0 },
            new FailedImagesData { ItemId = 5002, InvoiceId = "INV-5002", ImagePath = "img_5002.pdf", ImagePostRetryCount = 1 }
        });

        _dbMock
            .Setup(db => db.GetFailedImagesDataAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedTable);

        // Both images exist on disk
        var legacyConfig = LegacyConfig();
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "img_5001.pdf"), new byte[] { 0x25, 0x50, 0x44, 0x46 });
        await File.WriteAllBytesAsync(Path.Combine(_tempDir, "img_5002.pdf"), new byte[] { 0x25, 0x50, 0x44, 0x46 });

        // INV-5001 → HTTP 201 (success)
        _server
            .Given(Request.Create().WithPath("/invoices/INV-5001/child/attachments").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new { AttachedDocumentId = "DOC-5001" })));

        // INV-5002 → HTTP 400 (failure)
        _server
            .Given(Request.Create().WithPath("/invoices/INV-5002/child/attachments").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(400)
                .WithBody("Bad Request"));

        // DB setup for both records
        _dbMock.Setup(db => db.IncrementImagePostRetryCountAsync(5001, legacyConfig.HeaderTable, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _dbMock.Setup(db => db.IncrementImagePostRetryCountAsync(5002, legacyConfig.HeaderTable, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _dbMock.Setup(db => db.UpdateAttachedDocumentIdAsync(5001, "DOC-5001", legacyConfig.HeaderTable, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _dbMock.Setup(db => db.RouteWorkitemAsync(5001, 200, 100, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        await _service.RetryPostImagesAsync(legacyConfig, _clientConfig, _s3Config, CancellationToken.None);

        // Assert: both retry counts incremented
        _dbMock.Verify(db => db.IncrementImagePostRetryCountAsync(5001, legacyConfig.HeaderTable, It.IsAny<CancellationToken>()), Times.Once);
        _dbMock.Verify(db => db.IncrementImagePostRetryCountAsync(5002, legacyConfig.HeaderTable, It.IsAny<CancellationToken>()), Times.Once);

        // Assert: only 5001 was routed to success
        _dbMock.Verify(db => db.RouteWorkitemAsync(5001, 200, 100, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _dbMock.Verify(db => db.RouteWorkitemAsync(5002, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // Assert: only 5001 had AttachedDocumentId updated
        _dbMock.Verify(db => db.UpdateAttachedDocumentIdAsync(5001, "DOC-5001", legacyConfig.HeaderTable, It.IsAny<CancellationToken>()), Times.Once);
        _dbMock.Verify(db => db.UpdateAttachedDocumentIdAsync(5002, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Routing uses DefaultUserId and "Automatic Route:" prefix
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RetryOneImageAsync_OnSuccess_UsesDefaultUserIdAndAutomaticRouteComment()
    {
        // Arrange
        var legacyConfig = LegacyConfig(defaultUserId: 777, successQueueId: 555);

        var record = new FailedImagesData
        {
            ItemId              = 6006,
            InvoiceId           = "INV-6006",
            ImagePath           = "invoice_6006.pdf",
            ImagePostRetryCount = 0
        };

        var imagePath = Path.Combine(_tempDir, "invoice_6006.pdf");
        await File.WriteAllBytesAsync(imagePath, new byte[] { 0x25, 0x50, 0x44, 0x46 });

        _dbMock
            .Setup(db => db.IncrementImagePostRetryCountAsync(6006, legacyConfig.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.UpdateAttachedDocumentIdAsync(6006, It.IsAny<string>(), legacyConfig.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        string? capturedComment = null;
        int capturedUserId = 0;
        int capturedQueueId = 0;

        _dbMock
            .Setup(db => db.RouteWorkitemAsync(
                6006, It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<long, int, int, string, string, CancellationToken>(
                (_, queueId, userId, _, comment, _) =>
                {
                    capturedQueueId = queueId;
                    capturedUserId = userId;
                    capturedComment = comment;
                })
            .Returns(Task.CompletedTask);

        _server
            .Given(Request.Create().WithPath("/invoices/INV-6006/child/attachments").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new { AttachedDocumentId = "DOC-6006" })));

        // Act
        await _service.RetryOneImageAsync(legacyConfig, _clientConfig, _s3Config, record, CancellationToken.None);

        // Assert: correct userId and queue
        capturedUserId.Should().Be(777);
        capturedQueueId.Should().Be(555);

        // Assert: comment starts with "Automatic Route:"
        capturedComment.Should().StartWith(InvitedClubConstants.RouteCommentAutomatic);
    }

    // -----------------------------------------------------------------------
    // Content-Type header verification
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RetryOneImageAsync_PostsAttachmentWithCorrectContentType()
    {
        // Arrange
        var legacyConfig = LegacyConfig();

        var record = new FailedImagesData
        {
            ItemId              = 7007,
            InvoiceId           = "INV-7007",
            ImagePath           = "invoice_7007.pdf",
            ImagePostRetryCount = 0
        };

        var imagePath = Path.Combine(_tempDir, "invoice_7007.pdf");
        await File.WriteAllBytesAsync(imagePath, new byte[] { 0x25, 0x50, 0x44, 0x46 });

        _dbMock
            .Setup(db => db.IncrementImagePostRetryCountAsync(7007, legacyConfig.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.UpdateAttachedDocumentIdAsync(7007, It.IsAny<string>(), legacyConfig.HeaderTable, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _dbMock
            .Setup(db => db.RouteWorkitemAsync(
                7007, It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // WireMock: verify Content-Type header is the Oracle ADF resource item type
        _server
            .Given(Request.Create()
                .WithPath("/invoices/INV-7007/child/attachments")
                .UsingPost()
                .WithHeader("Content-Type", InvitedClubConstants.ContentTypeAdfResourceItem, WireMock.Matchers.MatchBehaviour.AcceptOnMatch))
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new { AttachedDocumentId = "DOC-7007" })));

        // Act
        await _service.RetryOneImageAsync(legacyConfig, _clientConfig, _s3Config, record, CancellationToken.None);

        // Assert: the WireMock server received exactly one request matching the Content-Type
        var logEntries = _server.LogEntries.ToList();
        logEntries.Should().ContainSingle(e =>
            e.RequestMessage.Path == "/invoices/INV-7007/child/attachments");
    }

    // -----------------------------------------------------------------------
    // Helper methods
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a copy of the base config with IsLegacyJob set to true.
    /// </summary>
    private GenericJobConfig LegacyConfig(int? defaultUserId = null, int? successQueueId = null)
    {
        return new GenericJobConfig
        {
            Id              = _config.Id,
            JobId           = _config.JobId,
            HeaderTable     = _config.HeaderTable,
            AuthUsername    = _config.AuthUsername,
            AuthPassword    = _config.AuthPassword,
            PostServiceUrl  = _config.PostServiceUrl,
            SuccessQueueId  = successQueueId ?? _config.SuccessQueueId,
            DefaultUserId   = defaultUserId ?? _config.DefaultUserId,
            IsLegacyJob     = true,
            ImageParentPath = _config.ImageParentPath
        };
    }

    /// <summary>
    /// Builds a DataTable matching the schema returned by
    /// <c>InvitedClub_GetFailedImagesData</c> for use in mock setups.
    /// </summary>
    private static DataTable BuildFailedImagesTable(IEnumerable<FailedImagesData> records)
    {
        var dt = new DataTable();
        dt.Columns.Add("InvoiceId",           typeof(string));
        dt.Columns.Add("ItemId",              typeof(long));
        dt.Columns.Add("ImagePostRetryCount", typeof(int));
        dt.Columns.Add("ImagePath",           typeof(string));

        foreach (var r in records)
        {
            dt.Rows.Add(r.InvoiceId, r.ItemId, r.ImagePostRetryCount, r.ImagePath);
        }

        return dt;
    }
}
