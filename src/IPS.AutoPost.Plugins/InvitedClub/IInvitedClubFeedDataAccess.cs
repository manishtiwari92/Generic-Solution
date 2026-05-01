using System.Data;

namespace IPS.AutoPost.Plugins.InvitedClub;

/// <summary>
/// Abstracts all database operations used by <see cref="InvitedClubFeedStrategy"/>
/// so that unit tests can mock them without a real SQL Server connection.
/// </summary>
public interface IInvitedClubFeedDataAccess
{
    /// <summary>Returns the row count of the specified table.</summary>
    Task<int> GetTableCountAsync(string tableName, CancellationToken ct);

    /// <summary>Truncates the specified table.</summary>
    Task TruncateTableAsync(string tableName, CancellationToken ct);

    /// <summary>
    /// Deletes rows from the specified table where SupplierId is in the provided list.
    /// </summary>
    Task DeleteBySupplierIdsAsync(string tableName, IEnumerable<string> supplierIds, CancellationToken ct);

    /// <summary>Bulk-copies a DataTable into the specified destination table.</summary>
    Task BulkCopyAsync(string tableName, DataTable dataTable, CancellationToken ct);

    /// <summary>Executes a stored procedure with no parameters and no result set.</summary>
    Task ExecuteNonQuerySpAsync(string spName, CancellationToken ct);

    /// <summary>
    /// Executes a stored procedure with a single @configurations_id parameter.
    /// Used by <c>UpdateSupplierLastDownloadTime</c>.
    /// </summary>
    Task ExecuteUpdateLastDownloadTimeAsync(int configurationsId, CancellationToken ct);

    /// <summary>
    /// Executes <c>InvitedClub_GetSupplierDataToExport</c> and returns the result DataSet.
    /// </summary>
    Task<DataSet> GetSupplierDataToExportAsync(CancellationToken ct);

    /// <summary>
    /// Loads email configuration for the given config ID via
    /// <c>GetInvitedClubsEmailConfigPerJob</c>.
    /// </summary>
    Task<DataTable> GetEmailConfigAsync(int configId, CancellationToken ct);

    /// <summary>
    /// Returns CodeCombinationIds present in <c>InvitedClubsCOAFullFeed</c>
    /// but missing from <c>InvitedClubCOA</c>.
    /// </summary>
    Task<List<string>> GetMissingCOAIdsAsync(CancellationToken ct);
}
