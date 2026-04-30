using IPS.AutoPost.Core.Models;

namespace IPS.AutoPost.Core.Interfaces;

/// <summary>
/// Handles workitem queue routing and the <c>PostInProcess</c> flag management.
/// Wraps the <c>WORKITEM_ROUTE</c> stored procedure and direct SQL UPDATE operations.
/// </summary>
public interface IRoutingRepository
{
    /// <summary>
    /// Routes a workitem to a target queue by calling the <c>WORKITEM_ROUTE</c>
    /// stored procedure with the exact parameter names and types used by the
    /// legacy Windows Service implementations.
    /// </summary>
    /// <param name="itemId">The <c>ItemId</c> of the workitem to route.</param>
    /// <param name="targetQueueId">The destination <c>StatusId</c> (queue position).</param>
    /// <param name="userId">User ID to record in the routing audit trail.</param>
    /// <param name="operationType">
    /// Operation type string for the audit log (e.g. "Post To InvitedClubs").
    /// </param>
    /// <param name="comment">
    /// Comment to store in the <c>question</c> field of the workitem routing record.
    /// Used for failure reasons and routing notes.
    /// </param>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RouteWorkitemAsync(
        long itemId,
        int targetQueueId,
        int userId,
        string operationType,
        string comment,
        string connectionString,
        CancellationToken ct = default);

    /// <summary>
    /// Sets <c>PostInProcess = 1</c> on the client-specific index header table row
    /// before the plugin calls the external ERP API.
    /// Prevents concurrent duplicate posting when multiple worker tasks are running.
    /// </summary>
    /// <param name="itemId">The <c>UID</c> of the header row to lock.</param>
    /// <param name="headerTable">
    /// Name of the client-specific header table (e.g. "WFInvitedClubsIndexHeader").
    /// Sourced from the trusted <c>generic_job_configuration</c> table.
    /// </param>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetPostInProcessAsync(
        long itemId,
        string headerTable,
        string connectionString,
        CancellationToken ct = default);

    /// <summary>
    /// Clears <c>PostInProcess = 0</c> on the client-specific index header table row.
    /// Always called from a <c>finally</c> block to ensure the flag is cleared
    /// regardless of success or failure.
    /// Used as the default implementation of
    /// <see cref="IClientPlugin.ClearPostInProcessAsync"/>.
    /// </summary>
    /// <param name="itemId">The <c>UID</c> of the header row to unlock.</param>
    /// <param name="headerTable">
    /// Name of the client-specific header table (e.g. "WFInvitedClubsIndexHeader").
    /// Sourced from the trusted <c>generic_job_configuration</c> table.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task ClearPostInProcessAsync(
        long itemId,
        string headerTable,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a client-specific stored procedure for workitem routing or flag management.
    /// Used by plugins that override the default routing behaviour
    /// (e.g. Sevita calls <c>UpdateSevitaHeaderPostFields(@UID)</c>).
    /// </summary>
    /// <param name="storedProcedureName">Name of the stored procedure to execute.</param>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="parameters">Parameters to pass to the stored procedure.</param>
    Task ExecuteSpAsync(
        string storedProcedureName,
        string connectionString,
        CancellationToken ct = default,
        params Microsoft.Data.SqlClient.SqlParameter[] parameters);
}
