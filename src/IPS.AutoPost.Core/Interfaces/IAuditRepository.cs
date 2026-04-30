using IPS.AutoPost.Core.Models;

namespace IPS.AutoPost.Core.Interfaces;

/// <summary>
/// Handles all audit logging operations: general log entries, post history,
/// and execution history.
/// </summary>
public interface IAuditRepository
{
    /// <summary>
    /// Inserts an audit log entry by calling the <c>GENERALLOG_INSERT</c> stored procedure
    /// with the exact parameter names used by the legacy Windows Service implementations.
    /// </summary>
    /// <param name="operationType">
    /// Operation type string (e.g. "Post To InvitedClubs", "Feed Download").
    /// </param>
    /// <param name="sourceObject">
    /// Source object identifier (e.g. "Contents", "InvitedClub").
    /// </param>
    /// <param name="userId">User ID performing the operation.</param>
    /// <param name="comments">Free-text comment describing the operation outcome.</param>
    /// <param name="itemId">The <c>ItemId</c> of the workitem being logged.</param>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddGeneralLogAsync(
        string operationType,
        string sourceObject,
        int userId,
        string comments,
        long itemId,
        string connectionString,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts a row into <c>generic_post_history</c> for every workitem processed.
    /// Called by the Core_Engine after each workitem completes (success or failure).
    /// </summary>
    Task SavePostHistoryAsync(
        GenericPostHistory history,
        string connectionString,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts a row into <c>generic_execution_history</c> after each execution run.
    /// Called by <c>AutoPostOrchestrator.ExecutePostBatchAsync</c> in the <c>finally</c> block.
    /// </summary>
    Task SaveExecutionHistoryAsync(
        GenericExecutionHistory history,
        string connectionString,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves execution history rows for the Status API endpoint.
    /// </summary>
    /// <param name="executionId">Primary key of the execution history row to retrieve.</param>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<GenericExecutionHistory?> GetExecutionHistoryAsync(
        long executionId,
        string connectionString,
        CancellationToken ct = default);
}
