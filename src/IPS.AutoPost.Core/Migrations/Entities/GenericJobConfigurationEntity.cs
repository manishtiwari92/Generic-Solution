namespace IPS.AutoPost.Core.Migrations.Entities;

/// <summary>
/// EF Core entity for the <c>generic_job_configuration</c> table.
/// Replaces all 15+ legacy <c>post_to_xxx_configuration</c> tables.
/// One row per client job — new clients are onboarded by inserting rows here.
/// </summary>
/// <remarks>
/// IMPORTANT: This entity is used ONLY for EF Core migrations.
/// All runtime data access uses <see cref="DataAccess.ConfigurationRepository"/> via SqlHelper.
/// </remarks>
public class GenericJobConfigurationEntity
{
    public int Id { get; set; }
    public string ClientType { get; set; } = string.Empty;
    public int JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public int DefaultUserId { get; set; } = 100;
    public bool IsActive { get; set; } = true;

    // Queue IDs
    public string SourceQueueId { get; set; } = string.Empty;
    public int? SuccessQueueId { get; set; }
    public int? PrimaryFailQueueId { get; set; }
    public int? SecondaryFailQueueId { get; set; }
    public int? QuestionQueueId { get; set; }
    public int? TerminatedQueueId { get; set; }

    // Table References
    public string? HeaderTable { get; set; }
    public string? DetailTable { get; set; }
    public string? DetailUidColumn { get; set; }
    public string? HistoryTable { get; set; }
    public string? DbConnectionString { get; set; }

    // Authentication
    public string? PostServiceUrl { get; set; }
    public string? AuthType { get; set; }
    public string? AuthUsername { get; set; }
    public string? AuthPassword { get; set; }

    // Scheduling
    public DateTime? LastPostTime { get; set; }
    public DateTime? LastDownloadTime { get; set; }
    public bool AllowAutoPost { get; set; } = false;
    public bool DownloadFeed { get; set; } = false;

    // Paths
    public string? OutputFilePath { get; set; }
    public string? FeedDownloadPath { get; set; }
    public string? ImageParentPath { get; set; }
    public string? NewUiImageParentPath { get; set; }
    public bool IsLegacyJob { get; set; } = false;

    // Client-specific JSON blob
    public string? ClientConfigJson { get; set; }

    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    // Navigation properties
    public ICollection<GenericExecutionScheduleEntity> ExecutionSchedules { get; set; } = new List<GenericExecutionScheduleEntity>();
    public ICollection<GenericFeedConfigurationEntity> FeedConfigurations { get; set; } = new List<GenericFeedConfigurationEntity>();
    public ICollection<GenericAuthConfigurationEntity> AuthConfigurations { get; set; } = new List<GenericAuthConfigurationEntity>();
    public ICollection<GenericQueueRoutingRuleEntity> QueueRoutingRules { get; set; } = new List<GenericQueueRoutingRuleEntity>();
    public ICollection<GenericEmailConfigurationEntity> EmailConfigurations { get; set; } = new List<GenericEmailConfigurationEntity>();
    public ICollection<GenericFieldMappingEntity> FieldMappings { get; set; } = new List<GenericFieldMappingEntity>();
    public ICollection<GenericExecutionHistoryEntity> ExecutionHistories { get; set; } = new List<GenericExecutionHistoryEntity>();
}
