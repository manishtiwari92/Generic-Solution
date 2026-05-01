namespace IPS.AutoPost.Plugins.InvitedClub.Models;

/// <summary>
/// Oracle Fusion attachment request payload for the POST /invoices/{invoiceId}/child/attachments API.
/// Content-Type: application/vnd.oracle.adf.resourceitem+json
/// </summary>
public class AttachmentRequest
{
    /// <summary>Attachment type. Always "File" for invoice image attachments.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Original file name of the invoice image (e.g. "invoice_12345.pdf").</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Display title shown in Oracle Fusion. Typically the same as FileName.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Oracle Fusion attachment category. Always "From Supplier" for invoice images.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Base64-encoded content of the invoice image file.</summary>
    public string FileContents { get; set; } = string.Empty;
}
