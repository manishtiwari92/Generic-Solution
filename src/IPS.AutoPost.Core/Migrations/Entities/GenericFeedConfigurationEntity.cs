namespace IPS.AutoPost.Core.Migrations.Entities;

/// <summary>
/// EF Core entity for the <c>generic_feed_configuration</c> table.
/// Replaces per-client feed type tables. Supports REST, FTP, SFTP, S3, and FILE sources.
/// </summary>
public class GenericFeedConfigurationEntity
{
    public int Id { get; set; }

    /// <summary>Foreign key to <c>generic_job_configuration.id</c>.</summary>
    public int JobConfigId { get; set; }

    /// <summary>Feed name (e.g. 'Vendor', 'Supplier', 'COA', 'MediaOrder').</summary>
    public string FeedName { get; set; } = string.Empty;

    /// <summary>
    /// Source type: 'REST', 'FTP', 'SFTP', 'S3', or 'FILE'.
    /// Determines which connection fields are used.
    /// </summary>
    public string FeedSourceType { get; set; } = "REST";

    // REST source fields
    public string? FeedUrl { get; set; }

    // FTP/SFTP source fields
    public string? FtpHost { get; set; }
    public int? FtpPort { get; set; }
    public string? FtpPath { get; set; }
    public string? FtpFilePattern { get; set; }

    // S3 source fields
    public string? S3Bucket { get; set; }
    public string? S3KeyPrefix { get; set; }

    // Local file source fields
    public string? LocalFilePath { get; set; }

    // File format (applies to FTP/SFTP/S3/FILE sources)
    public string? FileFormat { get; set; }
    public bool HasHeader { get; set; }
    public string? Delimiter { get; set; }

    // Target DB table
    public string? FeedTableName { get; set; }

    /// <summary>
    /// Refresh strategy: 'TRUNCATE', 'DELETE_BY_KEY', or 'INCREMENTAL'.
    /// </summary>
    public string RefreshStrategy { get; set; } = "TRUNCATE";

    /// <summary>Key column for DELETE_BY_KEY strategy (e.g. 'SupplierId').</summary>
    public string? KeyColumn { get; set; }

    public DateTime? LastDownloadTime { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// Extra parameters as JSON (date range, pagination size, chunk size, etc.).
    /// </summary>
    public string? FeedConfigJson { get; set; }

    // Navigation property
    public GenericJobConfigurationEntity JobConfiguration { get; set; } = null!;
}
