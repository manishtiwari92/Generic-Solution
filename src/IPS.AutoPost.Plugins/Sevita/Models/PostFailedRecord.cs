namespace IPS.AutoPost.Plugins.Sevita.Models;

/// <summary>
/// Represents a single failed invoice posting record collected during a batch run.
/// Used to build the failure notification email sent after all workitems are processed.
/// </summary>
/// <remarks>
/// <para>
/// After the batch loop completes, when any workitems failed, <c>SevitaPostStrategy</c>
/// sends a notification email to <c>FailedPostConfiguration.EmailTo</c> (split by semicolon).
/// The email body contains an HTML table generated from the list of <see cref="PostFailedRecord"/>
/// instances where <see cref="IsSendNotification"/> is <c>true</c>.
/// </para>
/// <para>
/// The <see cref="IsSendNotification"/> column is excluded from the HTML table output —
/// it is used only to filter which records appear in the email.
/// The <c>[[AppendTable]]</c> placeholder in the email template is replaced with the
/// generated HTML table (note: Sevita uses <c>[[AppendTable]]</c>, NOT InvitedClub's
/// <c>#MissingImagesTable#</c> placeholder).
/// </para>
/// </remarks>
public class PostFailedRecord
{
    /// <summary>
    /// Supplier / vendor name for the failed invoice.
    /// Displayed in the failure notification email table.
    /// </summary>
    public string SupplierName { get; set; } = string.Empty;

    /// <summary>
    /// Name of the approver associated with the failed invoice.
    /// Displayed in the failure notification email table.
    /// </summary>
    public string ApproverName { get; set; } = string.Empty;

    /// <summary>
    /// Invoice date of the failed invoice.
    /// Displayed in the failure notification email table.
    /// </summary>
    public string InvoiceDate { get; set; } = string.Empty;

    /// <summary>
    /// Document identifier (Edenred invoice ID) of the failed invoice.
    /// Displayed in the failure notification email table.
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Controls whether this record is included in the failure notification email.
    /// <c>true</c> = include in email; <c>false</c> = exclude from email.
    /// This column is NOT rendered in the HTML table — it is used only as a filter.
    /// </summary>
    public bool IsSendNotification { get; set; }

    /// <summary>
    /// Human-readable description of why the invoice posting failed.
    /// Examples: <c>"Image is not available."</c>, <c>"Line sum does not match invoice header."</c>,
    /// <c>"Internal Server error occurred while posting invoice."</c>.
    /// Displayed in the failure notification email table.
    /// </summary>
    public string FailureReason { get; set; } = string.Empty;
}
