using IPS.AutoPost.Core.Migrations.Entities;
using Microsoft.EntityFrameworkCore;

namespace IPS.AutoPost.Core.Migrations;

/// <summary>
/// EF Core DbContext for the 10 new generic tables only.
/// IMPORTANT: This context has NO DbSet for existing Workflow tables
/// (Workitems, WFInvitedClubsIndexHeader, post_to_invitedclub_configuration, etc.).
/// All access to existing tables uses SqlHelper directly.
/// </summary>
/// <remarks>
/// Used exclusively for:
/// 1. EF Core migrations (creating the 10 generic tables at startup)
/// 2. Integration tests (EF Core InMemory provider)
/// Runtime data access for generic tables also uses SqlHelper for consistency.
/// </remarks>
public class AutoPostDatabaseContext : DbContext
{
    public AutoPostDatabaseContext(DbContextOptions<AutoPostDatabaseContext> options)
        : base(options)
    {
    }

    // -----------------------------------------------------------------------
    // DbSets — 10 generic tables ONLY
    // -----------------------------------------------------------------------

    /// <summary>Replaces all 15+ post_to_xxx_configuration tables.</summary>
    public DbSet<GenericJobConfigurationEntity> JobConfigurations => Set<GenericJobConfigurationEntity>();

    /// <summary>Execution schedules (HH:mm + cron expression support).</summary>
    public DbSet<GenericExecutionScheduleEntity> ExecutionSchedules => Set<GenericExecutionScheduleEntity>();

    /// <summary>Feed source configuration (REST, FTP, SFTP, S3, FILE).</summary>
    public DbSet<GenericFeedConfigurationEntity> FeedConfigurations => Set<GenericFeedConfigurationEntity>();

    /// <summary>Per-job credential store (replaces embedded auth in config tables).</summary>
    public DbSet<GenericAuthConfigurationEntity> AuthConfigurations => Set<GenericAuthConfigurationEntity>();

    /// <summary>Configurable queue routing rules (replaces hardcoded queue IDs).</summary>
    public DbSet<GenericQueueRoutingRuleEntity> QueueRoutingRules => Set<GenericQueueRoutingRuleEntity>();

    /// <summary>Replaces all xxx_posted_records_history tables.</summary>
    public DbSet<GenericPostHistoryEntity> PostHistories => Set<GenericPostHistoryEntity>();

    /// <summary>Email notification configuration per job and notification type.</summary>
    public DbSet<GenericEmailConfigurationEntity> EmailConfigurations => Set<GenericEmailConfigurationEntity>();

    /// <summary>Individual feed download operation tracking.</summary>
    public DbSet<GenericFeedDownloadHistoryEntity> FeedDownloadHistories => Set<GenericFeedDownloadHistoryEntity>();

    /// <summary>Aggregate execution run statistics for monitoring and the Status API.</summary>
    public DbSet<GenericExecutionHistoryEntity> ExecutionHistories => Set<GenericExecutionHistoryEntity>();

    /// <summary>Dynamic payload field mappings for GenericRestPlugin clients.</summary>
    public DbSet<GenericFieldMappingEntity> FieldMappings => Set<GenericFieldMappingEntity>();

