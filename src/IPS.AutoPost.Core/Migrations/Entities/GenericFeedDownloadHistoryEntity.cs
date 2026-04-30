namespace IPS.AutoPost.Core.Migrations.Entities;

/// <summary>
/// EF Core entity for the <c>generic_feed_download_history</c> table.
/// Tracks individual feed download operations with status and record counts.
/// </summary>
public class GenericFeedDownloadHistoryEntity
{
    public long Id { get; set; }
    public int JobConfigId { get; set; }
    public string FeedName { get; set; } = string.Empty;
    public bool IsManual { get; set; }

    /// <summary>Download status: 'Start', 'End', or 'Error'.</summary>
    public string Status { get; set; } = string.Empty;

    public int? RecordCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime DownloadDate { get; set; }
}
