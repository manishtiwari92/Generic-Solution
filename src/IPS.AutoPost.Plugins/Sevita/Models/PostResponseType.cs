namespace IPS.AutoPost.Plugins.Sevita.Models;

/// <summary>
/// API response type configuration loaded from <c>api_response_configuration</c>
/// for the Sevita plugin.
/// </summary>
/// <remarks>
/// Loaded at the start of each <c>PostData</c> execution for manual posts via
/// <c>GetAPIResponseTypes(configuration)</c>. Used to map response type keys
/// (e.g., <c>"POST_SUCCESS"</c>, <c>"RECORD_NOT_POSTED"</c>) to numeric response
/// codes and human-readable messages returned to the Workflow UI.
/// <para>
/// Note: The <c>sevita_response_configuration</c> table exists in the database but
/// is NOT used in the current implementation. Only <c>api_response_configuration</c>
/// is queried for response type lookups.
/// </para>
/// </remarks>
public class PostResponseType
{
    /// <summary>
    /// Response type key (e.g., <c>"POST_SUCCESS"</c>, <c>"RECORD_NOT_POSTED"</c>).
    /// Maps to <c>api_response_configuration.response_type</c>.
    /// </summary>
    public string ResponseType { get; set; } = string.Empty;

    /// <summary>
    /// Numeric response code returned to the Workflow UI.
    /// Maps to <c>api_response_configuration.response_code</c>.
    /// </summary>
    public int ResponseCode { get; set; }

    /// <summary>
    /// Human-readable response message returned to the Workflow UI.
    /// Maps to <c>api_response_configuration.response_message</c>.
    /// </summary>
    public string ResponseMessage { get; set; } = string.Empty;
}