    // -----------------------------------------------------------------------
    // Model Configuration
    // -----------------------------------------------------------------------

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureJobConfiguration(modelBuilder);
        ConfigureExecutionSchedule(modelBuilder);
        ConfigureFeedConfiguration(modelBuilder);
        ConfigureAuthConfiguration(modelBuilder);
        ConfigureQueueRoutingRules(modelBuilder);
        ConfigurePostHistory(modelBuilder);
        ConfigureEmailConfiguration(modelBuilder);
        ConfigureFeedDownloadHistory(modelBuilder);
        ConfigureExecutionHistory(modelBuilder);
        ConfigureFieldMapping(modelBuilder);
    }

    // -----------------------------------------------------------------------
    // 5.1 generic_job_configuration
    // -----------------------------------------------------------------------
    private static void ConfigureJobConfiguration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GenericJobConfigurationEntity>(entity =>
        {
            entity.ToTable("generic_job_configuration");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();

            entity.Property(e => e.ClientType)
                .HasColumnName("client_type")
                .HasColumnType("VARCHAR(50)")
                .IsRequired();

            entity.Property(e => e.JobId)
                .HasColumnName("job_id")
                .IsRequired();

            entity.Property(e => e.JobName)
                .HasColumnName("job_name")
                .HasColumnType("VARCHAR(100)")
                .IsRequired();

            entity.Property(e => e.DefaultUserId)
                .HasColumnName("default_user_id")
                .HasDefaultValue(100)
                .IsRequired();

            entity.Property(e => e.IsActive)
                .HasColumnName("is_active")
                .HasDefaultValue(true)
                .IsRequired();

            // Queue IDs
            entity.Property(e => e.SourceQueueId)
                .HasColumnName("source_queue_id")
                .HasColumnType("VARCHAR(500)")
                .IsRequired();

            entity.Property(e => e.SuccessQueueId).HasColumnName("success_queue_id");
            entity.Property(e => e.PrimaryFailQueueId).HasColumnName("primary_fail_queue_id");
            entity.Property(e => e.SecondaryFailQueueId).HasColumnName("secondary_fail_queue_id");
            entity.Property(e => e.QuestionQueueId).HasColumnName("question_queue_id");
            entity.Property(e => e.TerminatedQueueId).HasColumnName("terminated_queue_id");

            // Table References
            entity.Property(e => e.HeaderTable)
                .HasColumnName("header_table")
                .HasColumnType("VARCHAR(200)");

            entity.Property(e => e.DetailTable)
                .HasColumnName("detail_table")
                .HasColumnType("VARCHAR(200)");

            entity.Property(e => e.DetailUidColumn)
                .HasColumnName("detail_uid_column")
                .HasColumnType("VARCHAR(200)");

            entity.Property(e => e.HistoryTable)
                .HasColumnName("history_table")
                .HasColumnType("VARCHAR(200)");

            entity.Property(e => e.DbConnectionString)
                .HasColumnName("db_connection_string")
                .HasColumnType("NVARCHAR(500)");

            // Authentication
            entity.Property(e => e.PostServiceUrl)
                .HasColumnName("post_service_url")
                .HasColumnType("VARCHAR(500)");

            entity.Property(e => e.AuthType)
                .HasColumnName("auth_type")
                .HasColumnType("VARCHAR(20)");

            entity.Property(e => e.AuthUsername)
                .HasColumnName("auth_username")
                .HasColumnType("VARCHAR(200)");

            entity.Property(e => e.AuthPassword)
                .HasColumnName("auth_password")
                .HasColumnType("VARCHAR(200)");

            // Scheduling
            entity.Property(e => e.LastPostTime).HasColumnName("last_post_time");
            entity.Property(e => e.LastDownloadTime).HasColumnName("last_download_time");

            entity.Property(e => e.AllowAutoPost)
                .HasColumnName("allow_auto_post")
                .HasDefaultValue(false)
                .IsRequired();

            entity.Property(e => e.DownloadFeed)
                .HasColumnName("download_feed")
                .HasDefaultValue(false)
                .IsRequired();

            // Paths
            entity.Property(e => e.OutputFilePath)
                .HasColumnName("output_file_path")
                .HasColumnType("NVARCHAR(500)");

            entity.Property(e => e.FeedDownloadPath)
                .HasColumnName("feed_download_path")
                .HasColumnType("NVARCHAR(500)");

            entity.Property(e => e.ImageParentPath)
                .HasColumnName("image_parent_path")
                .HasColumnType("NVARCHAR(500)");

            entity.Property(e => e.NewUiImageParentPath)
                .HasColumnName("new_ui_image_parent_path")
                .HasColumnType("NVARCHAR(500)");

            entity.Property(e => e.IsLegacyJob)
                .HasColumnName("is_legacy_job")
                .HasDefaultValue(false)
                .IsRequired();

            // Client-specific JSON blob
            entity.Property(e => e.ClientConfigJson)
                .HasColumnName("client_config_json")
                .HasColumnType("NVARCHAR(MAX)");

            entity.Property(e => e.CreatedDate)
                .HasColumnName("created_date")
                .HasDefaultValueSql("GETDATE()")
                .IsRequired();

            entity.Property(e => e.ModifiedDate).HasColumnName("modified_date");

            // Indexes
            entity.HasIndex(e => e.ClientType).HasDatabaseName("IX_generic_job_config_client_type");
            entity.HasIndex(e => e.JobId).HasDatabaseName("IX_generic_job_config_job_id").IsUnique();
            entity.HasIndex(e => e.IsActive).HasDatabaseName("IX_generic_job_config_is_active");
        });
    }

    // -----------------------------------------------------------------------
    // 5.2 generic_execution_schedule
    // -----------------------------------------------------------------------
    private static void ConfigureExecutionSchedule(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GenericExecutionScheduleEntity>(entity =>
        {
            entity.ToTable("generic_execution_schedule");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();

            entity.Property(e => e.JobConfigId).HasColumnName("job_config_id").IsRequired();

            entity.Property(e => e.ScheduleType)
                .HasColumnName("schedule_type")
                .HasColumnType("VARCHAR(20)")
                .HasDefaultValue("POST")
                .IsRequired();

            entity.Property(e => e.ExecutionTime)
                .HasColumnName("execution_time")
                .HasColumnType("VARCHAR(10)");

            entity.Property(e => e.CronExpression)
                .HasColumnName("cron_expression")
                .HasColumnType("VARCHAR(100)");

            entity.Property(e => e.LastExecutionTime).HasColumnName("last_execution_time");

            entity.Property(e => e.IsActive)
                .HasColumnName("is_active")
                .HasDefaultValue(true)
                .IsRequired();

            // CHECK constraint: at least one of execution_time or cron_expression must be set
            entity.ToTable(t => t.HasCheckConstraint(
                "CHK_schedule_has_time",
                "execution_time IS NOT NULL OR cron_expression IS NOT NULL"));

            // Foreign key
            entity.HasOne(e => e.JobConfiguration)
                .WithMany(j => j.ExecutionSchedules)
                .HasForeignKey(e => e.JobConfigId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.JobConfigId, e.ScheduleType })
                .HasDatabaseName("IX_exec_schedule_job_type");
        });
    }

    // -----------------------------------------------------------------------
    // 5.3 generic_feed_configuration
    // -----------------------------------------------------------------------
    private static void ConfigureFeedConfiguration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GenericFeedConfigurationEntity>(entity =>
        {
            entity.ToTable("generic_feed_configuration");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();

            entity.Property(e => e.JobConfigId).HasColumnName("job_config_id").IsRequired();

            entity.Property(e => e.FeedName)
                .HasColumnName("feed_name")
                .HasColumnType("VARCHAR(100)")
                .IsRequired();

            entity.Property(e => e.FeedSourceType)
                .HasColumnName("feed_source_type")
                .HasColumnType("VARCHAR(20)")
                .HasDefaultValue("REST")
                .IsRequired();

            entity.Property(e => e.FeedUrl)
                .HasColumnName("feed_url")
                .HasColumnType("VARCHAR(500)");

            entity.Property(e => e.FtpHost)
                .HasColumnName("ftp_host")
                .HasColumnType("VARCHAR(200)");

            entity.Property(e => e.FtpPort).HasColumnName("ftp_port");

            entity.Property(e => e.FtpPath)
                .HasColumnName("ftp_path")
                .HasColumnType("VARCHAR(500)");

            entity.Property(e => e.FtpFilePattern)
                .HasColumnName("ftp_file_pattern")
                .HasColumnType("VARCHAR(200)");

            entity.Property(e => e.S3Bucket)
                .HasColumnName("s3_bucket")
                .HasColumnType("VARCHAR(200)");

            entity.Property(e => e.S3KeyPrefix)
                .HasColumnName("s3_key_prefix")
                .HasColumnType("VARCHAR(500)");

            entity.Property(e => e.LocalFilePath)
                .HasColumnName("local_file_path")
                .HasColumnType("VARCHAR(500)");

            entity.Property(e => e.FileFormat)
                .HasColumnName("file_format")
                .HasColumnType("VARCHAR(20)");

            entity.Property(e => e.HasHeader)
                .HasColumnName("has_header")
                .HasDefaultValue(true)
                .IsRequired();

            entity.Property(e => e.Delimiter)
                .HasColumnName("delimiter")
                .HasColumnType("VARCHAR(5)");

            entity.Property(e => e.FeedTableName)
                .HasColumnName("feed_table_name")
                .HasColumnType("VARCHAR(200)");

            entity.Property(e => e.RefreshStrategy)
                .HasColumnName("refresh_strategy")
                .HasColumnType("VARCHAR(20)")
                .HasDefaultValue("TRUNCATE")
                .IsRequired();

            entity.Property(e => e.KeyColumn)
                .HasColumnName("key_column")
                .HasColumnType("VARCHAR(100)");

            entity.Property(e => e.LastDownloadTime).HasColumnName("last_download_time");

            entity.Property(e => e.IsActive)
                .HasColumnName("is_active")
                .HasDefaultValue(true)
                .IsRequired();

            entity.Property(e => e.FeedConfigJson)
                .HasColumnName("feed_config_json")
                .HasColumnType("NVARCHAR(MAX)");

            // Foreign key
            entity.HasOne(e => e.JobConfiguration)
                .WithMany(j => j.FeedConfigurations)
                .HasForeignKey(e => e.JobConfigId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.JobConfigId).HasDatabaseName("IX_feed_config_job_id");
        });
    }

    // -----------------------------------------------------------------------
    // 5.4 generic_auth_configuration
    // -----------------------------------------------------------------------
    private static void ConfigureAuthConfiguration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GenericAuthConfigurationEntity>(entity =>
        {
            entity.ToTable("generic_auth_configuration");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();

            entity.Property(e => e.JobConfigId).HasColumnName("job_config_id").IsRequired();

            entity.Property(e => e.AuthPurpose)
                .HasColumnName("auth_purpose")
                .HasColumnType("VARCHAR(20)")
                .IsRequired();

            entity.Property(e => e.AuthKey)
                .HasColumnName("auth_key")
                .HasColumnType("VARCHAR(100)");

            entity.Property(e => e.AuthType)
                .HasColumnName("auth_type")
                .HasColumnType("VARCHAR(20)")
                .IsRequired();

            entity.Property(e => e.Username)
                .HasColumnName("username")
                .HasColumnType("VARCHAR(200)");

            entity.Property(e => e.Password)
                .HasColumnName("password")
                .HasColumnType("VARCHAR(200)");

            entity.Property(e => e.ApiKey)
                .HasColumnName("api_key")
                .HasColumnType("VARCHAR(500)");

            entity.Property(e => e.TokenUrl)
                .HasColumnName("token_url")
                .HasColumnType("VARCHAR(500)");

            entity.Property(e => e.SecretArn)
                .HasColumnName("secret_arn")
                .HasColumnType("VARCHAR(500)");

            entity.Property(e => e.ExtraJson)
                .HasColumnName("extra_json")
                .HasColumnType("NVARCHAR(MAX)");

            // Foreign key
            entity.HasOne(e => e.JobConfiguration)
                .WithMany(j => j.AuthConfigurations)
                .HasForeignKey(e => e.JobConfigId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.JobConfigId, e.AuthPurpose })
                .HasDatabaseName("IX_auth_config_job_purpose");
        });
    }

    // -----------------------------------------------------------------------
    // 5.5 generic_queue_routing_rules
    // -----------------------------------------------------------------------
    private static void ConfigureQueueRoutingRules(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GenericQueueRoutingRuleEntity>(entity =>
        {
            entity.ToTable("generic_queue_routing_rules");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();

            entity.Property(e => e.JobConfigId).HasColumnName("job_config_id").IsRequired();

            entity.Property(e => e.ResultType)
                .HasColumnName("result_type")
                .HasColumnType("VARCHAR(50)")
                .IsRequired();

            entity.Property(e => e.QueueId).HasColumnName("queue_id").IsRequired();

            entity.Property(e => e.IsActive)
                .HasColumnName("is_active")
                .HasDefaultValue(true)
                .IsRequired();

            // Foreign key
            entity.HasOne(e => e.JobConfiguration)
                .WithMany(j => j.QueueRoutingRules)
                .HasForeignKey(e => e.JobConfigId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.JobConfigId, e.ResultType })
                .HasDatabaseName("IX_queue_routing_job_result");
        });
    }

    // -----------------------------------------------------------------------
    // 5.6 generic_post_history
    // -----------------------------------------------------------------------
    private static void ConfigurePostHistory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GenericPostHistoryEntity>(entity =>
        {
            entity.ToTable("generic_post_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();

            entity.Property(e => e.ClientType)
                .HasColumnName("client_type")
                .HasColumnType("VARCHAR(50)")
                .IsRequired();

            entity.Property(e => e.JobId).HasColumnName("job_id").IsRequired();
            entity.Property(e => e.ItemId).HasColumnName("item_id").IsRequired();

            entity.Property(e => e.StepName)
                .HasColumnName("step_name")
                .HasColumnType("VARCHAR(100)");

            entity.Property(e => e.PostRequest)
                .HasColumnName("post_request")
                .HasColumnType("NVARCHAR(MAX)");

            entity.Property(e => e.PostResponse)
                .HasColumnName("post_response")
                .HasColumnType("NVARCHAR(MAX)");

            entity.Property(e => e.PostDate)
                .HasColumnName("post_date")
                .HasDefaultValueSql("GETDATE()")
                .IsRequired();

            entity.Property(e => e.PostedBy).HasColumnName("posted_by").IsRequired();

            entity.Property(e => e.ManuallyPosted)
                .HasColumnName("manually_posted")
                .HasDefaultValue(false)
                .IsRequired();

            entity.Property(e => e.OutputFilePath)
                .HasColumnName("output_file_path")
                .HasColumnType("NVARCHAR(500)");

            // Composite index for fast lookups by item and job
            entity.HasIndex(e => new { e.ItemId, e.JobId })
                .HasDatabaseName("IX_generic_post_history_item");

            entity.HasIndex(e => e.ClientType).HasDatabaseName("IX_generic_post_history_client");
        });
    }

    // -----------------------------------------------------------------------
    // 5.7 generic_email_configuration
    // -----------------------------------------------------------------------
    private static void ConfigureEmailConfiguration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GenericEmailConfigurationEntity>(entity =>
        {
            entity.ToTable("generic_email_configuration");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();

            entity.Property(e => e.JobConfigId).HasColumnName("job_config_id").IsRequired();

            entity.Property(e => e.EmailType)
                .HasColumnName("email_type")
                .HasColumnType("VARCHAR(50)")
                .IsRequired();

            entity.Property(e => e.EmailTo)
                .HasColumnName("email_to")
                .HasColumnType("NVARCHAR(1000)");

            entity.Property(e => e.EmailCc)
                .HasColumnName("email_cc")
                .HasColumnType("NVARCHAR(1000)");

            entity.Property(e => e.EmailBcc)
                .HasColumnName("email_bcc")
                .HasColumnType("NVARCHAR(1000)");

            entity.Property(e => e.EmailSubject)
                .HasColumnName("email_subject")
                .HasColumnType("NVARCHAR(500)");

            entity.Property(e => e.EmailTemplate)
                .HasColumnName("email_template")
                .HasColumnType("NVARCHAR(500)");

            entity.Property(e => e.SmtpServer)
                .HasColumnName("smtp_server")
                .HasColumnType("VARCHAR(200)");

            entity.Property(e => e.SmtpPort)
                .HasColumnName("smtp_port")
                .HasDefaultValue(587);

            entity.Property(e => e.SmtpUsername)
                .HasColumnName("smtp_username")
                .HasColumnType("VARCHAR(200)");

            entity.Property(e => e.SmtpPassword)
                .HasColumnName("smtp_password")
                .HasColumnType("VARCHAR(200)");

            entity.Property(e => e.SmtpUseSsl)
                .HasColumnName("smtp_use_ssl")
                .HasDefaultValue(true)
                .IsRequired();

            entity.Property(e => e.IsActive)
                .HasColumnName("is_active")
                .HasDefaultValue(true)
                .IsRequired();

            // Foreign key
            entity.HasOne(e => e.JobConfiguration)
                .WithMany(j => j.EmailConfigurations)
                .HasForeignKey(e => e.JobConfigId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.JobConfigId, e.EmailType })
                .HasDatabaseName("IX_email_config_job_type");
        });
    }

    // -----------------------------------------------------------------------
    // 5.8 generic_feed_download_history
    // -----------------------------------------------------------------------
    private static void ConfigureFeedDownloadHistory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GenericFeedDownloadHistoryEntity>(entity =>
        {
            entity.ToTable("generic_feed_download_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();

            entity.Property(e => e.JobConfigId).HasColumnName("job_config_id").IsRequired();

            entity.Property(e => e.FeedName)
                .HasColumnName("feed_name")
                .HasColumnType("VARCHAR(100)")
                .IsRequired();

            entity.Property(e => e.IsManual)
                .HasColumnName("is_manual")
                .HasDefaultValue(false)
                .IsRequired();

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasColumnType("VARCHAR(20)")
                .IsRequired();

            entity.Property(e => e.RecordCount).HasColumnName("record_count");

            entity.Property(e => e.ErrorMessage)
                .HasColumnName("error_message")
                .HasColumnType("NVARCHAR(MAX)");

            entity.Property(e => e.DownloadDate)
                .HasColumnName("download_date")
                .HasDefaultValueSql("GETDATE()")
                .IsRequired();

            entity.HasIndex(e => new { e.JobConfigId, e.DownloadDate })
                .HasDatabaseName("IX_feed_download_history_job_date");
        });
    }

    // -----------------------------------------------------------------------
    // 5.9 generic_execution_history
    // -----------------------------------------------------------------------
    private static void ConfigureExecutionHistory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GenericExecutionHistoryEntity>(entity =>
        {
            entity.ToTable("generic_execution_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();

            entity.Property(e => e.JobConfigId).HasColumnName("job_config_id").IsRequired();

            entity.Property(e => e.ClientType)
                .HasColumnName("client_type")
                .HasColumnType("VARCHAR(50)")
                .IsRequired();

            entity.Property(e => e.JobId).HasColumnName("job_id").IsRequired();

            entity.Property(e => e.ExecutionType)
                .HasColumnName("execution_type")
                .HasColumnType("VARCHAR(30)")
                .IsRequired();

            entity.Property(e => e.TriggerType)
                .HasColumnName("trigger_type")
                .HasColumnType("VARCHAR(20)")
                .IsRequired();

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasColumnType("VARCHAR(20)")
                .IsRequired();

            entity.Property(e => e.RecordsProcessed)
                .HasColumnName("records_processed")
                .HasDefaultValue(0)
                .IsRequired();

            entity.Property(e => e.RecordsSucceeded)
                .HasColumnName("records_succeeded")
                .HasDefaultValue(0)
                .IsRequired();

            entity.Property(e => e.RecordsFailed)
                .HasColumnName("records_failed")
                .HasDefaultValue(0)
                .IsRequired();

            entity.Property(e => e.ErrorDetails)
                .HasColumnName("error_details")
                .HasColumnType("NVARCHAR(MAX)");

            entity.Property(e => e.StartTime)
                .HasColumnName("start_time")
                .HasDefaultValueSql("GETDATE()")
                .IsRequired();

            entity.Property(e => e.EndTime).HasColumnName("end_time");
            entity.Property(e => e.TriggeredByUser).HasColumnName("triggered_by_user");

            // Foreign key
            entity.HasOne(e => e.JobConfiguration)
                .WithMany(j => j.ExecutionHistories)
                .HasForeignKey(e => e.JobConfigId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes matching design.md section 7.10
            entity.HasIndex(e => new { e.JobConfigId, e.StartTime })
                .HasDatabaseName("IX_exec_history_job")
                .IsDescending(false, true);

            entity.HasIndex(e => new { e.Status, e.StartTime })
                .HasDatabaseName("IX_exec_history_status")
                .IsDescending(false, true);
        });
    }

    // -----------------------------------------------------------------------
    // 5.10 generic_field_mapping
    // -----------------------------------------------------------------------
    private static void ConfigureFieldMapping(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GenericFieldMappingEntity>(entity =>
        {
            entity.ToTable("generic_field_mapping");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityColumn();

            entity.Property(e => e.JobConfigId).HasColumnName("job_config_id").IsRequired();

            entity.Property(e => e.MappingType)
                .HasColumnName("mapping_type")
                .HasColumnType("VARCHAR(30)")
                .IsRequired();

            entity.Property(e => e.SourceField)
                .HasColumnName("source_field")
                .HasColumnType("VARCHAR(200)")
                .IsRequired();

            entity.Property(e => e.TargetField)
                .HasColumnName("target_field")
                .HasColumnType("VARCHAR(200)")
                .IsRequired();

            entity.Property(e => e.DataType)
                .HasColumnName("data_type")
                .HasColumnType("VARCHAR(50)")
                .HasDefaultValue("VARCHAR")
                .IsRequired();

            entity.Property(e => e.TransformRule)
                .HasColumnName("transform_rule")
                .HasColumnType("NVARCHAR(500)");

            entity.Property(e => e.IsRequired)
                .HasColumnName("is_required")
                .HasDefaultValue(false)
                .IsRequired();

            entity.Property(e => e.SortOrder)
                .HasColumnName("sort_order")
                .HasDefaultValue(0)
                .IsRequired();

            entity.Property(e => e.IsActive)
                .HasColumnName("is_active")
                .HasDefaultValue(true)
                .IsRequired();

            // Foreign key
            entity.HasOne(e => e.JobConfiguration)
                .WithMany(j => j.FieldMappings)
                .HasForeignKey(e => e.JobConfigId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.JobConfigId, e.MappingType, e.SortOrder })
                .HasDatabaseName("IX_field_mapping_job_type_sort");
        });
    }
}
