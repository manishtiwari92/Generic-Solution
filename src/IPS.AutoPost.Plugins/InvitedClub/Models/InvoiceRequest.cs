using Newtonsoft.Json;

namespace IPS.AutoPost.Plugins.InvitedClub.Models;

/// <summary>
/// Oracle Fusion invoice request payload for the POST /invoices API.
/// </summary>
public class InvoiceRequest
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public string InvoiceCurrency { get; set; } = string.Empty;
    public string PaymentCurrency { get; set; } = string.Empty;
    public string InvoiceAmount { get; set; } = string.Empty;
    public string InvoiceDate { get; set; } = string.Empty;
    public string BusinessUnit { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public string SupplierSite { get; set; } = string.Empty;
    public string RequesterId { get; set; } = string.Empty;
    public string AccountingDate { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string InvoiceType { get; set; } = string.Empty;
    public string LegalEntity { get; set; } = string.Empty;
    public string LegalEntityIdentifier { get; set; } = string.Empty;
    public string LiabilityDistribution { get; set; } = string.Empty;
    public string RoutingAttribute2 { get; set; } = string.Empty;
    public string InvoiceSource { get; set; } = string.Empty;

    [JsonProperty("invoiceDff")]
    public List<InvoiceDff> InvoiceDff { get; set; } = new();

    [JsonProperty("invoiceLines")]
    public List<InvoiceLine> InvoiceLines { get; set; } = new();
}

/// <summary>
/// Oracle Fusion invoice line item.
/// </summary>
public class InvoiceLine
{
    public string LineNumber { get; set; } = string.Empty;
    public string LineAmount { get; set; } = string.Empty;
    public string ShipToLocation { get; set; } = string.Empty;
    public string DistributionCombination { get; set; } = string.Empty;

    [JsonProperty("invoiceDistributions")]
    public List<InvoiceDistribution> InvoiceDistributions { get; set; } = new();
}

/// <summary>
/// Oracle Fusion invoice distribution (GL account split).
/// </summary>
public class InvoiceDistribution
{
    public string DistributionLineNumber { get; set; } = string.Empty;
    public string DistributionLineType { get; set; } = string.Empty;
    public string DistributionAmount { get; set; } = string.Empty;
    public string DistributionCombination { get; set; } = string.Empty;
}

/// <summary>
/// Oracle Fusion invoice descriptive flexfield (DFF) for custom attributes.
/// </summary>
public class InvoiceDff
{
    [JsonProperty("payor")]
    public string Payor { get; set; } = string.Empty;
}
