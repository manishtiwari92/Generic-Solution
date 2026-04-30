using System.Data;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace IPS.AutoPost.Core.DataAccess;

/// <summary>
/// Fetches workitem data from the <c>Workitems</c> table and client-specific
/// index header/detail tables using <see cref="SqlHelper"/>.
/// </summary>
public class WorkitemRepository : IWorkitemRepository
{
    private readonly string _connectionString;

    public WorkitemRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Workflow")
            ?? throw new InvalidOperationException(
                "Connection string 'Workflow' is not configured. " +
                "Ensure SecretsManagerConfigurationProvider has resolved the /IPS/Common/{env}/Database/Workflow secret.");
    }

    // -----------------------------------------------------------------------
    // GetWorkitemsAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// The <c>ISNULL(h.PostInProcess, 0) = 0</c> predicate prevents re-processing
    /// workitems that are already locked by another worker task.
    /// Table names and queue IDs come from the trusted <c>generic_job_configuration</c>
    /// table — they are not user-supplied values.
    /// </remarks>
    public async Task<IReadOnlyList<WorkitemData>> GetWorkitemsAsync(
        GenericJobConfig config,
        CancellationToken ct = default)
    {
        // Build the IN clause from the comma-separated source_queue_id value.
        // Values originate from the trusted generic_job_configuration table.
        var sql = $"""
            SELECT w.ItemId, w.StatusId
            FROM   Workitems w
            JOIN   {config.HeaderTable} h ON w.ItemId = h.UID
            WHERE  w.JobId    = @JobId
              AND  w.StatusId IN ({config.SourceQueueId})
              AND  ISNULL(h.PostInProcess, 0) = 0
            """;

        // Use the per-job connection string when available; fall back to the
        // default Workflow connection string injected at construction time.
        var connStr = string.IsNullOrWhiteSpace(config.DbConnectionString)
            ? _connectionString
            : config.DbConnectionString;

        var ds = await SqlHelper.ExecuteDatasetAsync(
            connStr,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@JobId", SqlDbType.Int, config.JobId));

        if (ds.Tables.Count == 0)
            return Array.Empty<WorkitemData>();

        var results = new List<WorkitemData>(ds.Tables[0].Rows.Count);
        foreach (DataRow row in ds.Tables[0].Rows)
        {
            results.Add(new WorkitemData
            {
                ItemId   = Convert.ToInt64(row["ItemId"]),
                StatusId = Convert.ToInt32(row["StatusId"])
            });
        }

        return results;
    }

    // -----------------------------------------------------------------------
    // GetWorkitemDataAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Returns a two-table DataSet: Table[0] = header row, Table[1] = detail rows.
    /// Table names come from the trusted <c>generic_job_configuration</c> table.
    /// </remarks>
    public async Task<DataSet> GetWorkitemDataAsync(
        long itemId,
        GenericJobConfig config,
        CancellationToken ct = default)
    {
        // Fetch header and detail in a single round-trip using two SELECT statements.
        // The detail join column name is stored in config.DetailUidColumn.
        var sql = $"""
            SELECT * FROM {config.HeaderTable} WHERE UID = @ItemId;
            SELECT * FROM {config.DetailTable} WHERE {config.DetailUidColumn} = @ItemId;
            """;

        var connStr = string.IsNullOrWhiteSpace(config.DbConnectionString)
            ? _connectionString
            : config.DbConnectionString;

        return await SqlHelper.ExecuteDatasetAsync(
            connStr,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@ItemId", SqlDbType.BigInt, itemId));
    }

    // -----------------------------------------------------------------------
    // GetWorkitemsByItemIdsAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Uses the existing <c>dbo.split</c> table-valued function to split the
    /// comma-separated <paramref name="itemIds"/> string, matching the legacy
    /// Windows Service implementation exactly.
    /// Uses the default Workflow connection string injected at construction time
    /// because this method is called before configuration is resolved in the
    /// manual post flow.
    /// </remarks>
    public async Task<DataSet> GetWorkitemsByItemIdsAsync(
        string itemIds,
        CancellationToken ct = default)
    {
        // dbo.split is an existing TVF in the Workflow database.
        // It splits a comma-separated string into a table of (items) values.
        // We join against it to avoid dynamic SQL with user-supplied values.
        const string sql = """
            SELECT w.ItemId, w.StatusId, w.JobId
            FROM   Workitems w
            JOIN   dbo.split(@ItemIds, ',') s ON w.ItemId = s.items
            """;

        return await SqlHelper.ExecuteDatasetAsync(
            _connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@ItemIds", SqlDbType.VarChar, itemIds, size: 4000));
    }
}
