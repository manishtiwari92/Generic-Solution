using System.Data;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using Microsoft.Data.SqlClient;

namespace IPS.AutoPost.Core.DataAccess;

/// <summary>
/// Handles all audit logging operations using <see cref="SqlHelper"/>:
/// general log entries via <c>GENERALLOG_INSERT</c>, post history inserts into
/// <c>generic_post_history</c>, and execution history inserts into
/// <c>generic_execution_history</c>.
/// </summary>
public class AuditRepository : IAuditRepository
{
    // -----------------------------------------------------------------------
    // AddGeneralLogAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Calls <c>GENERALLOG_INSERT</c> with the exact parameter names and types
    /// used by all legacy Windows Service implementations to ensure backward
    /// compatibility with the existing stored procedure signature.
    /// </remarks>
    public async Task AddGeneralLogAsync(
        string operationType,
        string sourceObject,
        int userId,
        string comments,
        long itemId,
        string connectionString,
        CancellationToken ct = default)
    {
        await SqlHelper.ExecuteNonQueryAsync(
            connectionString,
            "GENERALLOG_INSERT",
            ct,
            SqlHelper.Param("@operationType", SqlDbType.VarChar,  operationType, size: 100),
            SqlHelper.Param("@sourceObject",  SqlDbType.VarChar,  sourceObject,  size: 100),
            SqlHelper.Param("@userID",        SqlDbType.Int,      userId),
            SqlHelper.Param("@comments",      SqlDbType.VarChar,  comments,      size: 2000),
            SqlHelper.Param("@itemID",        SqlDbType.BigInt,   itemId));
    }

    // -----------------------------------------------------------------------
    // SavePostHistoryAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Inserts a row into <c>generic_post_history</c> for every workitem processed.
    /// The <c>id</c> column is an IDENTITY — it is not included in the INSERT.
    /// </remarks>
    public async Task SavePostHistoryAsync(
        GenericPostHistory history,
        string connectionString,
        CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO generic_post_history
                (job_config_id, client_type, job_id, item_id, step_name,
                 post_request, post_response, post_date, posted_by, manually_posted)
            VALUES
                (@JobConfigId, @ClientType, @JobId, @ItemId, @StepName,
                 @PostRequest, @PostResponse, @PostDate, @PostedBy, @ManuallyPosted)
            """;

        await SqlHelper.ExecuteNonQueryAsync(
            connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@JobConfigId",    SqlDbType.Int,      history.JobConfigId),
            SqlHelper.Param("@ClientType",     SqlDbType.VarChar,  history.ClientType,    size: 50),
            SqlHelper.Param("@JobId",          SqlDbType.Int,      history.JobId),
            SqlHelper.Param("@ItemId",         SqlDbType.BigInt,   history.ItemId),
            SqlHelper.Param("@StepName",       SqlDbType.VarChar,  history.StepName,      size: 100),
            SqlHelper.Param("@PostRequest",    SqlDbType.NVarChar, history.PostRequest,   size: -1),  // MAX
            SqlHelper.Param("@PostResponse",   SqlDbType.NVarChar, history.PostResponse,  size: -1),  // MAX
            SqlHelper.Param("@PostDate",       SqlDbType.DateTime2, history.PostDate),
            SqlHelper.Param("@PostedBy",       SqlDbType.Int,      history.PostedBy),
            SqlHelper.Param("@ManuallyPosted", SqlDbType.Bit,      history.ManuallyPosted));
    }

    // -----------------------------------------------------------------------
    // SaveExecutionHistoryAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Inserts a row into <c>generic_execution_history</c> after each execution run.
    /// Called from <c>AutoPostOrchestrator.ExecutePostBatchAsync</c> in the
    /// <c>finally</c> block to guarantee the record is always written.
    /// <c>duration_seconds</c> is computed from <c>end_time - start_time</c> in the
    /// model's computed property and stored explicitly for query convenience.
    /// </remarks>
    public async Task SaveExecutionHistoryAsync(
        GenericExecutionHistory history,
        string connectionString,
        CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO generic_execution_history
                (job_config_id, client_type, job_id, execution_type, trigger_type,
                 status, records_processed, records_succeeded, records_failed,
                 start_time, end_time, duration_seconds, error_details)
            VALUES
                (@JobConfigId, @ClientType, @JobId, @ExecutionType, @TriggerType,
                 @Status, @RecordsProcessed, @RecordsSucceeded, @RecordsFailed,
                 @StartTime, @EndTime, @DurationSeconds, @ErrorDetails)
            """;

        await SqlHelper.ExecuteNonQueryAsync(
            connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@JobConfigId",       SqlDbType.Int,      history.JobConfigId),
            SqlHelper.Param("@ClientType",        SqlDbType.VarChar,  history.ClientType,       size: 50),
            SqlHelper.Param("@JobId",             SqlDbType.Int,      history.JobId),
            SqlHelper.Param("@ExecutionType",     SqlDbType.VarChar,  history.ExecutionType,    size: 20),
            SqlHelper.Param("@TriggerType",       SqlDbType.VarChar,  history.TriggerType,      size: 20),
            SqlHelper.Param("@Status",            SqlDbType.VarChar,  history.Status,           size: 30),
            SqlHelper.Param("@RecordsProcessed",  SqlDbType.Int,      history.RecordsProcessed),
            SqlHelper.Param("@RecordsSucceeded",  SqlDbType.Int,      history.RecordsSucceeded),
            SqlHelper.Param("@RecordsFailed",     SqlDbType.Int,      history.RecordsFailed),
            SqlHelper.Param("@StartTime",         SqlDbType.DateTime2, history.StartTime),
            SqlHelper.Param("@EndTime",           SqlDbType.DateTime2, history.EndTime),
            SqlHelper.Param("@DurationSeconds",   SqlDbType.Float,    history.DurationSeconds),
            SqlHelper.Param("@ErrorDetails",      SqlDbType.NVarChar, history.ErrorDetails,     size: -1));  // MAX
    }

    // -----------------------------------------------------------------------
    // GetExecutionHistoryAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<GenericExecutionHistory?> GetExecutionHistoryAsync(
        long executionId,
        string connectionString,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                id,
                job_config_id,
                client_type,
                job_id,
                execution_type,
                trigger_type,
                status,
                records_processed,
                records_succeeded,
                records_failed,
                start_time,
                end_time,
                duration_seconds,
                error_details
            FROM generic_execution_history
            WHERE id = @Id
            """;

        var ds = await SqlHelper.ExecuteDatasetAsync(
            connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@Id", SqlDbType.BigInt, executionId));

        if (ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            return null;

        var row = ds.Tables[0].Rows[0];
        return new GenericExecutionHistory
        {
            Id               = Convert.ToInt64(row["id"]),
            JobConfigId      = Convert.ToInt32(row["job_config_id"]),
            ClientType       = row["client_type"]      as string ?? string.Empty,
            JobId            = Convert.ToInt32(row["job_id"]),
            ExecutionType    = row["execution_type"]   as string ?? string.Empty,
            TriggerType      = row["trigger_type"]     as string ?? string.Empty,
            Status           = row["status"]           as string ?? string.Empty,
            RecordsProcessed = Convert.ToInt32(row["records_processed"]),
            RecordsSucceeded = Convert.ToInt32(row["records_succeeded"]),
            RecordsFailed    = Convert.ToInt32(row["records_failed"]),
            StartTime        = Convert.ToDateTime(row["start_time"]),
            EndTime          = Convert.ToDateTime(row["end_time"]),
            ErrorDetails     = row["error_details"] == DBNull.Value
                                   ? null
                                   : row["error_details"] as string
        };
    }
}
