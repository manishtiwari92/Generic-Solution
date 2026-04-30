using System.Data;
using IPS.AutoPost.Core.Models;

namespace IPS.AutoPost.Core.Interfaces;

/// <summary>
/// Fetches workitem data from the <c>Workitems</c> table and client-specific
/// index header/detail tables.
/// </summary>
public interface IWorkitemRepository
{
    /// <summary>
    /// Fetches all eligible workitems for a scheduled post run.
    /// Executes the query:
    /// <code>
    /// SELECT w.ItemId, w.StatusId
    /// FROM Workitems w
    /// JOIN {header_table} h ON w.ItemId = h.UID
    /// WHERE JobId = @JobId
    ///   AND StatusId IN ({source_queue_id})
    ///   AND ISNULL(h.PostInProcess, 0) = 0
    /// </code>
    /// The <c>PostInProcess = 0</c> filter prevents re-processing workitems that are
    /// already being handled by another worker task.
    /// </summary>
    /// <param name="config">Job configuration providing table names and queue IDs.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<WorkitemData>> GetWorkitemsAsync(
        GenericJobConfig config,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches the full header and detail DataSet for a specific workitem.
    /// Called per-workitem by the plugin to get the data needed to build the
    /// ERP API request payload.
    /// </summary>
    /// <param name="itemId">The <c>ItemId</c> of the workitem to fetch.</param>
    /// <param name="config">Job configuration providing table names and connection string.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<DataSet> GetWorkitemDataAsync(
        long itemId,
        GenericJobConfig config,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches workitems by a comma-separated list of <c>ItemId</c> values.
    /// Used by the manual post flow to look up specific workitems requested by the user.
    /// Returns a DataSet whose first table contains the workitem rows including
    /// <c>StatusID</c> for config resolution.
    /// </summary>
    /// <param name="itemIds">Comma-separated list of <c>ItemId</c> values.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<DataSet> GetWorkitemsByItemIdsAsync(string itemIds, CancellationToken ct = default);
}
