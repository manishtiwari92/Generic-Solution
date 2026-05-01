namespace IPS.AutoPost.Plugins.InvitedClub.Models;

/// <summary>
/// Represents an invoice record whose image attachment POST previously failed
/// and is eligible for retry via <c>InvitedClubRetryService.RetryPostImagesAsync</c>.
/// Populated by the <c>InvitedClub_GetFailedImagesData</c> stored procedure.
/// </summary>
public class FailedImagesData
{
    /// <summary>
    /// Oracle Fusion InvoiceId already stored on the header record.
    /// The attachment will be re-posted to /invoices/{InvoiceId}/child/attachments.
    /// </summary>
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>Workflow ItemId (WFInvitedClubsIndexHeader.UID).</summary>
    public long ItemId { get; set; }

    /// <summary>
    /// Number of times the image attachment POST has already been attempted.
    /// Incremented on every retry attempt regardless of success or failure.
    /// </summary>
    public int ImagePostRetryCount { get; set; }

    /// <summary>
    /// S3 key (non-legacy) or relative file path (legacy) of the invoice image.
    /// </summary>
    public string ImagePath { get; set; } = string.Empty;
}
