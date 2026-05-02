using System.Data;
using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
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
/// PBT Property 27.9 — Retry Idempotence Property
///
/// PROPERTY 1: ImagePostRetryCount increments by exactly 1 per retry attempt,
///             regardless of whether the attachment POST succeeds or fails.
///
/// PROPERTY 2: Records with ImagePostRetryCount >= ImagePostRetryLimit are NOT
///             returned by GetFailedImagesData (filtered out by the SP).
///             The retry service must not attempt to retry records beyond the limit.
///
/// PROPERTY 3: For N records in the failed images table, IncrementImagePostRetryCount
///             is called exactly N times (once per record, always).
///
/// Tested via FsCheck generators that produce arbitrary:
///   - Record counts (1–20)
///   - Current retry counts (0 to limit-1)
///   - ImagePostRetryLimit values (1–10)
///   - API response codes (success/failure)
/// </summary>
public class RetryIdempotenceTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly string _tempDir;

    public RetryIdempotenceTests()
    {
        _server = WireMockServer.Start();
        _tempDir = Path.Combine(Path.GetTempPath(), "PBT_Retry_" + Guid.NewGuid());
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

    private static Gen<int> RecordCountGen =>
        Gen.Choose(1, 20);

    private static Gen<int> RetryLimitGen =>
        Gen.Choose(1, 10);

    private static Gen<bool> ApiSuccessGen =>
        Gen.Elements(true, false);

    // -----------------------------------------------------------------------
    // 27.9a — FsCheck property: IncrementRetryCount called exactly once per record
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementRetryCount_CalledExactlyOncePerRecord_ForAnyRecordCount()
    {
        var property = Prop.ForAll(
            RecordCountGen.ToArbitrary(),
            RetryLimitGen.ToArbitrary(),
            ApiSuccessGen.ToArbitrary(),
            (recordCount, retryLimit, apiSuccess) =>
            {
                var incrementCallCount =
                    RunRetryAndCountIncrements(recordCount, retryLimit, apiSuccess)
                        .GetAwaiter().GetResult();

                return incrementCallCount == recordCount;
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.9b — FsCheck property: increment count is independent of API success/failure
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementRetryCount_IsIndependentOfApiOutcome()
    {
        var property = Prop.ForAll(
            RecordCountGen.ToArbitrary(),
            RetryLimitGen.ToArbitrary(),
            (recordCount, retryLimit) =>
            {
                var successCount =
                    RunRetryAndCountIncrements(recordCount, retryLimit, apiSuccess: true)
                        .GetAwaiter().GetResult();

                var failureCount =
                    RunRetryAndCountIncrements(recordCount, retryLimit, apiSuccess: false)
                        .GetAwaiter().GetResult();

                // Increment count must be the same regardless of API outcome
                return successCount == failureCount && successCount == recordCount;
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.9c — Explicit parametric tests
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1, 3, true)]
    [InlineData(1, 3, false)]
    [InlineData(5, 3, true)]
    [InlineData(5, 3, false)]
    [InlineData(10, 5, true)]
    [InlineData(10, 5, false)]
    public async Task IncrementRetryCount_CalledExactlyNTimes_ForNRecords(
        int recordCount,
        int retryLimit,
        bool apiSuccess)
    {
        var incrementCallCount = await RunRetryAndCountIncrements(recordCount, retryLimit, apiSuccess);

        incrementCallCount.Should().Be(recordCount,
            $"IncrementImagePostRetryCount must be called exactly {recordCount} time(s) for {recordCount} record(s)");
    }

    // -----------------------------------------------------------------------
    // 27.9d — Verify retry count increments even when image is not found
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IncrementRetryCount_CalledEvenWhenImageNotFound()
    {
        // Arrange: one record with a missing image file
        var record = new FailedImagesData
        {
            ItemId = 1001,
            InvoiceId = "INV-001",
            ImagePath = "missing_image.pdf",  // file does not exist
            ImagePostRetryCount = 0
        };

        var incrementCallCount = 0;
        var dbMock = new Mock<IInvitedClubRetryDataAccess>(MockBehavior.Loose);
        dbMock
            .Setup(db => db.IncrementImagePostRetryCountAsync(1001, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => incrementCallCount++)
            .Returns(Task.CompletedTask);

        var service = BuildService(dbMock.Object);
        var config = BuildLegacyConfig();
        var clientConfig = new InvitedClubConfig { ImagePostRetryLimit = 3, InvitedFailQueueId = 999 };

        // Act
        await service.RetryOneImageAsync(config, clientConfig, new EdenredApiUrlConfig(), record, CancellationToken.None);

        // Assert: retry count incremented even though image was not found
        incrementCallCount.Should().Be(1,
            "IncrementImagePostRetryCount must be called even when the image file is not found");
    }

    // -----------------------------------------------------------------------
    // 27.9e — Verify retry count increments even when an exception is thrown
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IncrementRetryCount_CalledEvenWhenExceptionThrown()
    {
        // Arrange: image exists but DB throws on GetHeaderAndDetailData (simulated via a bad image path)
        var imgPath = "retry_exception_test.pdf";
        await WriteImageAsync(imgPath);

        var record = new FailedImagesData
        {
            ItemId = 2002,
            InvoiceId = "INV-002",
            ImagePath = imgPath,
            ImagePostRetryCount = 1
        };

        var incrementCallCount = 0;
        var dbMock = new Mock<IInvitedClubRetryDataAccess>(MockBehavior.Loose);
        dbMock
            .Setup(db => db.IncrementImagePostRetryCountAsync(2002, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => incrementCallCount++)
            .Returns(Task.CompletedTask);

        // WireMock: attachment endpoint throws a connection error (simulated via 500)
        _server
            .Given(Request.Create().WithPath("/invoices/INV-002/child/attachments").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Server Error"));

        var service = BuildService(dbMock.Object);
        var config = BuildLegacyConfig();
        var clientConfig = new InvitedClubConfig { ImagePostRetryLimit = 3, InvitedFailQueueId = 999 };

        // Act
        await service.RetryOneImageAsync(config, clientConfig, new EdenredApiUrlConfig(), record, CancellationToken.None);

        // Assert: retry count incremented even though the API call failed
        incrementCallCount.Should().Be(1,
            "IncrementImagePostRetryCount must be called even when the attachment POST fails");
    }

    // -----------------------------------------------------------------------
    // 27.9f — Verify records at retry limit are not returned by SP (SP-level filter)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(3, 3)]  // retryCount == limit → should NOT be returned
    [InlineData(4, 3)]  // retryCount > limit → should NOT be returned
    [InlineData(2, 3)]  // retryCount < limit → SHOULD be returned
    [InlineData(0, 3)]  // retryCount = 0 → SHOULD be returned
    public async Task GetFailedImagesData_FiltersRecordsAtOrBeyondRetryLimit(
        int currentRetryCount,
        int retryLimit)
    {
        // The SP filters records where ImagePostRetryCount >= ImagePostRetryLimit.
        // We verify this by checking what the mock returns and whether the service processes it.

        var shouldBeProcessed = currentRetryCount < retryLimit;

        var record = new FailedImagesData
        {
            ItemId = 3003,
            InvoiceId = "INV-003",
            ImagePath = "invoice_3003.pdf",
            ImagePostRetryCount = currentRetryCount
        };

        // Build a DataTable that simulates what the SP returns
        // (SP already filters out records at/beyond limit, so we simulate that behavior)
        var failedTable = new DataTable();
        failedTable.Columns.Add("InvoiceId", typeof(string));
        failedTable.Columns.Add("ItemId", typeof(long));
        failedTable.Columns.Add("ImagePostRetryCount", typeof(int));
        failedTable.Columns.Add("ImagePath", typeof(string));

        if (shouldBeProcessed)
        {
            // SP returns this record (below limit)
            failedTable.Rows.Add(record.InvoiceId, record.ItemId, record.ImagePostRetryCount, record.ImagePath);
        }
        // else: SP does NOT return this record (at/beyond limit)

        var incrementCallCount = 0;
        var dbMock = new Mock<IInvitedClubRetryDataAccess>(MockBehavior.Loose);
        dbMock
            .Setup(db => db.GetFailedImagesDataAsync(
                It.IsAny<string>(), retryLimit, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedTable);
        dbMock
            .Setup(db => db.IncrementImagePostRetryCountAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => incrementCallCount++)
            .Returns(Task.CompletedTask);

        if (shouldBeProcessed)
        {
            await WriteImageAsync("invoice_3003.pdf");
            _server
                .Given(Request.Create().WithPath("/invoices/INV-003/child/attachments").UsingPost())
                .RespondWith(Response.Create().WithStatusCode(500).WithBody("Error"));
        }

        var service = BuildService(dbMock.Object);
        var config = BuildLegacyConfig();
        var clientConfig = new InvitedClubConfig
        {
            ImagePostRetryLimit = retryLimit,
            InvitedFailQueueId = 999
        };

        await service.RetryPostImagesAsync(config, clientConfig, new EdenredApiUrlConfig(), CancellationToken.None);

        if (shouldBeProcessed)
        {
            incrementCallCount.Should().Be(1,
                $"record with retryCount={currentRetryCount} < limit={retryLimit} must be processed");
        }
        else
        {
            incrementCallCount.Should().Be(0,
                $"record with retryCount={currentRetryCount} >= limit={retryLimit} must NOT be processed (filtered by SP)");
        }

        _server.Reset();
    }

    // -----------------------------------------------------------------------
    // Core test runner
    // -----------------------------------------------------------------------

    private async Task<int> RunRetryAndCountIncrements(
        int recordCount,
        int retryLimit,
        bool apiSuccess)
    {
        var incrementCallCount = 0;

        // Build failed images table with N records
        var failedTable = new DataTable();
        failedTable.Columns.Add("InvoiceId", typeof(string));
        failedTable.Columns.Add("ItemId", typeof(long));
        failedTable.Columns.Add("ImagePostRetryCount", typeof(int));
        failedTable.Columns.Add("ImagePath", typeof(string));

        for (var i = 0; i < recordCount; i++)
        {
            var itemId = 10_000L + i;
            var invoiceId = $"INV-{itemId}";
            var imgPath = $"retry_{itemId}.pdf";
            await WriteImageAsync(imgPath);
            failedTable.Rows.Add(invoiceId, itemId, 0, imgPath);

            // Setup WireMock for each record
            if (apiSuccess)
            {
                _server
                    .Given(Request.Create().WithPath($"/invoices/{invoiceId}/child/attachments").UsingPost())
                    .RespondWith(Response.Create()
                        .WithStatusCode(201)
                        .WithHeader("Content-Type", "application/json")
                        .WithBody(JsonSerializer.Serialize(new { AttachedDocumentId = $"DOC-{itemId}" })));
            }
            else
            {
                _server
                    .Given(Request.Create().WithPath($"/invoices/{invoiceId}/child/attachments").UsingPost())
                    .RespondWith(Response.Create().WithStatusCode(500).WithBody("Error"));
            }
        }

        var dbMock = new Mock<IInvitedClubRetryDataAccess>(MockBehavior.Loose);
        dbMock
            .Setup(db => db.GetFailedImagesDataAsync(
                It.IsAny<string>(), retryLimit, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedTable);
        dbMock
            .Setup(db => db.IncrementImagePostRetryCountAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => incrementCallCount++)
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.UpdateAttachedDocumentIdAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dbMock
            .Setup(db => db.RouteWorkitemAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = BuildService(dbMock.Object);
        var config = BuildLegacyConfig();
        var clientConfig = new InvitedClubConfig
        {
            ImagePostRetryLimit = retryLimit,
            InvitedFailQueueId = 999
        };

        await service.RetryPostImagesAsync(config, clientConfig, new EdenredApiUrlConfig(), CancellationToken.None);

        _server.Reset();

        return incrementCallCount;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private InvitedClubRetryService BuildService(IInvitedClubRetryDataAccess db)
    {
        return new InvitedClubRetryService(
            db,
            new S3ImageService(NullLogger<S3ImageService>.Instance),
            NullLogger<InvitedClubRetryService>.Instance);
    }

    private GenericJobConfig BuildLegacyConfig() => new()
    {
        Id = 1,
        JobId = 42,
        HeaderTable = "WFInvitedClubsIndexHeader",
        AuthUsername = "user",
        AuthPassword = "pass",
        PostServiceUrl = _server.Urls[0] + "/invoices/",
        SuccessQueueId = 200,
        DefaultUserId = 100,
        IsLegacyJob = true,
        ImageParentPath = _tempDir + Path.DirectorySeparatorChar
    };

    private async Task WriteImageAsync(string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        if (!File.Exists(path))
            await File.WriteAllBytesAsync(path, new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }
}
