using System.Data;
using Microsoft.Data.SqlClient;

namespace IPS.AutoPost.Core.DataAccess;

/// <summary>
/// Async SQL helper that encapsulates all ADO.NET operations for the AutoPost platform.
/// Replaces the 15+ identical synchronous SqlHelper copies from the legacy Windows Service projects.
/// All methods accept a connection string per-call to support multi-tenant scenarios.
/// </summary>
public sealed class SqlHelper
{
    // -----------------------------------------------------------------------
    // Parameter Factory
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a SqlParameter with full control over type, direction, size, and null treatment.
    /// Mirrors the legacy CreatePerameter signature exactly so existing call sites compile unchanged.
    /// </summary>
    /// <param name="paramName">Parameter name including the @ prefix (e.g. "@JobId").</param>
    /// <param name="paramType">SQL Server data type.</param>
    /// <param name="paramValue">Value to bind. Pass null to send DBNull.</param>
    /// <param name="direction">Input, Output, InputOutput, or ReturnValue.</param>
    /// <param name="size">Column size; pass 0 to let SQL Server infer.</param>
    /// <param name="treatForNull">
    /// When true, zero integers, DateTime.MinValue, and empty strings are converted to DBNull
    /// so that SQL Server default values and nullable columns behave correctly.
    /// </param>
    public static SqlParameter Param(
        string paramName,
        SqlDbType paramType,
        object? paramValue,
        ParameterDirection direction = ParameterDirection.Input,
        int size = 0,
        bool treatForNull = false)
    {
        var param = new SqlParameter(paramName, paramType);

        if (size > 0)
            param.Size = size;

        param.Direction = direction;

        if (direction is ParameterDirection.Input or ParameterDirection.InputOutput)
        {
            if (paramValue is null)
            {
                param.Value = DBNull.Value;
            }
            else
            {
                param.Value = paramValue;

                if (treatForNull)
                {
                    param.Value = paramType switch
                    {
                        SqlDbType.BigInt when Convert.ToInt64(paramValue) == 0 => DBNull.Value,
                        SqlDbType.Int or SqlDbType.TinyInt or SqlDbType.SmallInt
                            when Convert.ToInt32(paramValue) == 0 => DBNull.Value,
                        SqlDbType.DateTime or SqlDbType.DateTime2 or SqlDbType.Date
                            when Convert.ToDateTime(paramValue) == DateTime.MinValue => DBNull.Value,
                        SqlDbType.VarChar or SqlDbType.NVarChar or SqlDbType.Char or SqlDbType.NChar
                            when Convert.ToString(paramValue) == string.Empty => DBNull.Value,
                        _ => paramValue
                    };
                }
            }
        }

        return param;
    }

    // -----------------------------------------------------------------------
    // ExecuteDatasetAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Executes a stored procedure and returns a DataSet containing all result sets.
    /// </summary>
    public static async Task<DataSet> ExecuteDatasetAsync(
        string connectionString,
        string storedProcedureName,
        CancellationToken ct = default,
        params SqlParameter[] parameters)
    {
        return await ExecuteDatasetAsync(
            connectionString,
            CommandType.StoredProcedure,
            storedProcedureName,
            ct,
            parameters);
    }

    /// <summary>
    /// Executes a command (stored procedure or ad-hoc SQL) and returns a DataSet.
    /// </summary>
    public static async Task<DataSet> ExecuteDatasetAsync(
        string connectionString,
        CommandType commandType,
        string commandText,
        CancellationToken ct = default,
        params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return await ExecuteDatasetAsync(connection, commandType, commandText, ct, parameters);
    }

    /// <summary>
    /// Executes a command on an existing open connection and returns a DataSet.
    /// </summary>
    public static async Task<DataSet> ExecuteDatasetAsync(
        SqlConnection connection,
        CommandType commandType,
        string commandText,
        CancellationToken ct = default,
        params SqlParameter[] parameters)
    {
        await using var command = BuildCommand(connection, null, commandType, commandText, parameters);
        var ds = new DataSet();
        // SqlDataAdapter does not implement IAsyncDisposable — use a regular using block
        using var adapter = new SqlDataAdapter(command);
        // Fill is synchronous; wrap in Task.Run to avoid blocking the thread pool
        await Task.Run(() => adapter.Fill(ds), ct);
        return ds;
    }

    // -----------------------------------------------------------------------
    // ExecuteNonQueryAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Executes a stored procedure that returns no result set (INSERT/UPDATE/DELETE/SP).
    /// Returns the number of rows affected.
    /// </summary>
    public static async Task<int> ExecuteNonQueryAsync(
        string connectionString,
        string storedProcedureName,
        CancellationToken ct = default,
        params SqlParameter[] parameters)
    {
        return await ExecuteNonQueryAsync(
            connectionString,
            CommandType.StoredProcedure,
            storedProcedureName,
            ct,
            parameters);
    }

