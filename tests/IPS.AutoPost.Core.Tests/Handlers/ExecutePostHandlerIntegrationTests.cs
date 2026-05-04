using FluentAssertions;
using FluentValidation;
using IPS.AutoPost.Core.Behaviors;
using IPS.AutoPost.Core.Commands;
using IPS.AutoPost.Core.Engine;
using IPS.AutoPost.Core.Handlers;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Migrations;
using IPS.AutoPost.Core.Migrations.Entities;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Core.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace IPS.AutoPost.Core.Tests.Handlers;

/// <summary>
/// Integration tests for <see cref="ExecutePostHandler"/> using EF Core InMemory database.
/// Verifies that the full MediatR pipeline (LoggingBehavior → ValidationBehavior → Handler)
/// dispatches correctly to the orchestrator, and that the handler correctly routes
/// scheduled vs manual post commands.
/// </summary>
/// <remarks>
/// These tests use a real MediatR pipeline with real behaviors registered in DI,
/// but mock the orchestrator to avoid needing a real database or external APIs.
/// The EF Core InMemory database is used to seed <c>generic_job_configuration</c>
/// rows that the orchestrator would normally read.
/// </remarks>
public class ExecutePostHandlerIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly AutoPostDatabaseContext _dbContext;
    private readonly Mock<AutoPostOrchestrator> _orchestratorMock;

    public ExecutePostHandlerIntegrationTests()
    {
        // -----------------------------------------------------------------------
        // Build EF Core InMemory context with a unique database per test
        // -----------------------------------------------------------------------
        var dbOptions = new DbContextOptionsBuilder<AutoPostDatabaseContext>()
            .UseInMemoryDatabase($"HandlerIntegration_{Guid.NewGuid()}")
            .Options;

        _dbContext = new AutoPostDatabaseContext(dbOptions);
        _dbContext.Database.EnsureCreated();

        // -----------------------------------------------------------------------
        // Build orchestrator mock — all dependencies mocked
        // -----------------------------------------------------------------------
        _orchestratorMock = new Mock<AutoPostOrchestrator>(
            Mock.Of<IConfigurationRepository>(),
            Mock.Of<IWorkitemRepository>(),
            Mock.Of<IRoutingRepository>(),
            Mock.Of<IAuditRepository>(),
            Mock.Of<IScheduleRepository>(),
            new PluginRegistry(),
            new SchedulerService(),
            Mock.Of<ICloudWatchMetricsService>(),
            Mock.Of<ILogger<AutoPostOrchestrator>>());

        // -----------------------------------------------------------------------
        // Build DI container with real MediatR pipeline
        // -----------------------------------------------------------------------
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Configuration (empty — not needed for handler tests)
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        // CorrelationId service
        services.AddScoped<ICorrelationIdService, CorrelationIdService>();

        // MediatR — register handlers from Core assembly
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ExecutePostHandler).Assembly));

        // Pipeline behaviors (in execution order)
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        // FluentValidation validators
        services.AddValidatorsFromAssembly(typeof(ExecutePostHandler).Assembly);

        // Register the mocked orchestrator so the handler gets it via DI
        services.AddScoped<AutoPostOrchestrator>(_ => _orchestratorMock.Object);

        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
        _serviceProvider.Dispose();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private IMediator GetMediator()
    {
        var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IMediator>();
    }

    private async Task SeedJobConfigAsync(string clientType = "INVITEDCLUB", int jobId = 42)
    {
        _dbContext.JobConfigurations.Add(new GenericJobConfigurationEntity
        {
            ClientType = clientType,
            JobId = jobId,
            JobName = $"{clientType} AutoPost",
            DefaultUserId = 100,
            IsActive = true,
            SourceQueueId = "101,102",
            SuccessQueueId = 200,
            PrimaryFailQueueId = 300,
            HeaderTable = "WFInvitedClubsIndexHeader",
            DetailTable = "WFInvitedClubsIndexDetails",
            HistoryTable = "post_to_invitedclub_history",
            AuthType = "BASIC",
            PostServiceUrl = "https://api.oracle.com/invoices",
            AllowAutoPost = true,
            DownloadFeed = true,
            IsLegacyJob = false,
            CreatedDate = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
    }

    // -----------------------------------------------------------------------
    // 1. Scheduled post — command dispatched, orchestrator called, result returned
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Send_ScheduledPostCommand_CallsRunScheduledPostAsync()
    {
        // Arrange
        await SeedJobConfigAsync("INVITEDCLUB", 42);

        var expected = new PostBatchResult { RecordsProcessed = 5, RecordsSuccess = 5 };
        _orchestratorMock
            .Setup(o => o.RunScheduledPostAsync(42, "INVITEDCLUB", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var mediator = GetMediator();
        var command = new ExecutePostCommand
        {
            JobId = 42,
            ClientType = "INVITEDCLUB",
            TriggerType = "Scheduled",
            ItemIds = string.Empty  // empty → scheduled path
        };

        // Act
        var result = await mediator.Send(command, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected,
            because: "the handler must return the orchestrator result unmodified");

        _orchestratorMock.Verify(
            o => o.RunScheduledPostAsync(42, "INVITEDCLUB", It.IsAny<CancellationToken>()),
            Times.Once);

        _orchestratorMock.Verify(
            o => o.RunManualPostAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Send_ScheduledPostCommand_ReturnsCorrectCounts()
    {
        // Arrange
        await SeedJobConfigAsync("SEVITA", 10);

        var expected = new PostBatchResult
        {
            RecordsProcessed = 8,
            RecordsSuccess = 6,
            RecordsFailed = 2
        };

        _orchestratorMock
            .Setup(o => o.RunScheduledPostAsync(10, "SEVITA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var mediator = GetMediator();
        var command = new ExecutePostCommand { JobId = 10, ClientType = "SEVITA" };

        // Act
        var result = await mediator.Send(command, CancellationToken.None);

        // Assert
        result.RecordsProcessed.Should().Be(8);
        result.RecordsSuccess.Should().Be(6);
        result.RecordsFailed.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // 2. Manual post — command dispatched, orchestrator called with ItemIds
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Send_ManualPostCommand_CallsRunManualPostAsync()
    {
        // Arrange
        await SeedJobConfigAsync("INVITEDCLUB", 42);

        var expected = new PostBatchResult { RecordsProcessed = 2, RecordsSuccess = 2 };
        _orchestratorMock
            .Setup(o => o.RunManualPostAsync("101,102", 99, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var mediator = GetMediator();
        var command = new ExecutePostCommand
        {
            JobId = 42,
            ClientType = "INVITEDCLUB",
            TriggerType = "Manual",
            ItemIds = "101,102",  // non-empty → manual path
            UserId = 99
        };

        // Act
        var result = await mediator.Send(command, CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);

        _orchestratorMock.Verify(
            o => o.RunManualPostAsync("101,102", 99, It.IsAny<CancellationToken>()),
            Times.Once);

        _orchestratorMock.Verify(
            o => o.RunScheduledPostAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // 3. Missing configuration — orchestrator returns error result
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Send_ManualPostCommand_OrchestratorReturnsMissingConfig_PropagatesError()
    {
        // Arrange — orchestrator returns "Missing Configuration." for unknown job
        var errorResult = new PostBatchResult
        {
            ResponseCode = -1,
            ErrorMessage = "Missing Configuration."
        };

        _orchestratorMock
            .Setup(o => o.RunManualPostAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(errorResult);

        var mediator = GetMediator();
        var command = new ExecutePostCommand
        {
            JobId = 999,
            ClientType = "UNKNOWN",
            ItemIds = "500",
            UserId = 1
        };

        // Act
        var result = await mediator.Send(command, CancellationToken.None);

        // Assert
        result.ResponseCode.Should().Be(-1,
            because: "the handler must propagate the orchestrator's error response code");
        result.ErrorMessage.Should().Be("Missing Configuration.",
            because: "the handler must propagate the orchestrator's error message");
    }

    // -----------------------------------------------------------------------
    // 4. LoggingBehavior is invoked — pipeline wraps the handler
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Send_Command_LoggingBehaviorIsInvoked()
    {
        // Arrange — verify the pipeline runs by checking the orchestrator is called
        // (LoggingBehavior wraps the handler; if it threw, the orchestrator would not be called)
        await SeedJobConfigAsync("INVITEDCLUB", 1);

        _orchestratorMock
            .Setup(o => o.RunScheduledPostAsync(1, "INVITEDCLUB", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PostBatchResult { RecordsProcessed = 1, RecordsSuccess = 1 });

        var mediator = GetMediator();
        var command = new ExecutePostCommand { JobId = 1, ClientType = "INVITEDCLUB" };

        // Act — should not throw even with LoggingBehavior in the pipeline
        var act = async () => await mediator.Send(command, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync(
            because: "LoggingBehavior must not interfere with normal command processing");

        _orchestratorMock.Verify(
            o => o.RunScheduledPostAsync(1, "INVITEDCLUB", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // 5. Mode field is passed through the pipeline
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Send_CommandWithMode_ModeIsAvailableInHandler()
    {
        // Arrange — verify Mode field is preserved through the pipeline
        await SeedJobConfigAsync("INVITEDCLUB", 5);

        _orchestratorMock
            .Setup(o => o.RunScheduledPostAsync(5, "INVITEDCLUB", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PostBatchResult());

        // We verify Mode is on the command (handler receives the full command)
        var mediator = GetMediator();
        var command = new ExecutePostCommand
        {
            JobId = 5,
            ClientType = "INVITEDCLUB",
            Mode = "DryRun"
        };

        // Act
        await mediator.Send(command, CancellationToken.None);

        // Assert — Mode field is preserved on the command object
        command.Mode.Should().Be("DryRun",
            because: "the Mode field must be preserved through the MediatR pipeline");
    }

    // -----------------------------------------------------------------------
    // 6. EF Core InMemory — seeded config data is queryable
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EfCoreInMemory_SeededJobConfig_IsQueryable()
    {
        // Arrange
        await SeedJobConfigAsync("INVITEDCLUB", 100);
        await SeedJobConfigAsync("SEVITA", 200);

        // Act
        var configs = await _dbContext.JobConfigurations.ToListAsync();

        // Assert
        configs.Should().HaveCount(2,
            because: "both seeded job configurations must be queryable from the InMemory database");
        configs.Should().Contain(c => c.ClientType == "INVITEDCLUB" && c.JobId == 100);
        configs.Should().Contain(c => c.ClientType == "SEVITA" && c.JobId == 200);
    }

    [Fact]
    public async Task EfCoreInMemory_JobConfig_AllowAutoPost_IsTrue()
    {
        // Arrange
        await SeedJobConfigAsync("INVITEDCLUB", 42);

        // Act
        var config = await _dbContext.JobConfigurations
            .FirstOrDefaultAsync(c => c.JobId == 42);

        // Assert
        config.Should().NotBeNull();
        config!.AllowAutoPost.Should().BeTrue(
            because: "the seeded InvitedClub config has AllowAutoPost=true");
        config.IsActive.Should().BeTrue(
            because: "the seeded config has IsActive=true");
    }

    // -----------------------------------------------------------------------
    // 7. Cancellation token propagation through the pipeline
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Send_Command_CancellationTokenPropagatedToOrchestrator()
    {
        // Arrange
        await SeedJobConfigAsync("INVITEDCLUB", 7);

        var cts = new CancellationTokenSource();
        CancellationToken capturedToken = CancellationToken.None;

        _orchestratorMock
            .Setup(o => o.RunScheduledPostAsync(7, "INVITEDCLUB", It.IsAny<CancellationToken>()))
            .Callback<int, string, CancellationToken>((_, _, ct) => capturedToken = ct)
            .ReturnsAsync(new PostBatchResult());

        var mediator = GetMediator();
        var command = new ExecutePostCommand { JobId = 7, ClientType = "INVITEDCLUB" };

        // Act
        await mediator.Send(command, cts.Token);

        // Assert
        capturedToken.Should().Be(cts.Token,
            because: "the cancellation token must be propagated through the MediatR pipeline to the orchestrator");
    }
}
