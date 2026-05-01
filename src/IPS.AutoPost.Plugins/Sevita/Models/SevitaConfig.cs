namespace IPS.AutoPost.Plugins.Sevita.Models;

/// <summary>
/// Client-specific configuration for Sevita, deserialized from
/// <c>generic_job_configuration.client_config_json</c>.
/// </summary>
public class SevitaConfig
{
    /// <summary>
    /// Controls which validation path is used for each workitem.
    /// <c>true</c> = PO record (validates vendorId, invoiceDate, invoiceNumber, checkMemo).
    /// <c>false</c> = Non-PO record (additionally validates employeeId, expensePeriod, and
    /// cerfTrackingNumber when any line has naturalAccountNumber = "174098").
    /// Maps to <c>post_to_sevita_configuration.is_PO_record</c>.
    /// </summary>
    public bool IsPORecord { get; set; }

    /// <summary>
    /// S3 path prefix for uploading the serialized invoice request JSON before posting.
    /// When configured, the payload is uploaded to <c>{PostJsonPath}/{itemId}_{timestamp}.json</c>
    /// for audit and debugging purposes.
    /// Maps to <c>post_to_sevita_configuration.post_json_path</c>.
    /// </summary>
    public string PostJsonPath { get; set; } = string.Empty;

    /// <summary>
    /// Legacy T-drive location for invoice images (not used in the new platform — Sevita
    /// always retrieves images from S3, never from the local file system).
    /// Maps to <c>post_to_sevita_configuration.t_drive_location</c>.
    /// </summary>
    public string TDriveLocation { get; set; } = string.Empty;

    /// <summary>
    /// New UI T-drive location for invoice images (not used in the new platform).
    /// Maps to <c>post_to_sevita_configuration.new_ui_t_drive_location</c>.
    /// </summary>
    public string NewUiTDriveLocation { get; set; } = string.Empty;

    /// <summary>
    /// Remote path used for file transfer operations.
    /// Maps to <c>post_to_sevita_configuration.remote_path</c>.
    /// </summary>
    public string RemotePath { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 token endpoint URL for the <c>client_credentials</c> grant.
    /// POST request is sent here with <c>grant_type=client_credentials</c>,
    /// <c>client_id</c>, and <c>client_secret</c>.
    /// Maps to <c>post_to_sevita_configuration.api_access_token_url</c>.
    /// </summary>
    public string ApiAccessTokenUrl { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 client ID used in the <c>client_credentials</c> token request.
    /// Maps to <c>post_to_sevita_configuration.client_id</c>.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 client secret used in the <c>client_credentials</c> token request.
    /// Maps to <c>post_to_sevita_configuration.client_secret</c>.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Number of minutes the cached OAuth2 Bearer token remains valid before a new
    /// token request is made. Defaults to 60 minutes.
    /// Maps to <c>post_to_sevita_configuration.token_expiration_min</c>.
    /// </summary>
    public int TokenExpirationMin { get; set; } = 60;

    /// <summary>
    /// Database error email configuration loaded from the <c>get_sevita_configurations</c>
    /// stored procedure result. Used for sending notifications when database errors occur,
    /// separate from the post failure notification email.
    /// </summary>
    public DBErrorEmailConfiguration DBErrorEmail { get; set; } = new();
}
