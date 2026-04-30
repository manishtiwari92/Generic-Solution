namespace IPS.AutoPost.Core.Migrations.Entities;

/// <summary>
/// EF Core entity for the <c>generic_execution_history</c> table.
/// Tracks every execution run with aggregate statistics for monitoring and the Status API.
/// Written by AutoPostOrchestrator — plugins never write to this table directly.
/// </summary>
public class GenericExecutionHistoryEntity
{
    public long Id { get; set; }

    /// <summary>Foreign key to <c>generic_job_configuration.id</c>.</summary>
    public int JobConfigId { get; set; }

    public string ClientType { get; set; } = string.Empty;
    public int JobId { get; set; }

    /// <summary>Execution type: 'POST', 'FEED_DOWNLOAD', or 'RETRY_IMAGES'.</summary>
    public string ExecutionType { get; set; } = string.Empty;

    /// <summary>Trigger type: 'SCHEDULED', 'MANUAL', or 'RETRY_QUEUE'.</summary>
    public string TriggerType { get; set; } = string.Empty;

    /// <summary>Outcome: 'SUCCESS', 'FAILED', 'PARTIAL_SUCCESS', or 'NO_RECORDS'.</summary>
    public string Status { get; set; } = string.Empty;

    public int RecordsProcessed { get; set; }
    public int RecordsSucceeded { get; set; }
    public int RecordsFailed { get; set; }
    public string? ErrorDetails { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    /// <summary>User ID for manual runs. Null for scheduled runs.</summary>
    public int? TriggeredByUser { get; set; }

    // Navigation property
    public GenericJobConfigurationEntity JobConfiguration { get; set; } = null!;
}
