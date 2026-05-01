using System.Data;
using IPS.AutoPost.Core.DataAccess;
using IPS.AutoPost.Core.Extensions;
using IPS.AutoPost.Plugins.InvitedClub.Constants;
using IPS.AutoPost.Plugins.InvitedClub.Models;
using Microsoft.Data.SqlClient;

namespace IPS.AutoPost.Plugins.InvitedClub;

/// <summary>
/// Production implementation of <see cref="IInvitedClubPostDataAccess"/> that
/// delegates to <see cref="SqlHelper"/> static methods.
/// </summary>
public sealed class SqlInvitedClubPostDataAccess : IInvitedClubPostDataAccess
{
    private readonly string _connectionString;

    public SqlInvitedClubPostDataAccess(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc/>
    public async Task<DataSet> GetHeaderAndDetailDataAsync(long itemId, CancellationToken ct)
    {
        return await SqlHelper.ExecuteDatasetAsync(
            _connectionString,
            InvitedClubConstants.SpGetHeaderAndDetailData,
            ct,
            SqlHelper.Param("@UID", SqlDbType.BigInt, itemId));
    }

    /// <inheritdoc/>
    public async Task<DataTable> GetEmailConfigAsync(int configId, CancellationToken ct)
    {
        var ds = await SqlHelper.ExecuteDatasetAsync(
            _connectionString,
            InvitedClubConstants.SpGetEmailConfigPerJob,
            ct,
            SqlHelper.Param("@ConfigId", SqlDbType.Int, configId));

        return ds.Tables.Count > 0 ? ds.Tables[0] : new DataTable();
    }

    /// <inheritdoc/>
    public async Task<List<APIResponseType>> GetApiResponseTypesAsync(int jobId, CancellationToken ct)
    {
        var ds = await SqlHelper.ExecuteDatasetAsync(
            _connectionString,
            CommandType.Text,
            "SELECT response_type AS ResponseType, response_code AS ResponseCode, response_message AS ResponseMessage " +
            "FROM api_response_configuration WHERE job_id = @JobId",
            ct,
            SqlHelper.Param("@JobId", SqlDbType.Int, jobId));

        return ds.IsEmpty() ? new List<APIResponseType>() : ds.Tables[0].ConvertDataTable<APIResponseType>();
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
    public async Task UpdateInvoiceIdAsync(long itemId, string invoiceId, string headerTable, CancellationToken ct)
    {
        var sql = $"UPDATE {headerTable} SET InvoiceId = @InvoiceId WHERE UID = @UID";
        await SqlHelper.ExecuteNonQueryAsync(
            _connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@InvoiceId", SqlDbType.VarChar, invoiceId, size: 200),
            SqlHelper.Param("@UID",       SqlDbType.BigInt,  itemId));
    }

    /// <inheritdoc/>
    public async Task UpdateAttachedDocumentIdAsync(long itemId, string attachedDocumentId, string headerTable, CancellationToken ct)
    {
        var sql = $"UPDATE {headerTable} SET AttachedDocumentId = @AttachedDocumentId WHERE UID = @UID";
        await SqlHelper.ExecuteNonQueryAsync(
            _connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@AttachedDocumentId", SqlDbType.VarChar, attachedDocumentId, size: 200),
            SqlHelper.Param("@UID",                SqlDbType.BigInt,  itemId));
    }

    /// <inheritdoc/>
    public async Task UpdateGlDateValueAsync(long itemId, string headerTable, CancellationToken ct)
    {
        var sql = $"UPDATE {headerTable} SET GlDate = NULL WHERE UID = @UID";
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
            InvitedClubConstants.SpWorkitemRoute,
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
            InvitedClubConstants.SpGeneralLogInsert,
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
            "INSERT INTO post_to_invitedclub_history " +
            "(item_id, invoice_request_json, invoice_response_json, " +
            " attachment_request_json, attachment_response_json, " +
            " calculate_tax_request_json, calculate_tax_response_json, " +
            " manually_posted, posted_by) " +
            "VALUES " +
            "(@ItemId, @InvoiceRequestJson, @InvoiceResponseJson, " +
            " @AttachmentRequestJson, @AttachmentResponseJson, " +
            " @CalculateTaxRequestJson, @CalculateTaxResponseJson, " +
            " @ManuallyPosted, @PostedBy)";

        await SqlHelper.ExecuteNonQueryAsync(
            _connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@ItemId",                  SqlDbType.BigInt,   history.ItemId),
            SqlHelper.Param("@InvoiceRequestJson",       SqlDbType.NVarChar, history.InvoiceRequestJson),
            SqlHelper.Param("@InvoiceResponseJson",      SqlDbType.NVarChar, history.InvoiceResponseJson),
            SqlHelper.Param("@AttachmentRequestJson",    SqlDbType.NVarChar, history.AttachmentRequestJson),
            SqlHelper.Param("@AttachmentResponseJson",   SqlDbType.NVarChar, history.AttachmentResponseJson),
            SqlHelper.Param("@CalculateTaxRequestJson",  SqlDbType.NVarChar, history.CalculateTaxRequestJson),
            SqlHelper.Param("@CalculateTaxResponseJson", SqlDbType.NVarChar, history.CalculateTaxResponseJson),
            SqlHelper.Param("@ManuallyPosted",           SqlDbType.Bit,      history.ManuallyPosted),
            SqlHelper.Param("@PostedBy",                 SqlDbType.Int,      history.PostedBy));
    }
}
