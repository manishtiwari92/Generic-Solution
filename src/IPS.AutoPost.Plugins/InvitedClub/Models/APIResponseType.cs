namespace IPS.AutoPost.Plugins.InvitedClub.Models;

/// <summary>
/// Maps a response type key to a numeric code and human-readable message,
/// loaded from the <c>api_response_configuration</c> table scoped by <c>job_id</c>.
/// Used only for manual post responses so the Workflow UI receives a structured result.
/// </summary>
/// <remarks>
/// Known response type keys used by InvitedClub:
/// <list type="bullet">
///   <item><c>POST_SUCCESS</c> — invoice posted successfully</item>
///   <item><c>RECORD_NOT_POSTED</c> — invoice post failed</item>
/// </list>
/// </remarks>
public class APIResponseType
{
    /// <summary>Response type key (e.g. "POST_SUCCESS", "RECORD_NOT_POSTED").</summary>
    public string ResponseType { get; set; } = string.Empty;

    /// <summary>Numeric response code returned to the Workflow UI.</summary>
    public string ResponseCode { get; set; } = string.Empty;

    /// <summary>Human-readable message returned to the Workflow UI.</summary>
    public string ResponseMessage { get; set; } = string.Empty;
}
