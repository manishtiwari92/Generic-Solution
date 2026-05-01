namespace IPS.AutoPost.Plugins.InvitedClub.Models;

/// <summary>
/// Oracle Fusion calculateTax action request payload.
/// Sent to POST /invoices/action/calculateTax when <c>UseTax = "YES"</c>.
/// Content-Type: application/vnd.oracle.adf.action+json
/// </summary>
public class InvoiceCalculateTaxRequest
{
    /// <summary>Invoice number matching the previously posted invoice.</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>Supplier name matching the previously posted invoice.</summary>
    public string Supplier { get; set; } = string.Empty;
}
