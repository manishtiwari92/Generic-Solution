using System.Data;
using IPS.AutoPost.Core.Interfaces;
using Microsoft.Data.SqlClient;

namespace IPS.AutoPost.Core.DataAccess;

/// <summary>
/// Handles workitem queue routing and <c>PostInProcess</c> flag management
/// using <see cref="SqlHelper"/>.
/// Wraps the <c>WORKITEM_ROUTE</c> stored procedure and direct SQL UPDATE operations.
/// </summary>
public class RoutingRepository : IRoutingRepository
{
    // -----------------------------------------------------------------------
    // RouteWorkitemAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <c>WORKITEM_ROUTE</c> with the exact parameter names and types
    /// used by all legacy Windows Service implementations to ensure backward
    /// compatibility with the existing stored procedure signature.
    /// </remarks>
    public async Task RouteWorkitemAsync(
        long itemId,
        int targetQueueId,
        int userId,
        string operationType,
        string comment,
        string connectionString,
        CancellationToken ct = default)
    {
        await SqlHelper.ExecuteNonQueryAsync(
            connectionString,
            "WORKITEM_ROUTE",
            ct,
            SqlHelper.Param("@itemID",        SqlDbType.BigInt,   itemId),
            SqlHelper.Param("@Qid",           SqlDbType.Int,      targetQueueId),
            SqlHelper.Param("@userId",        SqlDbType.Int,      userId),
            SqlHelper.Param("@operationType", SqlDbType.VarChar,  operationType, size: 100),
            SqlHelper.Param("@comment",       SqlDbType.VarChar,  comment,       size: 500));
    }

    // -----------------------------------------------------------------------
    // SetPostInProcessAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// The <paramref name="headerTable"/> value comes exclusively from the trusted
    /// <c>generic_job_configuration</c> table — it is never user-supplied.
    /// </remarks>
    public async Task SetPostInProcessAsync(
        long itemId,
        string headerTable,
        string connectionString,
        CancellationToken ct = default)
    {
        var sql = $"UPDATE {headerTable} SET PostInProcess = 1 WHERE UID = @UID";

        await SqlHelper.ExecuteNonQueryAsync(
            connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@UID", SqlDbType.BigInt, itemId));
    }

    // -----------------------------------------------------------------------
    // ClearPostInProcessAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Always called from a <c>finally</c> block to guarantee the flag is cleared
    /// regardless of success or failure. The <paramref name="headerTable"/> value
    /// comes exclusively from the trusted <c>generic_job_configuration</c> table.
    /// </remarks>
    public async Task ClearPostInProcessAsync(
        long itemId,
        string headerTable,
        CancellationToken ct = default)
    {
        // ClearPostInProcessAsync does not accept a connectionString because it is
        // called from IClientPlugin.ClearPostInProcessAsync which only has access
        // to the routing repository. The connection string must be resolved from
        // the config that was passed to the plugin. Plugins that need a connection
        // string should use ExecuteSpAsync or call SetPostInProcessAsync directly.
        //
        // This default implementation is a no-op placeholder — the actual connection
        // string is provided by the plugin via the overload below.
        throw new NotSupportedException(
            "Use ClearPostInProcessAsync(long itemId, string headerTable, string connectionString, CancellationToken ct) instead.");
    }

    /// <summary>
    /// Clears <c>PostInProcess = 0</c> on the client-specific index header table row.
    /// This overload accepts an explicit connection string and is the implementation
    /// used by the default <see cref="Interfaces.IClientPlugin.ClearPostInProcessAsync"/> path.
    /// </summary>
    public async Task ClearPostInProcessAsync(
        long itemId,
        string headerTable,
        string connectionString,
        CancellationToken ct = default)
    {
        var sql = $"UPDATE {headerTable} SET PostInProcess = 0 WHERE UID = @UID";

        await SqlHelper.ExecuteNonQueryAsync(
            connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@UID", SqlDbType.BigInt, itemId));
    }

    // -----------------------------------------------------------------------
    // ExecuteSpAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Used by plugins that override the default routing behaviour.
    /// Example: Sevita calls <c>UpdateSevitaHeaderPostFields(@UID)</c> instead of
    /// a direct SQL UPDATE to clear <c>PostInProcess</c>.
    /// </remarks>
    public async Task ExecuteSpAsync(
        string storedProcedureName,
        string connectionString,
        CancellationToken ct = default,
        params SqlParameter[] parameters)
    {
        await SqlHelper.ExecuteNonQueryAsync(
            connectionString,
            storedProcedureName,
            ct,
            parameters);
    }
}
