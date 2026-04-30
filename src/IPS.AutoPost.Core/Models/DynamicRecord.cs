namespace IPS.AutoPost.Core.Models;

/// <summary>
/// Schema-agnostic data model used by <c>GenericRestPlugin</c> to build request payloads
/// from <c>generic_field_mapping</c> rows without knowing column names at compile time.
/// Wraps a <see cref="Dictionary{TKey,TValue}"/> of field name → value pairs.
/// </summary>
public class DynamicRecord
{
    /// <summary>
    /// Field name → value dictionary populated from a DataRow or JSON object.
    /// Keys are case-insensitive for lookup convenience.
    /// </summary>
    public Dictionary<string, object?> Fields { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Retrieves a field value by name and converts it to <typeparamref name="T"/>.
    /// Returns <c>default(T)</c> when the field is absent, null, or DBNull.
    /// </summary>
    /// <typeparam name="T">The target type to convert the value to.</typeparam>
    /// <param name="fieldName">Field name (case-insensitive).</param>
    /// <returns>
    /// The converted value, or <c>default(T)</c> if the field is missing or null.
    /// </returns>
    /// <example>
    /// <code>
    /// var record = new DynamicRecord();
    /// record.Fields["InvoiceAmount"] = 1234.56m;
    /// decimal amount = record.GetValue&lt;decimal&gt;("InvoiceAmount"); // 1234.56
    /// string? missing = record.GetValue&lt;string&gt;("NonExistent");   // null
    /// </code>
    /// </example>
    public T? GetValue<T>(string fieldName)
    {
        if (!Fields.TryGetValue(fieldName, out var raw))
            return default;

        if (raw is null || raw is DBNull)
            return default;

        try
        {
            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T)Convert.ChangeType(raw, targetType);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the field exists and has a non-null, non-DBNull value.
    /// </summary>
    public bool HasValue(string fieldName)
    {
        return Fields.TryGetValue(fieldName, out var raw)
               && raw is not null
               && raw is not DBNull;
    }
}
