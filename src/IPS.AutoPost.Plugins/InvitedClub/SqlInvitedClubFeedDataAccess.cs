using System.Data;
using IPS.AutoPost.Core.DataAccess;
using IPS.AutoPost.Core.Extensions;
using IPS.AutoPost.Plugins.InvitedClub.Constants;
using Microsoft.Data.SqlClient;

namespace IPS.AutoPost.Plugins.InvitedClub;

/// <summary>
/// Production implementation of <see cref="IInvitedClubFeedDataAccess"/> that
/// delegates to <see cref="SqlHelper"/> static methods.
/// </summary>
public sealed class SqlInvitedClubFeedDataAccess : IInvitedClubFeedDataAccess
{
    private readonly string _connectionString;

    public SqlInvitedClubFeedDataAccess(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<int> GetTableCountAsync(string tableName, CancellationToken ct)
    {
        var result = await SqlHelper.ExecuteScalarAsync(
            _connectionString,
            CommandType.Text,
            $"SELECT COUNT(*) FROM {tableName}",
            ct);

        return result is null ? 0 : Convert.ToInt32(result);
    }

    public async Task TruncateTableAsync(string tableName, CancellationToken ct)
    {
        await SqlHelper.ExecuteNonQueryAsync(
            _connectionString,
            CommandType.Text,
            $"TRUNCATE TABLE {tableName}",
            ct);
    }

    public async Task DeleteBySupplierIdsAsync(string tableName, IEnumerable<string> supplierIds, CancellationToken ct)
    {
        var ids = string.Join(",", supplierIds.Select(id => $"'{id.Replace("'", "''")}'"));
        await SqlHelper.ExecuteNonQueryAsync(
            _connectionString,
            CommandType.Text,
            $"DELETE FROM {tableName} WHERE SupplierId IN ({ids})",
            ct);
    }

    public async Task BulkCopyAsync(string tableName, DataTable dataTable, CancellationToken ct)
    {
        await SqlHelper.BulkCopyAsync(_connectionString, tableName, dataTable, ct: ct);
    }

    public async Task ExecuteNonQuerySpAsync(string spName, CancellationToken ct)
    {
        await SqlHelper.ExecuteNonQueryAsync(_connectionString, spName, ct);
    }

    public async Task ExecuteUpdateLastDownloadTimeAsync(int configurationsId, CancellationToken ct)
    {
        await SqlHelper.ExecuteNonQueryAsync(
            _connectionString,
            InvitedClubConstants.SpUpdateSupplierLastDownloadTime,
            ct,
            SqlHelper.Param("@configurations_id", SqlDbType.Int, configurationsId));
    }

    public async Task<DataSet> GetSupplierDataToExportAsync(CancellationToken ct)
    {
        return await SqlHelper.ExecuteDatasetAsync(
            _connectionString,
            InvitedClubConstants.SpGetSupplierDataToExport,
            ct);
    }

    public async Task<DataTable> GetEmailConfigAsync(int configId, CancellationToken ct)
    {
        var ds = await SqlHelper.ExecuteDatasetAsync(
            _connectionString,
            InvitedClubConstants.SpGetEmailConfigPerJob,
            ct,
            SqlHelper.Param("@ConfigId", SqlDbType.Int, configId));

        return ds.Tables.Count > 0 ? ds.Tables[0] : new DataTable();
    }

    public async Task<List<string>> GetMissingCOAIdsAsync(CancellationToken ct)
    {
        var ds = await SqlHelper.ExecuteDatasetAsync(
            _connectionString,
            CommandType.Text,
            $"SELECT CodeCombinationId FROM {InvitedClubConstants.CoaFullFeedTableName} " +
            $"WHERE CodeCombinationId NOT IN (SELECT CodeCombinationId FROM {InvitedClubConstants.CoaTableName})",
            ct);

        if (ds.IsEmpty())
            return new List<string>();

        return ds.Tables[0].Rows
            .Cast<DataRow>()
            .Select(r => r[0]?.ToString() ?? string.Empty)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();
    }
}
