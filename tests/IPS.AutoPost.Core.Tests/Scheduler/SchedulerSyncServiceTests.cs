using Amazon.Scheduler;
using Amazon.Scheduler.Model;
using FluentAssertions;
using IPS.AutoPost.Scheduler;
using IPS.AutoPost.Scheduler.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace IPS.AutoPost.Core.Tests.Scheduler;

/// <summary>
/// Unit tests for <see cref="SchedulerSyncService"/>.
/// Covers task 21.3: new rule created, existing rule updated, inactive rule disabled.
/// </summary>
/// <remarks>
/// All tests use a mock <see cref="IAmazonScheduler"/> so no real AWS calls are made.
/// The <see cref="SchedulerSyncService.LoadScheduleRowsAsync"/> method is tested via
/// the public <see cref="SchedulerSyncService.SyncAsync"/> entry point using a
/// <see cref="TestableSchedulerSyncService"/> subclass that overrides the DB load.
/// </remarks>
public class SchedulerSyncServiceTests
{
    // -----------------------------------------------------------------------
    // Constants used across tests
    // -----------------------------------------------------------------------

    private const string FeedQueueArn     = "arn:aws:sqs:us-east-1:123456789012:ips-feed-queue";
    private const string PostQueueArn     = "arn:aws:sqs:us-east-1:123456789012:ips-post-queue";
    private const string SchedulerRoleArn = "arn:aws:iam::123456789012:role/ips-autopost-scheduler-role";

    // -----------------------------------------------------------------------
    // Testable subclass — overrides DB load so tests don't need a real DB
    // -----------------------------------------------------------------------

    private sealed class TestableSchedulerSyncService : SchedulerSyncService
    {
        private readonly IReadOnlyList<ScheduleSyncRow> _rows;

        public TestableSchedulerSyncService(
            IReadOnlyList<ScheduleSyncRow> rows,
            IAmazonScheduler schedulerClient,
            ILogger<SchedulerSyncService> logger)
            : base("Server=test;Database=Workflow;", schedulerClient, logger)
        {
            _rows = rows;
        }

        protected override Task<IReadOnlyList<ScheduleSyncRow>> LoadScheduleRowsAsync(
            CancellationToken ct = default)
            => Task.FromResult(_rows);
    }

    // -----------------------------------------------------------------------
    // Fixture helpers
    // -----------------------------------------------------------------------

    private readonly Mock<IAmazonScheduler> _scheduler = new();
    private readonly Mock<ILogger<SchedulerSyncService>> _logger = new();

    private TestableSchedulerSyncService CreateService(IReadOnlyList<ScheduleSyncRow> rows)
        => new(rows, _scheduler.Object, _logger.Object);

    private static ScheduleSyncRow ActivePostRow(int jobId = 1001, string cronExpr = "cron(0 14 * * ? *)") =>
        new()
        {
            ScheduleId     = 1,
            JobConfigId    = 10,
            JobId          = jobId,
            JobName        = "Test Job",
            ClientType     = "TESTCLIENT",
            ScheduleType   = "POST",
            CronExpression = cronExpr,
            ExecutionTime  = null,
            IsActive       = true
        };

    private static ScheduleSyncRow ActiveDownloadRow(int jobId = 1001, string executionTime = "08:00") =>
        new()
        {
            ScheduleId     = 2,
            JobConfigId    = 10,
            JobId          = jobId,
            JobName        = "Test Job",
            ClientType     = "TESTCLIENT",
            ScheduleType   = "DOWNLOAD",
            CronExpression = null,
            ExecutionTime  = executionTime,
            IsActive       = true
        };

    private static ScheduleSyncRow InactiveRow(int jobId = 1001) =>
        new()
        {
            ScheduleId     = 3,
            JobConfigId    = 10,
            JobId          = jobId,
            JobName        = "Inactive Job",
            ClientType     = "TESTCLIENT",
            ScheduleType   = "POST",
            CronExpression = "cron(0 9 * * ? *)",
            ExecutionTime  = null,
            IsActive       = false
        };

