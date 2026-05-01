namespace IPS.AutoPost.Plugins.Sevita.Models;

/// <summary>
/// Runtime-loaded sets of valid vendor and employee identifiers used by
/// <c>SevitaValidationService</c> to validate invoice records before posting.
/// </summary>
/// <remarks>
/// Loaded once per batch in <c>SevitaPlugin.OnBeforePostAsync()</c> using a direct
/// <c>SqlConnection</c> with <c>SqlDataReader.NextResult()</c> to read both result sets
/// in a single round trip:
/// <code>
/// SELECT Supplier FROM Sevita_Supplier_SiteInformation_Feed;
/// SELECT EmployeeID FROM Sevita_Employee_Feed
/// </code>
/// Uses raw ADO.NET (not SqlHelper) because SqlHelper's <c>ExecuteDatasetAsync</c>
/// does not support multi-statement queries with <c>NextResult()</c>.
/// </remarks>
public class ValidIds
{
    /// <summary>
    /// Set of valid vendor identifiers loaded from
    /// <c>Sevita_Supplier_SiteInformation_Feed.Supplier</c>.
    /// Used to validate <see cref="InvoiceRequest.vendorId"/> for both PO and Non-PO records.
    /// Case-insensitive comparison using <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    public HashSet<string> VendorIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Set of valid employee identifiers loaded from
    /// <c>Sevita_Employee_Feed.EmployeeID</c>.
    /// Used to validate <see cref="InvoiceRequest.employeeId"/> for Non-PO records only
    /// (<c>is_PO_record = false</c>).
    /// Case-insensitive comparison using <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    public HashSet<string> EmployeeIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
