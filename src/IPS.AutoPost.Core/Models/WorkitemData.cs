namespace IPS.AutoPost.Core.Models;

/// <summary>
/// Lightweight workitem reference loaded from the <c>Workitems</c> table.
/// Used by <c>WorkitemRepository</c> to pass eligible workitems to the orchestrator.
/// The full header and detail DataSet is fetched per-workitem by the plugin.
/// </summary>
public class WorkitemData
{
    /// <summary>
    /// Unique identifier of the workitem in the <c>Workitems</c> table.
    /// Corresponds to the <c>UID</c> column on the client-specific index header table.
    /// </summary>
    public long ItemId { get; set; }

    /// <summary>
    /// Current queue position (StatusId) of the workitem.
    /// Must be in the <c>GenericJobConfig.SourceQueueId</c> list to be eligible.
    /// </summary>
    public int StatusId { get; set; }
}
