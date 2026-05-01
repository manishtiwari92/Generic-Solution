using System.Data;
using IPS.AutoPost.Core.DataAccess;
using IPS.AutoPost.Plugins.InvitedClub.Constants;
using Microsoft.Data.SqlClient;

namespace IPS.AutoPost.Plugins.InvitedClub;

/// <summary>
/// Production implementation of <see cref="IInvitedClubRetryDataAccess"/> that
/// delegates to <see cref="SqlHelper"/> static methods.
/// </summary>
public sealed class SqlInvitedClubRetryDataAccess : IInvitedClubRetryDataAccess
{
    private readonly string _connectionString;

    public SqlInvitedClubRetryDataAccess(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <inheritdoc/>
    public async Task<DataTable> GetFailedImagesDataAsync(
        string headerTable,
        int imagePostRetryLimit,
        int invitedFailPostQueueId,
        CancellationToken ct)
    {
        var ds = await SqlHelper.ExecuteDatasetAsync(
            _connectionString,
            InvitedClubConstants.SpGetFailedImagesData,
            ct,
            SqlHelper.Param("@HeaderTable",           SqlDbType.VarChar, headerTable,           size: 100),
            SqlHelper.Param("@ImagePostRetryLimit",    SqlDbType.Int,     imagePostRetryLimit),
            SqlHelper.Param("@InvitedFailPostQueueId", SqlDbType.BigInt,  invitedFailPostQueueId));

        return ds.Tables.Count > 0 ? ds.Tables[0] : new DataTable();
    }

    /// <inheritdoc/>
    public async Task UpdateAttachedDocumentIdAsync(
        long itemId,
        string attachedDocumentId,
        string headerTable,
        CancellationToken ct)
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
    public async Task IncrementImagePostRetryCountAsync(
        long itemId,
        string headerTable,
        CancellationToken ct)
    {
        var sql = $"UPDATE {headerTable} SET ImagePostRetryCount = ISNULL(ImagePostRetryCount, 0) + 1 WHERE UID = @UID";

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
}
