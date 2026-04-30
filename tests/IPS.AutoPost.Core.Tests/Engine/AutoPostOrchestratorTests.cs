using System.Data;
using FluentAssertions;
using IPS.AutoPost.Core.Engine;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace IPS.AutoPost.Core.Tests.Engine;

/// <summary>
/// Unit tests for <see cref="AutoPostOrchestrator"/>.
/// Covers tasks 6.5 (RunScheduledPostAsync) and 6.6 (RunManualPostAsync).
/// </summary>
public class AutoPostOrchestratorTests
{
    // -----------------------------------------------------------------------
    // Test fixture helpers
    // -----------------------------------------------------------------------

    private readonly Mock<IConfigurationRepository> _configRepo = new();
    private readonly Mock<IWorkitemRepository> _workitemRepo = new();
    private readonly Mock<IRoutingRepository> _routingRepo = new();
    private readonly Mock<IAuditRepository> _auditRepo = new();
    private readonly Mock<IScheduleRepository> _scheduleRepo = new();
    private readonly Mock<IClientPlugin> _plugin = new();
    private readonly Mock<SchedulerService> _scheduler = new();
    private readonly PluginRegistry _registry = new();
    private readonly Mock<ILogger<AutoPostOrchestrator>> _logger = new();

    private AutoPostOrchestrator CreateOrchestrator()
    {
        // Only register the plugin mock if it has a non-null ClientType set
        if (_plugin.Object.ClientType != null)
            _registry.Register(_plugin.Object);

        return new AutoPostOrchestrator(
            _configRepo.Object,
            _workitemRepo.Object,
            _routingRepo.Object,
            _auditRepo.Object,
            _scheduleRepo.Object,
            _registry,
            _scheduler.Object,
            _logger.Object);
    }

    private static GenericJobConfig ActiveConfig(string clientType = "TESTCLIENT") => new()
    {
        Id = 1,
        JobId = 42,
        ClientType = clientType,
        IsActive = true,
        AllowAutoPost = true,
        DownloadFeed = true,
        DefaultUserId = 100,
        HeaderTable = "WFTestHeader",
        DbConnectionString = "Server=test;"
    };

    private static IReadOnlyList<ScheduleConfig> WindowOpenSchedules() =>
    [
        new ScheduleConfig
        {
            Id = 1,
            IsActive = true,
            ScheduleType = "POST",
            // Use a time that is always "now" by returning empty list — we mock ShouldExecute directly
            ExecutionTime = "00:00"
        }
    ];