    private void SetupGroupExists()
    {
        _scheduler
            .Setup(s => s.GetScheduleGroupAsync(
                It.IsAny<GetScheduleGroupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetScheduleGroupResponse());
    }

    private void SetupRuleNotFound(string ruleName)
    {
        _scheduler
            .Setup(s => s.GetScheduleAsync(
                It.Is<GetScheduleRequest>(r => r.Name == ruleName),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("not found"));
    }

    private void SetupRuleExists(string ruleName, string scheduleExpression, ScheduleState state = null!)
    {
        _scheduler
            .Setup(s => s.GetScheduleAsync(
                It.Is<GetScheduleRequest>(r => r.Name == ruleName),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetScheduleResponse
            {
                Name               = ruleName,
                ScheduleExpression = scheduleExpression,
                State              = state ?? ScheduleState.ENABLED,
                Target             = new Target
                {
                    Arn     = PostQueueArn,
                    RoleArn = SchedulerRoleArn,
                    Input   = "{}"
                },
                FlexibleTimeWindow = new FlexibleTimeWindow
                {
                    Mode                   = FlexibleTimeWindowMode.FLEXIBLE,
                    MaximumWindowInMinutes = 5
                }
            });
    }

    private void SetupCreateSucceeds()
    {
        _scheduler
            .Setup(s => s.CreateScheduleAsync(
                It.IsAny<CreateScheduleRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateScheduleResponse());
    }

    private void SetupUpdateSucceeds()
    {
        _scheduler
            .Setup(s => s.UpdateScheduleAsync(
                It.IsAny<UpdateScheduleRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateScheduleResponse());
    }

    // -----------------------------------------------------------------------
    // BuildRuleName
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildRuleName_PostSchedule_ReturnsExpectedName()
    {
        SchedulerSyncService.BuildRuleName(1001, "POST")
            .Should().Be("ips-autopost-1001-post");
    }

    [Fact]
    public void BuildRuleName_DownloadSchedule_ReturnsExpectedName()
    {
        SchedulerSyncService.BuildRuleName(1001, "DOWNLOAD")
            .Should().Be("ips-autopost-1001-download");
    }

    [Fact]
    public void BuildRuleName_MixedCaseScheduleType_IsLowercased()
    {
        SchedulerSyncService.BuildRuleName(42, "Download")
            .Should().Be("ips-autopost-42-download");
    }

    // -----------------------------------------------------------------------
    // BuildScheduleExpression
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildScheduleExpression_CronExpressionSet_ReturnsCronAsIs()
    {
        var row = ActivePostRow(cronExpr: "cron(0 14 * * ? *)");
        SchedulerSyncService.BuildScheduleExpression(row)
            .Should().Be("cron(0 14 * * ? *)");
    }

    [Fact]
    public void BuildScheduleExpression_ExecutionTimeOnly_ConvertsToDailyCron()
    {
        var row = ActiveDownloadRow(executionTime: "14:30");
        SchedulerSyncService.BuildScheduleExpression(row)
            .Should().Be("cron(30 14 * * ? *)");
    }

    [Fact]
    public void BuildScheduleExpression_ExecutionTimeMidnight_ConvertsToDailyCron()
    {
        var row = ActiveDownloadRow(executionTime: "00:00");
        SchedulerSyncService.BuildScheduleExpression(row)
            .Should().Be("cron(0 0 * * ? *)");
    }

    [Fact]
    public void BuildScheduleExpression_NeitherCronNorExecutionTime_ReturnsNull()
    {
        var row = new ScheduleSyncRow
        {
            ScheduleId     = 1,
            JobId          = 1001,
            ScheduleType   = "POST",
            CronExpression = null,
            ExecutionTime  = null,
            IsActive       = true
        };
        SchedulerSyncService.BuildScheduleExpression(row).Should().BeNull();
    }

    [Fact]
    public void BuildScheduleExpression_InvalidExecutionTime_ReturnsNull()
    {
        var row = new ScheduleSyncRow
        {
            ScheduleId     = 1,
            JobId          = 1001,
            ScheduleType   = "POST",
            CronExpression = null,
            ExecutionTime  = "not-a-time",
            IsActive       = true
        };
        SchedulerSyncService.BuildScheduleExpression(row).Should().BeNull();
    }

    [Fact]
    public void BuildScheduleExpression_CronExpressionTakesPrecedenceOverExecutionTime()
    {
        var row = new ScheduleSyncRow
        {
            ScheduleId     = 1,
            JobId          = 1001,
            ScheduleType   = "POST",
            CronExpression = "cron(0 9 * * ? *)",
            ExecutionTime  = "14:00",  // should be ignored
            IsActive       = true
        };
        SchedulerSyncService.BuildScheduleExpression(row)
            .Should().Be("cron(0 9 * * ? *)");
    }

    // -----------------------------------------------------------------------
    // SyncAsync — new rule created
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SyncAsync_NewPostRule_CreatesRule()
    {
        // Arrange
        var row = ActivePostRow(jobId: 1001, cronExpr: "cron(0 14 * * ? *)");
        var sut = CreateService([row]);
        var ruleName = SchedulerSyncService.BuildRuleName(1001, "POST");

        SetupGroupExists();
        SetupRuleNotFound(ruleName);
        SetupCreateSucceeds();

        // Act
        await sut.SyncAsync(FeedQueueArn, PostQueueArn, SchedulerRoleArn);

        // Assert — CreateScheduleAsync called once with correct rule name and expression
        _scheduler.Verify(
            s => s.CreateScheduleAsync(
                It.Is<CreateScheduleRequest>(r =>
                    r.Name == ruleName &&
                    r.ScheduleExpression == "cron(0 14 * * ? *)" &&
                    r.State == ScheduleState.ENABLED &&
                    r.GroupName == SchedulerSyncService.ScheduleGroupName),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncAsync_NewDownloadRule_TargetsFeedQueue()
    {
        // Arrange
        var row = ActiveDownloadRow(jobId: 2002, executionTime: "08:00");
        var sut = CreateService([row]);
        var ruleName = SchedulerSyncService.BuildRuleName(2002, "DOWNLOAD");

        SetupGroupExists();
        SetupRuleNotFound(ruleName);
        SetupCreateSucceeds();

        // Act
        await sut.SyncAsync(FeedQueueArn, PostQueueArn, SchedulerRoleArn);

        // Assert — target ARN must be the feed queue
        _scheduler.Verify(
            s => s.CreateScheduleAsync(
                It.Is<CreateScheduleRequest>(r =>
                    r.Name == ruleName &&
                    r.Target.Arn == FeedQueueArn),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncAsync_NewPostRule_TargetsPostQueue()
    {
        // Arrange
        var row = ActivePostRow(jobId: 1001);
        var sut = CreateService([row]);
        var ruleName = SchedulerSyncService.BuildRuleName(1001, "POST");

        SetupGroupExists();
        SetupRuleNotFound(ruleName);
        SetupCreateSucceeds();

        // Act
        await sut.SyncAsync(FeedQueueArn, PostQueueArn, SchedulerRoleArn);

        // Assert — target ARN must be the post queue
        _scheduler.Verify(
            s => s.CreateScheduleAsync(
                It.Is<CreateScheduleRequest>(r =>
                    r.Target.Arn == PostQueueArn),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncAsync_NewRule_SqsMessageBodyContainsCorrectFields()
    {
        // Arrange
        var row = ActivePostRow(jobId: 1001, cronExpr: "cron(0 14 * * ? *)");
        var sut = CreateService([row]);
        var ruleName = SchedulerSyncService.BuildRuleName(1001, "POST");

        SetupGroupExists();
        SetupRuleNotFound(ruleName);

        string? capturedInput = null;
        _scheduler
            .Setup(s => s.CreateScheduleAsync(
                It.IsAny<CreateScheduleRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateScheduleRequest, CancellationToken>((req, _) =>
                capturedInput = req.Target.Input)
            .ReturnsAsync(new CreateScheduleResponse());

        // Act
        await sut.SyncAsync(FeedQueueArn, PostQueueArn, SchedulerRoleArn);

        // Assert — message body must contain JobId, ClientType, Pipeline, TriggerType
        capturedInput.Should().NotBeNullOrEmpty();
        capturedInput.Should().Contain("\"JobId\":1001");
        capturedInput.Should().Contain("\"ClientType\":\"TESTCLIENT\"");
        capturedInput.Should().Contain("\"Pipeline\":\"Post\"");
        capturedInput.Should().Contain("\"TriggerType\":\"Scheduled\"");
    }

    [Fact]
    public async Task SyncAsync_DownloadRule_SqsMessageBodyPipelineIsFeed()
    {
        // Arrange
        var row = ActiveDownloadRow(jobId: 2002);
        var sut = CreateService([row]);
        var ruleName = SchedulerSyncService.BuildRuleName(2002, "DOWNLOAD");

        SetupGroupExists();
        SetupRuleNotFound(ruleName);

        string? capturedInput = null;
        _scheduler
            .Setup(s => s.CreateScheduleAsync(
                It.IsAny<CreateScheduleRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateScheduleRequest, CancellationToken>((req, _) =>
                capturedInput = req.Target.Input)
            .ReturnsAsync(new CreateScheduleResponse());

        // Act
        await sut.SyncAsync(FeedQueueArn, PostQueueArn, SchedulerRoleArn);

        // Assert — Pipeline must be "Feed" for DOWNLOAD schedule type
        capturedInput.Should().Contain("\"Pipeline\":\"Feed\"");
    }

    // -----------------------------------------------------------------------
    // SyncAsync — existing rule updated when cron changed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SyncAsync_ExistingRuleWithChangedCron_UpdatesRule()
    {
        // Arrange
        var row = ActivePostRow(jobId: 1001, cronExpr: "cron(0 16 * * ? *)"); // new: 16:00
        var sut = CreateService([row]);
        var ruleName = SchedulerSyncService.BuildRuleName(1001, "POST");

        SetupGroupExists();
        SetupRuleExists(ruleName, "cron(0 14 * * ? *)"); // existing: 14:00
        SetupUpdateSucceeds();

        // Act
        await sut.SyncAsync(FeedQueueArn, PostQueueArn, SchedulerRoleArn);

        // Assert — UpdateScheduleAsync called with new expression
        _scheduler.Verify(
            s => s.UpdateScheduleAsync(
                It.Is<UpdateScheduleRequest>(r =>
                    r.Name == ruleName &&
                    r.ScheduleExpression == "cron(0 16 * * ? *)" &&
                    r.State == ScheduleState.ENABLED),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // CreateScheduleAsync must NOT be called
        _scheduler.Verify(
            s => s.CreateScheduleAsync(
                It.IsAny<CreateScheduleRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncAsync_ExistingRuleWithSameCron_DoesNotUpdate()
    {
        // Arrange — same expression as what's already in EventBridge
        var row = ActivePostRow(jobId: 1001, cronExpr: "cron(0 14 * * ? *)");
        var sut = CreateService([row]);
        var ruleName = SchedulerSyncService.BuildRuleName(1001, "POST");

        SetupGroupExists();
        SetupRuleExists(ruleName, "cron(0 14 * * ? *)"); // same expression

        // Act
        await sut.SyncAsync(FeedQueueArn, PostQueueArn, SchedulerRoleArn);

        // Assert — neither Create nor Update should be called
        _scheduler.Verify(
            s => s.CreateScheduleAsync(
                It.IsAny<CreateScheduleRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _scheduler.Verify(
            s => s.UpdateScheduleAsync(
                It.IsAny<UpdateScheduleRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncAsync_ExistingRuleWithChangedExecutionTime_UpdatesRule()
    {
        // Arrange — execution_time changed from 08:00 to 10:00
        var row = ActiveDownloadRow(jobId: 2002, executionTime: "10:00");
        var sut = CreateService([row]);
        var ruleName = SchedulerSyncService.BuildRuleName(2002, "DOWNLOAD");

        SetupGroupExists();
        SetupRuleExists(ruleName, "cron(0 8 * * ? *)"); // existing: 08:00
        SetupUpdateSucceeds();

        // Act
        await sut.SyncAsync(FeedQueueArn, PostQueueArn, SchedulerRoleArn);

        // Assert — updated to 10:00 cron
        _scheduler.Verify(
            s => s.UpdateScheduleAsync(
                It.Is<UpdateScheduleRequest>(r =>
                    r.ScheduleExpression == "cron(0 10 * * ? *)"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // SyncAsync — inactive rule disabled
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SyncAsync_InactiveRow_DisablesExistingRule()
    {
        // Arrange
        var row = InactiveRow(jobId: 1001);
        var sut = CreateService([row]);
        var ruleName = SchedulerSyncService.BuildRuleName(1001, "POST");

        SetupGroupExists();
        SetupRuleExists(ruleName, "cron(0 9 * * ? *)", ScheduleState.ENABLED);
        SetupUpdateSucceeds();

        // Act
        await sut.SyncAsync(FeedQueueArn, PostQueueArn, SchedulerRoleArn);

        // Assert — UpdateScheduleAsync called with DISABLED state
        _scheduler.Verify(
            s => s.UpdateScheduleAsync(
                It.Is<UpdateScheduleRequest>(r =>
                    r.Name == ruleName &&
                    r.State == ScheduleState.DISABLED),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncAsync_InactiveRow_RuleAlreadyDisabled_DoesNotCallUpdate()
    {
        // Arrange — rule is already disabled in EventBridge
        var row = InactiveRow(jobId: 1001);
        var sut = CreateService([row]);
        var ruleName = SchedulerSyncService.BuildRuleName(1001, "POST");

        SetupGroupExists();
        SetupRuleExists(ruleName, "cron(0 9 * * ? *)", ScheduleState.DISABLED);

        // Act
        await sut.SyncAsync(FeedQueueArn, PostQueueArn, SchedulerRoleArn);

        // Assert — no update needed since it's already disabled
        _scheduler.Verify(
            s => s.UpdateScheduleAsync(
                It.IsAny<UpdateScheduleRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncAsync_InactiveRow_RuleDoesNotExist_DoesNotCreate()
    {
        // Arrange — inactive row but rule doesn't exist in EventBridge yet
        var row = InactiveRow(jobId: 1001);
        var sut = CreateService([row]);
        var ruleName = SchedulerSyncService.BuildRuleName(1001, "POST");

        SetupGroupExists();
        SetupRuleNotFound(ruleName);

        // Act
        await sut.SyncAsync(FeedQueueArn, PostQueueArn, SchedulerRoleArn);

        // Assert — neither Create nor Update should be called for an inactive row
        _scheduler.Verify(
            s => s.CreateScheduleAsync(
                It.IsAny<CreateScheduleRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _scheduler.Verify(
            s => s.UpdateScheduleAsync(
                It.IsAny<UpdateScheduleRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // SyncAsync — rows with no valid schedule expression are skipped
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SyncAsync_RowWithNoScheduleExpression_IsSkipped()
    {
        // Arrange — active row but no cron_expression and no execution_time
        var row = new ScheduleSyncRow
        {
            ScheduleId     = 1,
            JobConfigId    = 10,
            JobId          = 1001,
            JobName        = "Test Job",
            ClientType     = "TESTCLIENT",
            ScheduleType   = "POST",
            CronExpression = null,
            ExecutionTime  = null,
            IsActive       = true
        };
        var sut = CreateService([row]);

        SetupGroupExists();

        // Act
        await sut.SyncAsync(FeedQueueArn, PostQueueArn, SchedulerRoleArn);

        // Assert — no rule operations performed
        _scheduler.Verify(
            s => s.CreateScheduleAsync(
                It.IsAny<CreateScheduleRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // SyncAsync — multiple rows processed independently
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SyncAsync_MultipleRows_EachProcessedIndependently()
    {
        // Arrange — one new POST rule, one new DOWNLOAD rule
        var postRow     = ActivePostRow(jobId: 1001, cronExpr: "cron(0 14 * * ? *)");
        var downloadRow = ActiveDownloadRow(jobId: 1001, executionTime: "08:00");
        var sut = CreateService([postRow, downloadRow]);

        var postRuleName     = SchedulerSyncService.BuildRuleName(1001, "POST");
        var downloadRuleName = SchedulerSyncService.BuildRuleName(1001, "DOWNLOAD");

        SetupGroupExists();
        SetupRuleNotFound(postRuleName);
        SetupRuleNotFound(downloadRuleName);
        SetupCreateSucceeds();

        // Act
        await sut.SyncAsync(FeedQueueArn, PostQueueArn, SchedulerRoleArn);

        // Assert — CreateScheduleAsync called twice (once per rule)
        _scheduler.Verify(
            s => s.CreateScheduleAsync(
                It.IsAny<CreateScheduleRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SyncAsync_EmptyScheduleList_NoRuleOperationsPerformed()
    {
        // Arrange — no schedule rows in DB
        var sut = CreateService([]);

        SetupGroupExists();

        // Act
        await sut.SyncAsync(FeedQueueArn, PostQueueArn, SchedulerRoleArn);

        // Assert — no rule operations
        _scheduler.Verify(
            s => s.CreateScheduleAsync(
                It.IsAny<CreateScheduleRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _scheduler.Verify(
            s => s.UpdateScheduleAsync(
                It.IsAny<UpdateScheduleRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // SyncAsync — schedule group created when it does not exist
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SyncAsync_ScheduleGroupNotFound_CreatesGroup()
    {
        // Arrange
        var row = ActivePostRow();
        var sut = CreateService([row]);
        var ruleName = SchedulerSyncService.BuildRuleName(1001, "POST");

        // Group does not exist
        _scheduler
            .Setup(s => s.GetScheduleGroupAsync(
                It.IsAny<GetScheduleGroupRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("group not found"));

        _scheduler
            .Setup(s => s.CreateScheduleGroupAsync(
                It.IsAny<CreateScheduleGroupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateScheduleGroupResponse());

        SetupRuleNotFound(ruleName);
        SetupCreateSucceeds();

        // Act
        await sut.SyncAsync(FeedQueueArn, PostQueueArn, SchedulerRoleArn);

        // Assert — group was created
        _scheduler.Verify(
            s => s.CreateScheduleGroupAsync(
                It.Is<CreateScheduleGroupRequest>(r =>
                    r.Name == SchedulerSyncService.ScheduleGroupName),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
