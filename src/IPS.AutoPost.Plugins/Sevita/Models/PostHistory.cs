namespace IPS.AutoPost.Plugins.Sevita.Models;

/// <summary>
/// Sevita-specific post history record inserted into <c>sevita_posted_records_history</c>
/// after each workitem is processed.
/// </summary>
/// <remarks>
/// <para>
/// Unlike InvitedClub, Sevita ALWAYS writes a history record — even for image-fail early
/// exits. When the image cannot be found, <see cref="InvoiceRequestJson"/> and
/// <see cref="InvoiceResponseJson"/> are set to empty strings.
/// </para>
/// <para>
/// The <c>fileBase</c> field on all attachments within <see cref="InvoiceRequestJson"/>
/// is set to <c>null</c> before saving to prevent storing large base64 strings in the
/// database. The payload is parsed as a <c>JArray</c> (not <c>JObject</c>) because the
/// Sevita payload is wrapped in a JSON array <c>[{...}]</c>.
/// </para>
/// <para>
/// Table columns: <c>job_id</c>, <c>item_id</c>, <c>post_request</c>, <c>post_response</c>,
/// <c>post_date</c>, <c>posted_by</c>, <c>manually_posted</c>, <c>Comment</c>.
/// </para>
/// </remarks>
public class PostHistory
{
    /// <summary>
    /// Workflow ItemId (header table UID).
    /// Maps to <c>sevita_posted_records_history.item_id</c>.
    /// </summary>
    public long ItemId { get; set; }

    /// <summary>
    /// Serialized <see cref="InvoiceRequest"/> JSON sent to the Sevita API,
    /// with all <c>fileBase</c> values set to <c>null</c>.
    /// Empty string when the post was aborted due to image not found.
    /// Maps to <c>sevita_posted_records_history.post_request</c>.
    /// </summary>
    public string InvoiceRequestJson { get; set; } = string.Empty;

    /// <summary>
    /// Raw response body from the Sevita API.
    /// Empty string when the post was aborted before calling the API.
    /// Maps to <c>sevita_posted_records_history.post_response</c>.
    /// </summary>
    public string InvoiceResponseJson { get; set; } = string.Empty;

    /// <summary>
    /// <c>true</c> when the post was triggered manually from the Workflow UI;
    /// <c>false</c> for scheduled automatic posts.
    /// Maps to <c>sevita_posted_records_history.manually_posted</c>.
    /// </summary>
    public bool ManuallyPosted { get; set; }

    /// <summary>
    /// UserId of the person or service account that triggered the post.
    /// Uses <c>DefaultUserId</c> from configuration for scheduled posts,
    /// or the passed-in <c>userId</c> for manual posts.
    /// Maps to <c>sevita_posted_records_history.posted_by</c>.
    /// </summary>
    public int PostedBy { get; set; }

    /// <summary>
    /// Routing comment describing the outcome of the post operation
    /// (e.g., <c>"Automatic Route:"</c> or <c>"Manual Route:"</c> with result details).
    /// Maps to <c>sevita_posted_records_history.Comment</c>.
    /// </summary>
    public string Comment { get; set; } = string.Empty;
}
