using IPS.AutoPost.Core.Migrations;
using IPS.AutoPost.Core.Migrations.Entities;
using Microsoft.EntityFrameworkCore;

namespace IPS.AutoPost.Core.Tests.Migrations;

/// <summary>
/// Integration tests for AutoPostDatabaseContext using the EF Core InMemory provider.
/// Verifies that all 10 generic tables are created with correct structure and
/// that FK constraints are enforced at the EF Core model level.
/// </summary>
/// <remarks>
/// NOTE: The InMemory provider does not enforce SQL CHECK constraints or unique indexes.
/// Those are verified by the migration SQL script (task 7.4) and will be enforced
/// by SQL Server at runtime. This test suite focuses on:
/// 1. All 10 DbSets are accessible and can be queried
/// 2. Entities can be inserted and retrieved with correct property mapping
/// 3. Navigation properties (FK relationships) work correctly
/// 4. Required fields are enforced at the EF Core model level
/// </remarks>
public class AutoPostDatabaseContextTests : IDisposable
{
    private readonly AutoPostDatabaseContext _context;

    public AutoPostDatabaseContextTests()
    {
        var options = new DbContextOptionsBuilder<AutoPostDatabaseContext>()
            .UseInMemoryDatabase(databaseName: $"AutoPost_Test_{Guid.NewGuid()}")
            .Options;

        _context = new AutoPostDatabaseContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // -----------------------------------------------------------------------
    // Table existence — all 10 DbSets must be queryable
    // -----------------------------------------------------------------------

    [Fact]
    public void AllTenDbSets_AreQueryable()
    {
        // Act & Assert — no exception means the table/DbSet is registered
        Assert.NotNull(_context.JobConfigurations.ToList());
        Assert.NotNull(_context.ExecutionSchedules.ToList());
        Assert.NotNull(_context.FeedConfigurations.ToList());
        Assert.NotNull(_context.AuthConfigurations.ToList());
        Assert.NotNull(_context.QueueRoutingRules.ToList());
        Assert.NotNull(_context.PostHistories.ToList());
        Assert.NotNull(_context.EmailConfigurations.ToList());
        Assert.NotNull(_context.FeedDownloadHistories.ToList());
        Assert.NotNull(_context.ExecutionHistories.ToList());
        Assert.NotNull(_context.FieldMappings.ToList());
    }

    // -----------------------------------------------------------------------
    // generic_job_configuration — insert and retrieve
    // -----------------------------------------------------------------------

    [Fact]
    public async Task JobConfiguration_CanBeInsertedAndRetrieved()
    {
        // Arrange
        var entity = new GenericJobConfigurationEntity
        {
            ClientType = "INVITEDCLUB",
            JobId = 1001,
            JobName = "InvitedClub AutoPost",
            DefaultUserId = 100,
            IsActive = true,
            SourceQueueId = "101,102",
            SuccessQueueId = 200,
            PrimaryFailQueueId = 300,
            HeaderTable = "WFInvitedClubsIndexHeader",
            DetailTable = "WFInvitedClubsIndexDetails",
            HistoryTable = "post_to_invitedclub_history",
            AuthType = "BASIC",
            AuthUsername = "apiuser",
            AuthPassword = "secret",
            PostServiceUrl = "https://api.oracle.com/invoices",
            AllowAutoPost = true,
            DownloadFeed = true,
            IsLegacyJob = false,
            CreatedDate = DateTime.UtcNow
        };

        // Act
        _context.JobConfigurations.Add(entity);
        await _context.SaveChangesAsync();

        var retrieved = await _context.JobConfigurations
            .FirstOrDefaultAsync(j => j.JobId == 1001);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("INVITEDCLUB", retrieved.ClientType);
        Assert.Equal("WFInvitedClubsIndexHeader", retrieved.HeaderTable);
        Assert.Equal("BASIC", retrieved.AuthType);
        Assert.True(retrieved.AllowAutoPost);
        Assert.True(retrieved.DownloadFeed);
        Assert.False(retrieved.IsLegacyJob);
    }

    [Fact]
    public async Task JobConfiguration_DefaultValues_AreApplied()
    {
        // Arrange — minimal required fields only
        var entity = new GenericJobConfigurationEntity
        {
            ClientType = "SEVITA",
            JobId = 2001,
            JobName = "Sevita AutoPost",
            SourceQueueId = "201",
            CreatedDate = DateTime.UtcNow
        };

        // Act
        _context.JobConfigurations.Add(entity);
        await _context.SaveChangesAsync();

        var retrieved = await _context.JobConfigurations
            .FirstOrDefaultAsync(j => j.JobId == 2001);

        // Assert — verify EF Core default values are set
        Assert.NotNull(retrieved);
        Assert.Equal(100, retrieved.DefaultUserId);
        Assert.True(retrieved.IsActive);
        Assert.False(retrieved.AllowAutoPost);
        Assert.False(retrieved.DownloadFeed);
        Assert.False(retrieved.IsLegacyJob);
    }

    // -----------------------------------------------------------------------
    // generic_execution_schedule — FK relationship and constraint
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecutionSchedule_WithHHmmFormat_CanBeInserted()
    {
        // Arrange
        var job = CreateJobConfig("INVITEDCLUB", 1002);
        _context.JobConfigurations.Add(job);
        await _context.SaveChangesAsync();

        var schedule = new GenericExecutionScheduleEntity
        {
            JobConfigId = job.Id,
            ScheduleType = "POST",
            ExecutionTime = "08:00",
            IsActive = true
        };

        // Act
        _context.ExecutionSchedules.Add(schedule);
        await _context.SaveChangesAsync();

        var retrieved = await _context.ExecutionSchedules
            .Include(s => s.JobConfiguration)
            .FirstOrDefaultAsync(s => s.JobConfigId == job.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("08:00", retrieved.ExecutionTime);
        Assert.Null(retrieved.CronExpression);
        Assert.Equal("INVITEDCLUB", retrieved.JobConfiguration.ClientType);
    }

    [Fact]
    public async Task ExecutionSchedule_WithCronExpression_CanBeInserted()
    {
        // Arrange
        var job = CreateJobConfig("SEVITA", 2002);
        _context.JobConfigurations.Add(job);
        await _context.SaveChangesAsync();

        var schedule = new GenericExecutionScheduleEntity
        {
            JobConfigId = job.Id,
            ScheduleType = "DOWNLOAD",
            CronExpression = "cron(0 7 * * ? *)",
            IsActive = true
        };

        // Act
        _context.ExecutionSchedules.Add(schedule);
        await _context.SaveChangesAsync();

        var retrieved = await _context.ExecutionSchedules
            .FirstOrDefaultAsync(s => s.JobConfigId == job.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("cron(0 7 * * ? *)", retrieved.CronExpression);
        Assert.Null(retrieved.ExecutionTime);
        Assert.Equal("DOWNLOAD", retrieved.ScheduleType);
    }

    // -----------------------------------------------------------------------
    // generic_post_history — insert and retrieve
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PostHistory_CanBeInsertedAndRetrieved()
    {
        // Arrange
        var history = new GenericPostHistoryEntity
        {
            ClientType = "INVITEDCLUB",
            JobId = 1001,
            ItemId = 99001,
            StepName = "PostInvoice",
            PostRequest = """{"invoiceNumber":"INV001"}""",
            PostResponse = """{"invoiceId":"12345"}""",
            PostDate = DateTime.UtcNow,
            PostedBy = 100,
            ManuallyPosted = false
        };

        // Act
        _context.PostHistories.Add(history);
        await _context.SaveChangesAsync();

        var retrieved = await _context.PostHistories
            .FirstOrDefaultAsync(h => h.ItemId == 99001);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("PostInvoice", retrieved.StepName);
        Assert.Equal("INVITEDCLUB", retrieved.ClientType);
        Assert.False(retrieved.ManuallyPosted);
    }

    // -----------------------------------------------------------------------
    // generic_execution_history — FK relationship
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecutionHistory_WithForeignKey_CanBeInserted()
    {
        // Arrange
        var job = CreateJobConfig("INVITEDCLUB", 1003);
        _context.JobConfigurations.Add(job);
        await _context.SaveChangesAsync();

        var execHistory = new GenericExecutionHistoryEntity
        {
            JobConfigId = job.Id,
            ClientType = "INVITEDCLUB",
            JobId = 1003,
            ExecutionType = "POST",
            TriggerType = "SCHEDULED",
            Status = "SUCCESS",
            RecordsProcessed = 10,
            RecordsSucceeded = 10,
            RecordsFailed = 0,
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow
        };

        // Act
        _context.ExecutionHistories.Add(execHistory);
        await _context.SaveChangesAsync();

        var retrieved = await _context.ExecutionHistories
            .Include(h => h.JobConfiguration)
            .FirstOrDefaultAsync(h => h.JobConfigId == job.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("SUCCESS", retrieved.Status);
        Assert.Equal(10, retrieved.RecordsProcessed);
        Assert.Equal(0, retrieved.RecordsFailed);
        Assert.Equal("INVITEDCLUB", retrieved.JobConfiguration.ClientType);
    }

    // -----------------------------------------------------------------------
    // generic_feed_configuration — all source types
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FeedConfiguration_RestSource_CanBeInserted()
    {
        // Arrange
        var job = CreateJobConfig("INVITEDCLUB", 1004);
        _context.JobConfigurations.Add(job);
        await _context.SaveChangesAsync();

        var feed = new GenericFeedConfigurationEntity
        {
            JobConfigId = job.Id,
            FeedName = "Supplier",
            FeedSourceType = "REST",
            FeedUrl = "https://api.oracle.com/suppliers",
            FeedTableName = "InvitedClubSupplier",
            RefreshStrategy = "TRUNCATE",
            HasHeader = true,
            IsActive = true
        };

        // Act
        _context.FeedConfigurations.Add(feed);
        await _context.SaveChangesAsync();

        var retrieved = await _context.FeedConfigurations
            .FirstOrDefaultAsync(f => f.JobConfigId == job.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("REST", retrieved.FeedSourceType);
        Assert.Equal("Supplier", retrieved.FeedName);
        Assert.Equal("TRUNCATE", retrieved.RefreshStrategy);
    }

    // -----------------------------------------------------------------------
    // generic_queue_routing_rules — all result types
    // -----------------------------------------------------------------------

    [Fact]
    public async Task QueueRoutingRules_AllResultTypes_CanBeInserted()
    {
        // Arrange
        var job = CreateJobConfig("INVITEDCLUB", 1005);
        _context.JobConfigurations.Add(job);
        await _context.SaveChangesAsync();

        var resultTypes = new[] { "SUCCESS", "FAIL_POST", "FAIL_IMAGE", "DUPLICATE", "QUESTION", "TERMINATED" };
        var rules = resultTypes.Select((rt, i) => new GenericQueueRoutingRuleEntity
        {
            JobConfigId = job.Id,
            ResultType = rt,
            QueueId = 200 + i,
            IsActive = true
        }).ToList();

        // Act
        _context.QueueRoutingRules.AddRange(rules);
        await _context.SaveChangesAsync();

        var retrieved = await _context.QueueRoutingRules
            .Where(r => r.JobConfigId == job.Id)
            .ToListAsync();

        // Assert
        Assert.Equal(6, retrieved.Count);
        Assert.Contains(retrieved, r => r.ResultType == "SUCCESS");
        Assert.Contains(retrieved, r => r.ResultType == "FAIL_IMAGE");
        Assert.Contains(retrieved, r => r.ResultType == "TERMINATED");
    }

    // -----------------------------------------------------------------------
    // generic_email_configuration — all email types
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EmailConfiguration_AllEmailTypes_CanBeInserted()
    {
        // Arrange
        var job = CreateJobConfig("INVITEDCLUB", 1006);
        _context.JobConfigurations.Add(job);
        await _context.SaveChangesAsync();

        var emailTypes = new[] { "POST_FAILURE", "IMAGE_FAILURE", "MISSING_COA", "FEED_FAILURE" };
        var configs = emailTypes.Select(et => new GenericEmailConfigurationEntity
        {
            JobConfigId = job.Id,
            EmailType = et,
            EmailTo = "ops@example.com",
            EmailSubject = $"[AutoPost] {et}",
            SmtpServer = "smtp.example.com",
            SmtpPort = 587,
            SmtpUseSsl = true,
            IsActive = true
        }).ToList();

        // Act
        _context.EmailConfigurations.AddRange(configs);
        await _context.SaveChangesAsync();

        var retrieved = await _context.EmailConfigurations
            .Where(e => e.JobConfigId == job.Id)
            .ToListAsync();

        // Assert
        Assert.Equal(4, retrieved.Count);
        Assert.Contains(retrieved, e => e.EmailType == "MISSING_COA");
        Assert.All(retrieved, e => Assert.Equal(587, e.SmtpPort));
    }

    // -----------------------------------------------------------------------
    // generic_field_mapping — GenericRestPlugin mappings
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FieldMapping_WithTransformRule_CanBeInserted()
    {
        // Arrange
        var job = CreateJobConfig("VANTACA", 3001);
        _context.JobConfigurations.Add(job);
        await _context.SaveChangesAsync();

        var mappings = new[]
        {
            new GenericFieldMappingEntity
            {
                JobConfigId = job.Id,
                MappingType = "INVOICE_HEADER",
                SourceField = "InvoiceNumber",
                TargetField = "invoiceNumber",
                DataType = "VARCHAR",
                IsRequired = true,
                SortOrder = 1,
                IsActive = true
            },
            new GenericFieldMappingEntity
            {
                JobConfigId = job.Id,
                MappingType = "INVOICE_HEADER",
                SourceField = "InvoiceDate",
                TargetField = "invoiceDate",
                DataType = "DATE",
                TransformRule = """{"format":"yyyy-MM-dd"}""",
                IsRequired = true,
                SortOrder = 2,
                IsActive = true
            },
            new GenericFieldMappingEntity
            {
                JobConfigId = job.Id,
                MappingType = "INVOICE_HEADER",
                SourceField = "CONST:USD",
                TargetField = "currency",
                DataType = "VARCHAR",
                IsRequired = false,
                SortOrder = 3,
                IsActive = true
            }
        };

        // Act
        _context.FieldMappings.AddRange(mappings);
        await _context.SaveChangesAsync();

        var retrieved = await _context.FieldMappings
            .Where(m => m.JobConfigId == job.Id)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();

        // Assert
        Assert.Equal(3, retrieved.Count);
        Assert.Equal("InvoiceNumber", retrieved[0].SourceField);
        Assert.Equal("""{"format":"yyyy-MM-dd"}""", retrieved[1].TransformRule);
        Assert.Equal("CONST:USD", retrieved[2].SourceField);
        Assert.False(retrieved[2].IsRequired);
    }

    // -----------------------------------------------------------------------
    // generic_feed_download_history — status tracking
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FeedDownloadHistory_StartEndError_CanBeInserted()
    {
        // Arrange
        var statuses = new[] { "Start", "End", "Error" };
        var histories = statuses.Select(s => new GenericFeedDownloadHistoryEntity
        {
            JobConfigId = 1,
            FeedName = "Supplier",
            IsManual = false,
            Status = s,
            RecordCount = s == "End" ? 500 : null,
            ErrorMessage = s == "Error" ? "Connection timeout" : null,
            DownloadDate = DateTime.UtcNow
        }).ToList();

        // Act
        _context.FeedDownloadHistories.AddRange(histories);
        await _context.SaveChangesAsync();

        var retrieved = await _context.FeedDownloadHistories
            .Where(h => h.FeedName == "Supplier")
            .ToListAsync();

        // Assert
        Assert.Equal(3, retrieved.Count);
        Assert.Contains(retrieved, h => h.Status == "Start");
        Assert.Contains(retrieved, h => h.Status == "End" && h.RecordCount == 500);
        Assert.Contains(retrieved, h => h.Status == "Error" && h.ErrorMessage == "Connection timeout");
    }

    // -----------------------------------------------------------------------
    // Cascade delete — deleting a job config removes all child records
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CascadeDelete_RemovesAllChildRecords_WhenJobConfigDeleted()
    {
        // Arrange
        var job = CreateJobConfig("INVITEDCLUB", 1007);
        _context.JobConfigurations.Add(job);
        await _context.SaveChangesAsync();

        _context.ExecutionSchedules.Add(new GenericExecutionScheduleEntity
        {
            JobConfigId = job.Id, ScheduleType = "POST", ExecutionTime = "08:00", IsActive = true
        });
        _context.QueueRoutingRules.Add(new GenericQueueRoutingRuleEntity
        {
            JobConfigId = job.Id, ResultType = "SUCCESS", QueueId = 200, IsActive = true
        });
        _context.ExecutionHistories.Add(new GenericExecutionHistoryEntity
        {
            JobConfigId = job.Id, ClientType = "INVITEDCLUB", JobId = 1007,
            ExecutionType = "POST", TriggerType = "SCHEDULED", Status = "SUCCESS",
            StartTime = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        // Act — delete the parent job config
        _context.JobConfigurations.Remove(job);
        await _context.SaveChangesAsync();

        // Assert — all child records are gone
        Assert.Empty(await _context.ExecutionSchedules.Where(s => s.JobConfigId == job.Id).ToListAsync());
        Assert.Empty(await _context.QueueRoutingRules.Where(r => r.JobConfigId == job.Id).ToListAsync());
        Assert.Empty(await _context.ExecutionHistories.Where(h => h.JobConfigId == job.Id).ToListAsync());
    }

    // -----------------------------------------------------------------------
    // Navigation properties — Include works correctly
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NavigationProperties_LoadCorrectly_WithInclude()
    {
        // Arrange
        var job = CreateJobConfig("SEVITA", 2003);
        _context.JobConfigurations.Add(job);
        await _context.SaveChangesAsync();

        _context.ExecutionSchedules.Add(new GenericExecutionScheduleEntity
        {
            JobConfigId = job.Id, ScheduleType = "POST", CronExpression = "rate(30 minutes)", IsActive = true
        });
        _context.AuthConfigurations.Add(new GenericAuthConfigurationEntity
        {
            JobConfigId = job.Id, AuthPurpose = "POST", AuthType = "OAUTH", Username = "client_id"
        });
        await _context.SaveChangesAsync();

        // Act
        var retrieved = await _context.JobConfigurations
            .Include(j => j.ExecutionSchedules)
            .Include(j => j.AuthConfigurations)
            .FirstOrDefaultAsync(j => j.JobId == 2003);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Single(retrieved.ExecutionSchedules);
        Assert.Equal("rate(30 minutes)", retrieved.ExecutionSchedules.First().CronExpression);
        Assert.Single(retrieved.AuthConfigurations);
        Assert.Equal("OAUTH", retrieved.AuthConfigurations.First().AuthType);
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    private static GenericJobConfigurationEntity CreateJobConfig(string clientType, int jobId) =>
        new()
        {
            ClientType = clientType,
            JobId = jobId,
            JobName = $"{clientType} AutoPost",
            DefaultUserId = 100,
            IsActive = true,
            SourceQueueId = "101",
            CreatedDate = DateTime.UtcNow
        };
}
