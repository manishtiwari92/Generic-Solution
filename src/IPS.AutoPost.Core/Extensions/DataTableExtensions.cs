using System.ComponentModel;
using System.Data;
using System.Reflection;
using System.Text;

namespace IPS.AutoPost.Core.Extensions;

/// <summary>
/// Extension and utility methods for DataTable / DataSet operations used across all plugins.
/// Consolidates the GenerateHtmlTable, ToDataTable, and ConvertDataTable helpers that were
/// previously duplicated in every legacy API library.
/// </summary>
public static class DataTableExtensions
{
    // -----------------------------------------------------------------------
    // GenerateHtmlTable
    // -----------------------------------------------------------------------

    /// <summary>
    /// Renders a DataTable as an HTML table string suitable for embedding in email bodies.
    /// Used by InvitedClub (missing-images email) and Sevita (failed-post notification email).
    /// Returns an empty string if the DataTable is null or an exception occurs.
    /// </summary>
    /// <param name="dt">The DataTable to render.</param>
    /// <param name="excludeColumns">
    /// Optional set of column names to omit from the output (case-insensitive).
    /// Sevita uses this to hide the internal IsSendNotification column from the email.
    /// </param>
    public static string GenerateHtmlTable(this DataTable dt, IEnumerable<string>? excludeColumns = null)
    {
        if (dt is null)
            return string.Empty;

        try
        {
            var excluded = excludeColumns is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(excludeColumns, StringComparer.OrdinalIgnoreCase);

            // Collect visible columns once
            var visibleColumns = dt.Columns
                .Cast<DataColumn>()
                .Where(c => !excluded.Contains(c.ColumnName))
                .ToList();

            var sb = new StringBuilder();
            sb.Append("<table class='datatbl' border='1' style='text-align:center; min-width:200px; border:1px solid black; border-collapse:collapse;'>");

            // Header row
            sb.Append("<thead style='color:#548DD4; font-weight:600;'><tr>");
            foreach (var col in visibleColumns)
                sb.Append($"<td style='border:1px solid black; border-collapse:collapse;'>{col.ColumnName}</td>");
            sb.Append("</tr></thead>");

            // Data rows
            sb.Append("<tbody style='color:#548DD4'>");
            foreach (DataRow row in dt.Rows)
            {
                sb.Append("<tr>");
                foreach (var col in visibleColumns)
                    sb.Append($"<td style='border:1px solid black; border-collapse:collapse;'>{row[col]}</td>");
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");

            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    // -----------------------------------------------------------------------
    // ToDataTable<T>
    // -----------------------------------------------------------------------

    /// <summary>
    /// Converts a generic list to a DataTable whose columns match the public properties of T.
    /// Nullable value types are unwrapped so the DataTable column type is the underlying type.
    /// </summary>
    /// <typeparam name="T">The element type of the list.</typeparam>
    /// <param name="data">The list to convert.</param>
    /// <returns>A DataTable with one row per list element.</returns>
    public static DataTable ToDataTable<T>(this IEnumerable<T> data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var properties = TypeDescriptor.GetProperties(typeof(T));
        var dt = new DataTable(typeof(T).Name);

        // Build columns — unwrap Nullable<T> so DataTable stores the base type
        for (int i = 0; i < properties.Count; i++)
        {
            var prop = properties[i];
            var columnType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            dt.Columns.Add(prop.Name, columnType);
        }

        // Populate rows
        var values = new object?[properties.Count];
        foreach (var item in data)
        {
            for (int i = 0; i < values.Length; i++)
                values[i] = properties[i].GetValue(item) ?? DBNull.Value;

            dt.Rows.Add(values);
        }

        return dt;
    }

    // -----------------------------------------------------------------------
    // ConvertDataTable<T>
    // -----------------------------------------------------------------------

    /// <summary>
    /// Converts a DataTable to a strongly-typed list by matching column names to property names
    /// (case-sensitive, matching the legacy Parser.ConvertDataTable behaviour).
    /// DBNull values are converted to the default value for the property type.
    /// </summary>
    /// <typeparam name="T">Target type. Must have a public parameterless constructor.</typeparam>
    /// <param name="dt">Source DataTable.</param>
    /// <returns>A list of T with one element per DataRow.</returns>
    public static List<T> ConvertDataTable<T>(this DataTable dt) where T : new()
    {
        ArgumentNullException.ThrowIfNull(dt);

        var properties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToArray();

        // Pre-build a lookup: column name -> property (only for columns that exist in the DataTable)
        var columnToProperty = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn col in dt.Columns)
        {
            var prop = properties.FirstOrDefault(p =>
                string.Equals(p.Name, col.ColumnName, StringComparison.OrdinalIgnoreCase));
            if (prop is not null)
                columnToProperty[col.ColumnName] = prop;
        }

        var result = new List<T>(dt.Rows.Count);

        foreach (DataRow row in dt.Rows)
        {
            var item = new T();

            foreach (var (columnName, prop) in columnToProperty)
            {
                var rawValue = row[columnName];

                if (rawValue == DBNull.Value || rawValue is null)
                {
                    // Leave the property at its default value (null for reference types, 0 for value types)
                    continue;
                }

                try
                {
                    var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    var converted = Convert.ChangeType(rawValue, targetType);
                    prop.SetValue(item, converted);
                }
                catch
                {
                    // If conversion fails, leave the property at its default — same behaviour as legacy Parser
                }
            }

            result.Add(item);
        }

        return result;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns true when the DataSet is null, has no tables, or the first table has no rows.
    /// Mirrors the legacy CommonMethods.CheckForEmptyDataSet helper.
    /// </summary>
    public static bool IsEmpty(this DataSet? ds)
        => ds is null || ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0;

    /// <summary>
    /// Returns true when the DataTable is null or has no rows.
    /// </summary>
    public static bool IsEmpty(this DataTable? dt)
        => dt is null || dt.Rows.Count == 0;
}
