namespace IPS.AutoPost.Core.Models;

/// <summary>
/// Result returned by <see cref="Interfaces.IClientPlugin.ExecuteFeedDownloadAsync"/>.
/// The Core_Engine uses <see cref="IsApplicable"/> to decide whether to update
/// <c>last_download_time</c> in <c>generic_job_configuration</c>.
/// </summary>
public class FeedResult
{
    /// <summary>
    /// <c>false</c> when the plugin has no feed download step (default implementation).
    /// The Core_Engine skips all feed-related processing when this is <c>false</c>.
    /// </summary>
    public bool IsApplicable { get; private set; }

    /// <summary>
    /// <c>true</c> when the feed download completed without errors.
    /// Only meaningful when <see cref="IsApplicable"/> is <c>true</c>.
    /// </summary>
    public bool Success { get; private set; }

    /// <summary>Total number of records downloaded and persisted in this run.</summary>
    public int RecordsDownloaded { get; set; }

    /// <summary>
    /// Error message when the feed download failed.
    /// <c>null</c> on success or when not applicable.
    /// </summary>
    public string? ErrorMessage { get; set; }

    // -----------------------------------------------------------------------
    // Factory methods
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns a result indicating this plugin has no feed download step.
    /// The Core_Engine will skip feed processing entirely.
    /// Used as the default return value in <see cref="Interfaces.IClientPlugin.ExecuteFeedDownloadAsync"/>.
    /// </summary>
    public static FeedResult NotApplicable() => new() { IsApplicable = false };

    /// <summary>
    /// Returns a successful feed download result.
    /// </summary>
    /// <param name="records">Number of records downloaded and persisted.</param>
    public static FeedResult Succeeded(int records) => new()
    {
        IsApplicable = true,
        Success = true,
        RecordsDownloaded = records
    };

    /// <summary>
    /// Returns a failed feed download result.
    /// </summary>
    /// <param name="error">Description of the failure.</param>
    public static FeedResult Failed(string error) => new()
    {
        IsApplicable = true,
        Success = false,
        ErrorMessage = error
    };
}
