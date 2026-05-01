using System.Data;
using IPS.AutoPost.Core.DataAccess;
using IPS.AutoPost.Core.Extensions;
using IPS.AutoPost.Plugins.Sevita.Constants;
using IPS.AutoPost.Plugins.Sevita.Models;
using Microsoft.Data.SqlClient;

namespace IPS.AutoPost.Plugins.Sevita;

/// <summary>
/// Production implementation of <see cref="ISevitaPostDataAccess"/> that
/// delegates to <see cref="SqlHelper"/> static methods.
/// </summary>
public sealed class SqlSevitaPostDataAccess : ISevitaPostDataAccess
{
    private readonly string _connectionString;

    public SqlSevitaPostDataAccess(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc/>
    public async Task<DataSet> GetHeaderAndDetailDataAsync(long itemId, CancellationToken ct)
    {
        return await SqlHelper.ExecuteDatasetAsync(
            _connectionString,
            SevitaConstants.SpGetHeaderAndDetailData,
            ct,
            SqlHelper.Param("@UID", SqlDbType.BigInt, itemId));
    }

    /// <inheritdoc/>
    public async Task<List<PostResponseType>> GetApiResponseTypesAsync(int jobId, CancellationToken ct)
    {
        var ds = await SqlHelper.ExecuteDatasetAsync(
            _connectionString,
            CommandType.Text,
            "SELECT response_type AS ResponseType, response_code AS ResponseCode, response_message AS ResponseMessage " +
            "FROM api_response_configuration WHERE job_id = @JobId",
            ct,
            SqlHelper.Param("@JobId", SqlDbType.Int, jobId));

        return ds.IsEmpty() ? new List<PostResponseType>() : ds.Tables[0].ConvertDataTable<PostResponseType>();
    }

    /// <inheritdoc/>
    public async Task SetPostInProcessAsync(long itemId, string headerTable, CancellationToken ct)
    {
        var sql = $"UPDATE {headerTable} SET PostInProcess = 1 WHERE UID = @UID";
        await SqlHelper.ExecuteNonQueryAsync(
            _connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@UID", SqlDbType.BigInt, itemId));
    }

    /// <inheritdoc/>
    public async Task ClearPostInProcessAsync(long itemId, string headerTable, CancellationToken ct)
    {
        var sql = $"UPDATE {headerTable} SET PostInProcess = 0 WHERE UID = @UID";
        await SqlHelper.ExecuteNonQueryAsync(
            _connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@UID", SqlDbType.BigInt, itemId));
    }

    /// <inheritdoc/>
    public async Task RouteWorkitemAsync(
        long itemId,
        int targetQueueId,
        int userId,
        string operationType,
        string comment,
        CancellationToken ct)
    {
        await SqlHelper.ExecuteNonQueryAsync(
            _connectionString,
            SevitaConstants.SpWorkitemRoute,
            ct,
            SqlHelper.Param("@itemID",        SqlDbType.BigInt,  itemId),
            SqlHelper.Param("@Qid",           SqlDbType.Int,     targetQueueId),
            SqlHelper.Param("@userId",        SqlDbType.Int,     userId),
            SqlHelper.Param("@operationType", SqlDbType.VarChar, operationType, size: 100),
            SqlHelper.Param("@comment",       SqlDbType.VarChar, comment,       size: 500));
    }

    /// <inheritdoc/>
    public async Task InsertGeneralLogAsync(
        string operationType,
        string sourceObject,
        int userId,
        string comments,
        long itemId,
        CancellationToken ct)
    {
        await SqlHelper.ExecuteNonQueryAsync(
            _connectionString,
            SevitaConstants.SpGeneralLogInsert,
            ct,
            SqlHelper.Param("@operationType", SqlDbType.VarChar, operationType, size: 100),
            SqlHelper.Param("@sourceObject",  SqlDbType.VarChar, sourceObject,  size: 100),
            SqlHelper.Param("@userID",        SqlDbType.Int,     userId),
            SqlHelper.Param("@comments",      SqlDbType.VarChar, comments,      size: 2000),
            SqlHelper.Param("@itemID",        SqlDbType.BigInt,  itemId));
    }

    /// <inheritdoc/>
    public async Task SavePostHistoryAsync(PostHistory history, CancellationToken ct)
    {
        const string sql =
            "INSERT INTO sevita_posted_records_history " +
            "(item_id, post_request, post_response, manually_posted, posted_by, Comment) " +
            "VALUES " +
            "(@ItemId, @InvoiceRequestJson, @InvoiceResponseJson, @ManuallyPosted, @PostedBy, @Comment)";

        await SqlHelper.ExecuteNonQueryAsync(
            _connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@ItemId",             SqlDbType.BigInt,   history.ItemId),
            SqlHelper.Param("@InvoiceRequestJson",  SqlDbType.NVarChar, history.InvoiceRequestJson),
            SqlHelper.Param("@InvoiceResponseJson", SqlDbType.NVarChar, history.InvoiceResponseJson),
            SqlHelper.Param("@ManuallyPosted",      SqlDbType.Bit,      history.ManuallyPosted),
            SqlHelper.Param("@PostedBy",            SqlDbType.Int,      history.PostedBy),
            SqlHelper.Param("@Comment",             SqlDbType.NVarChar, history.Comment));
    }

    /// <inheritdoc/>
    public async Task UpdateHeaderPostFieldsAsync(long itemId, CancellationToken ct)
    {
        await SqlHelper.ExecuteNonQueryAsync(
            _connectionString,
            SevitaConstants.SpUpdateHeaderPostFields,
            ct,
            SqlHelper.Param("@UID", SqlDbType.BigInt, itemId));
    }
}
