namespace IPS.AutoPost.Core.Migrations.Entities;

/// <summary>
/// EF Core entity for the <c>generic_field_mapping</c> table.
/// Drives dynamic payload building for simple REST clients (GenericRestPlugin).
/// Allows clients like Vantaca, Akron, Michelman, Signature, Rent Manager, ReactorNet,
/// Workday, and Trump to be handled entirely from configuration with zero plugin code.
/// </summary>
public class GenericFieldMappingEntity
{
    public int Id { get; set; }

    /// <summary>Foreign key to <c>generic_job_configuration.id</c>.</summary>
    public int JobConfigId { get; set; }

    /// <summary>
    /// Mapping type: 'INVOICE_HEADER', 'INVOICE_LINE', 'FEED_RESPONSE', or 'FEED_REQUEST'.
    /// </summary>
    public string MappingType { get; set; } = string.Empty;

    /// <summary>
    /// Source field — a DB column name, a constant ('CONST:USD'), or a nested path.
    /// </summary>
    public string SourceField { get; set; } = string.Empty;

    /// <summary>
    /// Target field — JSON path in the request body or XML element path.
    /// </summary>
    public string TargetField { get; set; } = string.Empty;

    /// <summary>
    /// Data type for conversion: 'VARCHAR', 'INT', 'DECIMAL', 'DATE', 'DATETIME', 'BIT', 'BASE64'.
    /// </summary>
    public string DataType { get; set; } = "VARCHAR";

    /// <summary>
    /// Optional transform rule as JSON (e.g. date format, string trim, number format, lookup).
    /// </summary>
    public string? TransformRule { get; set; }

    /// <summary>
    /// When true, a null/empty source value blocks the post and routes to the question queue.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>Controls JSON field order in the generated payload.</summary>
    public int SortOrder { get; set; }

    public bool IsActive { get; set; }

    // Navigation property
    public GenericJobConfigurationEntity JobConfiguration { get; set; } = null!;
}
