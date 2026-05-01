namespace IPS.AutoPost.Plugins.InvitedClub.Models;

/// <summary>
/// InvitedClub-specific post history record inserted into
/// <c>post_to_invitedclub_history</c> after each workitem is processed.
/// Only written when at least one Oracle Fusion API call was attempted —
/// NOT written for early exits (image not found, empty RequesterId).
/// </summary>
public class PostHistory
{
    /// <summary>Workflow ItemId (WFInvitedClubsIndexHeader.UID).</summary>
    public long ItemId { get; set; }

    /// <summary>Serialized <see cref="InvoiceRequest"/> JSON sent to Oracle Fusion.</summary>
    public string InvoiceRequestJson { get; set; } = string.Empty;

    /// <summary>Raw response body from the Oracle Fusion invoice POST.</summary>
    public string InvoiceResponseJson { get; set; } = string.Empty;

    /// <summary>Serialized <see cref="AttachmentRequest"/> JSON sent to Oracle Fusion.</summary>
    public string AttachmentRequestJson { get; set; } = string.Empty;

    /// <summary>Raw response body from the Oracle Fusion attachment POST.</summary>
    public string AttachmentResponseJson { get; set; } = string.Empty;

    /// <summary>Serialized <see cref="InvoiceCalculateTaxRequest"/> JSON sent to Oracle Fusion (only when UseTax = "YES").</summary>
    public string CalculateTaxRequestJson { get; set; } = string.Empty;

    /// <summary>Raw response body from the Oracle Fusion calculateTax action (only when UseTax = "YES").</summary>
    public string CalculateTaxResponseJson { get; set; } = string.Empty;

    /// <summary>
    /// <c>true</c> when the post was triggered manually from the Workflow UI;
    /// <c>false</c> for scheduled automatic posts.
    /// </summary>
    public bool ManuallyPosted { get; set; }

    /// <summary>UserId of the person or service account that triggered the post.</summary>
    public int PostedBy { get; set; }
}
