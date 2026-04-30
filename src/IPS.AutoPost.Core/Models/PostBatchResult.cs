namespace IPS.AutoPost.Core.Models;

/// <summary>
/// Aggregated result of a post execution batch returned by
/// <see cref="Interfaces.IClientPlugin.ExecutePostAsync"/>.
/// Used by <c>AutoPostOrchestrator</c> to write <c>generic_execution_history</c>
/// and by the API to return a response to the caller.
/// </summary>
public class PostBatchResult
{
    /// <summary>Total number of workitems that were attempted in this batch.</summary>
    public int RecordsProcessed { get; set; }

    /// <summary>Number of workitems that were routed to the success queue.</summary>
    public int RecordsSuccess { get; set; }

    /// <summary>Number of workitems that were routed to a failure queue.</summary>
    public int RecordsFailed { get; set; }

    /// <summary>Per-workitem results for detailed reporting and API responses.</summary>
    public List<PostItemResult> ItemResults { get; set; } = new();

    /// <summary>
    /// Top-level error message when the entire batch fails before processing any workitems
    /// (e.g. "Missing Configuration.", "No workitems found.").
    /// <c>null</c> when the batch completed normally (even if individual items failed).
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Numeric response code for API responses.
    /// -1 indicates a configuration error (e.g. no matching job config found).
    /// 0 (default) indicates normal completion.
    /// </summary>
    public int ResponseCode { get; set; }
}
