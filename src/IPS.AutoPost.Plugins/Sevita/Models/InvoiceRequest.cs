namespace IPS.AutoPost.Plugins.Sevita.Models;

/// <summary>
/// Sevita AP invoice request payload. Serialized as a JSON array <c>[{...}]</c>
/// (wrapped in array brackets) before being POSTed to the Sevita invoice endpoint.
/// </summary>
/// <remarks>
/// Property names use camelCase to match the Sevita API contract exactly.
/// The payload is serialized with <c>JsonSerializerOptions.PropertyNamingPolicy = CamelCase</c>
/// or Newtonsoft.Json default camelCase settings.
/// </remarks>
public class InvoiceRequest
{
    /// <summary>
    /// Sevita vendor identifier. Required for both PO and Non-PO records.
    /// Must exist in <c>Sevita_Supplier_SiteInformation_Feed.Supplier</c> (ValidIds.VendorIds).
    /// </summary>
    public string vendorId { get; set; } = string.Empty;

    /// <summary>
    /// Edenred invoice identifier, mapped from <c>documentId.Trim()</c> on the header row.
    /// Used as the base for building <see cref="InvoiceLine.edenredLineItemId"/> values.
    /// </summary>
    public string edenredInvoiceId { get; set; } = string.Empty;

    /// <summary>
    /// Employee identifier. Required for Non-PO records (<c>is_PO_record = false</c>).
    /// Must exist in <c>Sevita_Employee_Feed.EmployeeID</c> (ValidIds.EmployeeIds).
    /// </summary>
    public string employeeId { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether this invoice should be paid alone (not combined with other invoices).
    /// </summary>
    public bool payAlone { get; set; }

    /// <summary>
    /// Indicates whether this invoice is related to a Zycus purchase order.
    /// When <c>true</c>, <see cref="zycusInvoiceNumber"/> must be populated.
    /// </summary>
    public bool invoiceRelatedToZycusPurchase { get; set; }

    /// <summary>
    /// Zycus purchase order invoice number. Only included in the payload when
    /// <see cref="invoiceRelatedToZycusPurchase"/> is <c>true</c>.
    /// </summary>
    public string? zycusInvoiceNumber { get; set; }

    /// <summary>
    /// Invoice number. Required for both PO and Non-PO records.
    /// </summary>
    public string invoiceNumber { get; set; } = string.Empty;

    /// <summary>
    /// Invoice date string (format as required by the Sevita API). Required for both PO and Non-PO records.
    /// </summary>
    public string invoiceDate { get; set; } = string.Empty;

    /// <summary>
    /// Expense period for the invoice. Required for Non-PO records (<c>is_PO_record = false</c>).
    /// </summary>
    public string expensePeriod { get; set; } = string.Empty;

    /// <summary>
    /// Check memo / payment description. Required for both PO and Non-PO records.
    /// For PO records, defaults to <c>"PO#"</c> when empty.
    /// </summary>
    public string checkMemo { get; set; } = string.Empty;

    /// <summary>
    /// CERF tracking number. Required for Non-PO records when any line item has
    /// <see cref="InvoiceLine.naturalAccountNumber"/> equal to <c>"174098"</c>.
    /// </summary>
    public string? cerfTrackingNumber { get; set; }

    /// <summary>
    /// Indicates whether a remittance advice is required for this invoice.
    /// </summary>
    public bool remittanceRequired { get; set; }

    /// <summary>
    /// Invoice image attachments (PDF/TIFF). Each attachment includes the base64-encoded
    /// file content in <see cref="AttachmentRequest.fileBase"/>.
    /// Note: <c>fileBase</c> is set to <c>null</c> before saving to history to avoid
    /// storing large base64 strings in the database.
    /// </summary>
    public List<AttachmentRequest> attachments { get; set; } = new();

    /// <summary>
    /// Invoice line items, grouped by composite key (<c>alias + naturalAccountNumber</c>)
    /// with amounts summed per group. Built by <c>SevitaPostStrategy.BuildLineItems()</c>.
    /// </summary>
    public List<InvoiceLine> lineItems { get; set; } = new();
}

/// <summary>
/// A single line item in the Sevita invoice request.
/// Detail rows are grouped by (<c>alias + naturalAccountNumber</c>) and amounts are summed
/// per group before building this model.
/// </summary>
public class InvoiceLine
{
    /// <summary>
    /// GL account alias / cost center code. Used as part of the grouping key.
    /// </summary>
    public string alias { get; set; } = string.Empty;

    /// <summary>
    /// Line item amount, formatted to 2 decimal places.
    /// Represents the sum of all detail rows in the group.
    /// </summary>
    public decimal amount { get; set; }

    /// <summary>
    /// Natural account number from the GL chart of accounts.
    /// Used as part of the grouping key.
    /// When any line has value <c>"174098"</c>, <see cref="InvoiceRequest.cerfTrackingNumber"/>
    /// is required for Non-PO records.
    /// </summary>
    public string naturalAccountNumber { get; set; } = string.Empty;

    /// <summary>
    /// Edenred line item identifier, constructed as
    /// <c>{edenredInvoiceId}_{lineItemCount}</c> where <c>lineItemCount</c> is the
    /// 1-based index of this line within the grouped line items list.
    /// </summary>
    public string edenredLineItemId { get; set; } = string.Empty;
}

/// <summary>
/// Invoice image attachment included in the Sevita invoice request payload.
/// </summary>
/// <remarks>
/// The <see cref="fileBase"/> field contains the base64-encoded file content and is
/// populated when building the API payload. Before saving to <c>sevita_posted_records_history</c>,
/// <c>fileBase</c> is set to <c>null</c> on all attachments to prevent storing large
/// base64 strings in the database.
/// </remarks>
public class AttachmentRequest
{
    /// <summary>
    /// Original file name of the invoice image (e.g., <c>"invoice_12345.pdf"</c>).
    /// Required — validated before calling the Sevita API.
    /// </summary>
    public string fileName { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded content of the invoice image file.
    /// Required in the API payload — validated before calling the Sevita API.
    /// Set to <c>null</c> before saving to history.
    /// </summary>
    public string fileBase { get; set; } = string.Empty;

    /// <summary>
    /// URL reference to the invoice image file.
    /// Required — validated before calling the Sevita API.
    /// </summary>
    public string fileUrl { get; set; } = string.Empty;

    /// <summary>
    /// Document identifier for the attachment.
    /// Required — validated before calling the Sevita API.
    /// </summary>
    public string docid { get; set; } = string.Empty;
}
