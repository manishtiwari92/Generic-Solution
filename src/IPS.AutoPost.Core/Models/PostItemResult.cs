namespace IPS.AutoPost.Core.Models;

/// <summary>
/// Result for a single workitem within a post batch.
/// Collected into <see cref="PostBatchResult.ItemResults"/> by the plugin.
/// </summary>
public class PostItemResult
{
    /// <summary>The <c>ItemId</c> of the workitem that was processed.</summary>
    public long ItemId { get; set; }

    /// <summary><c>true</c> when the workitem was routed to the success queue.</summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Numeric response code from <c>api_response_configuration</c>
    /// (e.g. the code for POST_SUCCESS or RECORD_NOT_POSTED).
    /// </summary>
    public int ResponseCode { get; set; }

    /// <summary>
    /// Human-readable response message from <c>api_response_configuration</c>
    /// or a descriptive error string when no API response was received.
    /// </summary>
    public string ResponseMessage { get; set; } = string.Empty;

    /// <summary>
    /// The <c>StatusId</c> of the queue the workitem was routed to.
    /// Matches one of: <c>SuccessQueueId</c>, <c>PrimaryFailQueueId</c>,
    /// <c>SecondaryFailQueueId</c>, or <c>QuestionQueueId</c>.
    /// </summary>
    public long DestinationQueue { get; set; }
}