    private void SetupAuditRepoNoOp()
    {
        _auditRepo
            .Setup(r => r.SaveExecutionHistoryAsync(
                It.IsAny<GenericExecutionHistory>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupS3Config()
    {
        _configRepo
            .Setup(r => r.GetEdenredApiUrlConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EdenredApiUrlConfig());
    }

    // -----------------------------------------------------------------------
    // 6.5 — RunScheduledPostAsync: configuration not found
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunScheduledPostAsync_ConfigNotFound_ReturnsEmptyResult()
    {
        // Arrange
        _configRepo
            .Setup(r => r.GetByJobIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenericJobConfig?)null);

        var sut = CreateOrchestrator();

        // Act
        var result = await sut.RunScheduledPostAsync(42, "TESTCLIENT", CancellationToken.None);

        // Assert
        result.RecordsProcessed.Should().Be(0);
        result.RecordsSuccess.Should().Be(0);
        _plugin.Verify(p => p.ExecutePostAsync(
            It.IsAny<GenericJobConfig>(),
            It.IsAny<PostContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunScheduledPostAsync_ConfigInactive_ReturnsEmptyResult()
    {
        // Arrange
        var config = ActiveConfig();
        config.IsActive = false;

        _configRepo
            .Setup(r => r.GetByJobIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var sut = CreateOrchestrator();

        // Act
        var result = await sut.RunScheduledPostAsync(42, "TESTCLIENT", CancellationToken.None);

        // Assert
        result.RecordsProcessed.Should().Be(0);
        _plugin.Verify(p => p.ExecutePostAsync(
            It.IsAny<GenericJobConfig>(),
            It.IsAny<PostContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // 6.5 — RunScheduledPostAsync: schedule window check
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunScheduledPostAsync_OutsideScheduleWindow_SkipsExecution()
    {
        // Arrange
        var config = ActiveConfig();
        _plugin.Setup(p => p.ClientType).Returns("TESTCLIENT");

        _configRepo
            .Setup(r => r.GetByJobIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        _scheduleRepo
            .Setup(r => r.GetSchedulesAsync(config.Id, config.JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WindowOpenSchedules());

        // ShouldExecute returns false → outside window
        _scheduler
            .Setup(s => s.ShouldExecute(It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<ScheduleConfig>>()))
            .Returns(false);

        var sut = CreateOrchestrator();

        // Act
        var result = await sut.RunScheduledPostAsync(42, "TESTCLIENT", CancellationToken.None);

        // Assert
        result.RecordsProcessed.Should().Be(0);
        _plugin.Verify(p => p.ExecutePostAsync(
            It.IsAny<GenericJobConfig>(),
            It.IsAny<PostContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunScheduledPostAsync_InsideScheduleWindow_ExecutesPlugin()
    {
        // Arrange
        var config = ActiveConfig();
        var expected = new PostBatchResult { RecordsProcessed = 3, RecordsSuccess = 3 };

        _configRepo
            .Setup(r => r.GetByJobIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        _scheduleRepo
            .Setup(r => r.GetSchedulesAsync(config.Id, config.JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WindowOpenSchedules());
        _scheduler
            .Setup(s => s.ShouldExecute(It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<ScheduleConfig>>()))
            .Returns(true);
        _plugin.Setup(p => p.ClientType).Returns("TESTCLIENT");
        _plugin
            .Setup(p => p.ExecutePostAsync(config, It.IsAny<PostContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        _configRepo
            .Setup(r => r.UpdateLastPostTimeAsync(config.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        SetupS3Config();
        SetupAuditRepoNoOp();

        var sut = CreateOrchestrator();

        // Act
        var result = await sut.RunScheduledPostAsync(42, "TESTCLIENT", CancellationToken.None);

        // Assert
        result.RecordsProcessed.Should().Be(3);
        result.RecordsSuccess.Should().Be(3);
        _plugin.Verify(p => p.ExecutePostAsync(
            config, It.IsAny<PostContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // 6.5 — RunScheduledPostAsync: AllowAutoPost=false
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunScheduledPostAsync_AllowAutoPostFalse_SkipsExecution()
    {
        // Arrange
        var config = ActiveConfig();
        config.AllowAutoPost = false;
        _plugin.Setup(p => p.ClientType).Returns("TESTCLIENT");

        _configRepo
            .Setup(r => r.GetByJobIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        _scheduleRepo
            .Setup(r => r.GetSchedulesAsync(config.Id, config.JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WindowOpenSchedules());
        _scheduler
            .Setup(s => s.ShouldExecute(It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<ScheduleConfig>>()))
            .Returns(true);  // window is open, but AllowAutoPost=false should still skip

        var sut = CreateOrchestrator();

        // Act
        var result = await sut.RunScheduledPostAsync(42, "TESTCLIENT", CancellationToken.None);

        // Assert
        result.RecordsProcessed.Should().Be(0);
        _plugin.Verify(p => p.ExecutePostAsync(
            It.IsAny<GenericJobConfig>(),
            It.IsAny<PostContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // 6.5 — RunScheduledPostAsync: UpdateLastPostTime called after success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunScheduledPostAsync_OnSuccess_UpdatesLastPostTime()
    {
        // Arrange
        var config = ActiveConfig();

        _configRepo
            .Setup(r => r.GetByJobIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        _scheduleRepo
            .Setup(r => r.GetSchedulesAsync(config.Id, config.JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WindowOpenSchedules());
        _scheduler
            .Setup(s => s.ShouldExecute(It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<ScheduleConfig>>()))
            .Returns(true);
        _plugin.Setup(p => p.ClientType).Returns("TESTCLIENT");
        _plugin
            .Setup(p => p.ExecutePostAsync(config, It.IsAny<PostContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PostBatchResult { RecordsProcessed = 1, RecordsSuccess = 1 });
        _configRepo
            .Setup(r => r.UpdateLastPostTimeAsync(config.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        SetupS3Config();
        SetupAuditRepoNoOp();

        var sut = CreateOrchestrator();

        // Act
        await sut.RunScheduledPostAsync(42, "TESTCLIENT", CancellationToken.None);

        // Assert
        _configRepo.Verify(r => r.UpdateLastPostTimeAsync(config.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // 6.5 — RunScheduledPostAsync: OnBeforePostAsync called before plugin
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunScheduledPostAsync_CallsOnBeforePostAsync_BeforePlugin()
    {
        // Arrange
        var config = ActiveConfig();
        var callOrder = new List<string>();

        _configRepo
            .Setup(r => r.GetByJobIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        _scheduleRepo
            .Setup(r => r.GetSchedulesAsync(config.Id, config.JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WindowOpenSchedules());
        _scheduler
            .Setup(s => s.ShouldExecute(It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<ScheduleConfig>>()))
            .Returns(true);
        _plugin.Setup(p => p.ClientType).Returns("TESTCLIENT");
        _plugin
            .Setup(p => p.OnBeforePostAsync(config, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("OnBefore"))
            .Returns(Task.CompletedTask);
        _plugin
            .Setup(p => p.ExecutePostAsync(config, It.IsAny<PostContext>(), It.IsAny<CancellationToken>()))
            .Callback<GenericJobConfig, PostContext, CancellationToken>((_, _, _) => callOrder.Add("Execute"))
            .ReturnsAsync(new PostBatchResult());
        _configRepo
            .Setup(r => r.UpdateLastPostTimeAsync(config.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        SetupS3Config();
        SetupAuditRepoNoOp();

        var sut = CreateOrchestrator();

        // Act
        await sut.RunScheduledPostAsync(42, "TESTCLIENT", CancellationToken.None);

        // Assert
        callOrder.Should().ContainInOrder("OnBefore", "Execute");
    }

    // -----------------------------------------------------------------------
    // 6.5 — RunScheduledPostAsync: execution history written in finally
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunScheduledPostAsync_PluginThrows_ExecutionHistoryStillWritten()
    {
        // Arrange
        var config = ActiveConfig();

        _configRepo
            .Setup(r => r.GetByJobIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        _scheduleRepo
            .Setup(r => r.GetSchedulesAsync(config.Id, config.JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WindowOpenSchedules());
        _scheduler
            .Setup(s => s.ShouldExecute(It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<ScheduleConfig>>()))
            .Returns(true);
        _plugin.Setup(p => p.ClientType).Returns("TESTCLIENT");
        _plugin
            .Setup(p => p.ExecutePostAsync(config, It.IsAny<PostContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ERP API unreachable"));
        _configRepo
            .Setup(r => r.UpdateLastPostTimeAsync(config.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        SetupS3Config();
        SetupAuditRepoNoOp();

        var sut = CreateOrchestrator();

        // Act
        var result = await sut.RunScheduledPostAsync(42, "TESTCLIENT", CancellationToken.None);

        // Assert — execution history must be written even when plugin throws
        _auditRepo.Verify(r => r.SaveExecutionHistoryAsync(
            It.Is<GenericExecutionHistory>(h => h.Status == "FAILED"),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        result.ErrorMessage.Should().Contain("ERP API unreachable");
    }

    // -----------------------------------------------------------------------
    // 6.5 — RunScheduledPostAsync: TriggerType set to "Scheduled" in context
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunScheduledPostAsync_SetsScheduledTriggerTypeInContext()
    {
        // Arrange
        var config = ActiveConfig();
        PostContext? capturedContext = null;

        _configRepo
            .Setup(r => r.GetByJobIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        _scheduleRepo
            .Setup(r => r.GetSchedulesAsync(config.Id, config.JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WindowOpenSchedules());
        _scheduler
            .Setup(s => s.ShouldExecute(It.IsAny<DateTime>(), It.IsAny<IReadOnlyList<ScheduleConfig>>()))
            .Returns(true);
        _plugin.Setup(p => p.ClientType).Returns("TESTCLIENT");
        _plugin
            .Setup(p => p.ExecutePostAsync(config, It.IsAny<PostContext>(), It.IsAny<CancellationToken>()))
            .Callback<GenericJobConfig, PostContext, CancellationToken>((_, ctx, _) => capturedContext = ctx)
            .ReturnsAsync(new PostBatchResult());
        _configRepo
            .Setup(r => r.UpdateLastPostTimeAsync(config.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        SetupS3Config();
        SetupAuditRepoNoOp();

        var sut = CreateOrchestrator();

        // Act
        await sut.RunScheduledPostAsync(42, "TESTCLIENT", CancellationToken.None);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.TriggerType.Should().Be("Scheduled");
        capturedContext.ItemIds.Should().BeEmpty();
        capturedContext.UserId.Should().Be(config.DefaultUserId);
    }

    // -----------------------------------------------------------------------
    // 6.6 — RunManualPostAsync: no workitems found
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunManualPostAsync_NoWorkitemsFound_ReturnsErrorResult()
    {
        // Arrange
        var emptyDs = new DataSet();
        emptyDs.Tables.Add(new DataTable());

        _workitemRepo
            .Setup(r => r.GetWorkitemsByItemIdsAsync("999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyDs);

        var sut = CreateOrchestrator();

        // Act
        var result = await sut.RunManualPostAsync("999", 1, CancellationToken.None);

        // Assert
        result.ErrorMessage.Should().Be("No workitems found.");
        _plugin.Verify(p => p.ExecutePostAsync(
            It.IsAny<GenericJobConfig>(),
            It.IsAny<PostContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunManualPostAsync_NullDataSet_ReturnsErrorResult()
    {
        // Arrange
        _workitemRepo
            .Setup(r => r.GetWorkitemsByItemIdsAsync("999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataSet?)null!);

        var sut = CreateOrchestrator();

        // Act
        var result = await sut.RunManualPostAsync("999", 1, CancellationToken.None);

        // Assert
        result.ErrorMessage.Should().Be("No workitems found.");
    }

    // -----------------------------------------------------------------------
    // 6.6 — RunManualPostAsync: StatusId resolution → Missing Configuration
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunManualPostAsync_NoConfigForStatusId_ReturnsMissingConfiguration()
    {
        // Arrange — workitem exists but no config matches its StatusId
        var ds = BuildWorkitemDataSet(statusId: 777);

        _workitemRepo
            .Setup(r => r.GetWorkitemsByItemIdsAsync("101", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ds);
        _configRepo
            .Setup(r => r.GetBySourceQueueIdAsync(777, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenericJobConfig?)null);

        var sut = CreateOrchestrator();

        // Act
        var result = await sut.RunManualPostAsync("101", 1, CancellationToken.None);

        // Assert
        result.ResponseCode.Should().Be(-1);
        result.ErrorMessage.Should().Be("Missing Configuration.");
        _plugin.Verify(p => p.ExecutePostAsync(
            It.IsAny<GenericJobConfig>(),
            It.IsAny<PostContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // 6.6 — RunManualPostAsync: happy path — config resolved, plugin called
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunManualPostAsync_ValidWorkitems_ResolvesConfigAndCallsPlugin()
    {
        // Arrange
        var config = ActiveConfig();
        var ds = BuildWorkitemDataSet(statusId: 101);
        var expected = new PostBatchResult { RecordsProcessed = 1, RecordsSuccess = 1 };

        _workitemRepo
            .Setup(r => r.GetWorkitemsByItemIdsAsync("500", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ds);
        _configRepo
            .Setup(r => r.GetBySourceQueueIdAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        _plugin.Setup(p => p.ClientType).Returns("TESTCLIENT");
        _plugin
            .Setup(p => p.ExecutePostAsync(config, It.IsAny<PostContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        SetupS3Config();
        SetupAuditRepoNoOp();

        var sut = CreateOrchestrator();

        // Act
        var result = await sut.RunManualPostAsync("500", 99, CancellationToken.None);

        // Assert
        result.RecordsProcessed.Should().Be(1);
        result.RecordsSuccess.Should().Be(1);
        _plugin.Verify(p => p.ExecutePostAsync(
            config, It.IsAny<PostContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // 6.6 — RunManualPostAsync: context has correct TriggerType and ItemIds
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunManualPostAsync_SetsManualTriggerTypeAndItemIdsInContext()
    {
        // Arrange
        var config = ActiveConfig();
        var ds = BuildWorkitemDataSet(statusId: 101);
        PostContext? capturedContext = null;

        _workitemRepo
            .Setup(r => r.GetWorkitemsByItemIdsAsync("101,102", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ds);
        _configRepo
            .Setup(r => r.GetBySourceQueueIdAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        _plugin.Setup(p => p.ClientType).Returns("TESTCLIENT");
        _plugin
            .Setup(p => p.ExecutePostAsync(config, It.IsAny<PostContext>(), It.IsAny<CancellationToken>()))
            .Callback<GenericJobConfig, PostContext, CancellationToken>((_, ctx, _) => capturedContext = ctx)
            .ReturnsAsync(new PostBatchResult());
        SetupS3Config();
        SetupAuditRepoNoOp();

        var sut = CreateOrchestrator();

        // Act
        await sut.RunManualPostAsync("101,102", 55, CancellationToken.None);

        // Assert
        capturedContext.Should().NotBeNull();
        capturedContext!.TriggerType.Should().Be("Manual");
        capturedContext.ItemIds.Should().Be("101,102");
        capturedContext.UserId.Should().Be(55);
        capturedContext.ProcessManually.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // 6.6 — RunManualPostAsync: OnBeforePostAsync called
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunManualPostAsync_CallsOnBeforePostAsync()
    {
        // Arrange
        var config = ActiveConfig();
        var ds = BuildWorkitemDataSet(statusId: 101);

        _workitemRepo
            .Setup(r => r.GetWorkitemsByItemIdsAsync("200", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ds);
        _configRepo
            .Setup(r => r.GetBySourceQueueIdAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        _plugin.Setup(p => p.ClientType).Returns("TESTCLIENT");
        _plugin
            .Setup(p => p.OnBeforePostAsync(config, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _plugin
            .Setup(p => p.ExecutePostAsync(config, It.IsAny<PostContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PostBatchResult());
        SetupS3Config();
        SetupAuditRepoNoOp();

        var sut = CreateOrchestrator();

        // Act
        await sut.RunManualPostAsync("200", 1, CancellationToken.None);

        // Assert
        _plugin.Verify(p => p.OnBeforePostAsync(config, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // 6.6 — RunManualPostAsync: execution history written
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunManualPostAsync_WritesExecutionHistoryWithManualTriggerType()
    {
        // Arrange
        var config = ActiveConfig();
        var ds = BuildWorkitemDataSet(statusId: 101);
        GenericExecutionHistory? capturedHistory = null;

        _workitemRepo
            .Setup(r => r.GetWorkitemsByItemIdsAsync("300", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ds);
        _configRepo
            .Setup(r => r.GetBySourceQueueIdAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        _plugin.Setup(p => p.ClientType).Returns("TESTCLIENT");
        _plugin
            .Setup(p => p.ExecutePostAsync(config, It.IsAny<PostContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PostBatchResult { RecordsProcessed = 1, RecordsSuccess = 1 });
        _auditRepo
            .Setup(r => r.SaveExecutionHistoryAsync(
                It.IsAny<GenericExecutionHistory>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<GenericExecutionHistory, string, CancellationToken>((h, _, _) => capturedHistory = h)
            .Returns(Task.CompletedTask);
        SetupS3Config();

        var sut = CreateOrchestrator();

        // Act
        await sut.RunManualPostAsync("300", 1, CancellationToken.None);

        // Assert
        capturedHistory.Should().NotBeNull();
        capturedHistory!.TriggerType.Should().Be("Manual");
        capturedHistory.ExecutionType.Should().Be("POST");
        capturedHistory.Status.Should().Be("SUCCESS");
    }

    // -----------------------------------------------------------------------
    // Helper: build a DataSet with one workitem row
    // -----------------------------------------------------------------------

    private static DataSet BuildWorkitemDataSet(int statusId)
    {
        var ds = new DataSet();
        var table = new DataTable();
        table.Columns.Add("ItemId", typeof(long));
        table.Columns.Add("StatusID", typeof(int));
        table.Rows.Add(101L, statusId);
        ds.Tables.Add(table);
        return ds;
    }
}