    /// <summary>
    /// Executes a command (stored procedure or ad-hoc SQL) that returns no result set.
    /// Returns the number of rows affected.
    /// </summary>
    public static async Task<int> ExecuteNonQueryAsync(
        string connectionString,
        CommandType commandType,
        string commandText,
        CancellationToken ct = default,
        params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return await ExecuteNonQueryAsync(connection, commandType, commandText, ct, parameters);
    }

    /// <summary>
    /// Executes a command on an existing open connection that returns no result set.
    /// </summary>
    public static async Task<int> ExecuteNonQueryAsync(
        SqlConnection connection,
        CommandType commandType,
        string commandText,
        CancellationToken ct = default,
        params SqlParameter[] parameters)
    {
        await using var command = BuildCommand(connection, null, commandType, commandText, parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Executes a command within an existing transaction that returns no result set.
    /// </summary>
    public static async Task<int> ExecuteNonQueryAsync(
        SqlTransaction transaction,
        CommandType commandType,
        string commandText,
        CancellationToken ct = default,
        params SqlParameter[] parameters)
    {
        await using var command = BuildCommand(transaction.Connection!, transaction, commandType, commandText, parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    // -----------------------------------------------------------------------
    // ExecuteScalarAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Executes a stored procedure and returns the first column of the first row.
    /// Returns null if the result set is empty.
    /// </summary>
    public static async Task<object?> ExecuteScalarAsync(
        string connectionString,
        string storedProcedureName,
        CancellationToken ct = default,
        params SqlParameter[] parameters)
    {
        return await ExecuteScalarAsync(
            connectionString,
            CommandType.StoredProcedure,
            storedProcedureName,
            ct,
            parameters);
    }

    /// <summary>
    /// Executes a command (stored procedure or ad-hoc SQL) and returns the first column of the first row.
    /// </summary>
    public static async Task<object?> ExecuteScalarAsync(
        string connectionString,
        CommandType commandType,
        string commandText,
        CancellationToken ct = default,
        params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return await ExecuteScalarAsync(connection, commandType, commandText, ct, parameters);
    }

    /// <summary>
    /// Executes a command on an existing open connection and returns the first column of the first row.
    /// </summary>
    public static async Task<object?> ExecuteScalarAsync(
        SqlConnection connection,
        CommandType commandType,
        string commandText,
        CancellationToken ct = default,
        params SqlParameter[] parameters)
    {
        await using var command = BuildCommand(connection, null, commandType, commandText, parameters);
        return await command.ExecuteScalarAsync(ct);
    }

    // -----------------------------------------------------------------------
    // BulkCopyAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Bulk-inserts all rows from a DataTable into the specified destination table.
    /// Column mappings are built automatically from the DataTable column names.
    /// </summary>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="destinationTable">Target table name (e.g. "InvitedClubSupplier").</param>
    /// <param name="dataTable">Source data. Column names must match destination column names.</param>
    /// <param name="timeoutSeconds">Bulk copy timeout in seconds. Default 600 (10 minutes).</param>
    /// <param name="options">SqlBulkCopyOptions flags. Default KeepIdentity.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task BulkCopyAsync(
        string connectionString,
        string destinationTable,
        DataTable dataTable,
        int timeoutSeconds = 600,
        SqlBulkCopyOptions options = SqlBulkCopyOptions.KeepIdentity,
        CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await BulkCopyAsync(connection, destinationTable, dataTable, timeoutSeconds, options, ct);
    }

    /// <summary>
    /// Bulk-inserts all rows from a DataTable using an existing open connection.
    /// </summary>
    public static async Task BulkCopyAsync(
        SqlConnection connection,
        string destinationTable,
        DataTable dataTable,
        int timeoutSeconds = 600,
        SqlBulkCopyOptions options = SqlBulkCopyOptions.KeepIdentity,
        CancellationToken ct = default)
    {
        using var bulkCopy = new SqlBulkCopy(connection, options, null)
        {
            DestinationTableName = destinationTable,
            BulkCopyTimeout = timeoutSeconds
        };

        // Auto-map every column by name so callers don't need to specify mappings
        foreach (DataColumn col in dataTable.Columns)
            bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);

        await bulkCopy.WriteToServerAsync(dataTable, ct);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a SqlCommand with CommandTimeout=0 (unlimited) and attaches parameters.
    /// </summary>
    private static SqlCommand BuildCommand(
        SqlConnection connection,
        SqlTransaction? transaction,
        CommandType commandType,
        string commandText,
        SqlParameter[] parameters)
    {
        var command = new SqlCommand(commandText, connection)
        {
            CommandType = commandType,
            CommandTimeout = 0  // unlimited — long-running invoice batches must not time out
        };

        if (transaction is not null)
            command.Transaction = transaction;

        foreach (var p in parameters)
        {
            // Ensure InputOutput parameters with null value get DBNull so SQL defaults work
            if (p.Direction == ParameterDirection.InputOutput && p.Value is null)
                p.Value = DBNull.Value;

            command.Parameters.Add(p);
        }

        return command;
    }
}
