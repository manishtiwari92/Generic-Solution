using System.Text.Json;

namespace IPS.AutoPost.Core.Models;

/// <summary>
/// Unified configuration model loaded from <c>generic_job_configuration</c>.
/// Replaces all 15+ legacy <c>post_to_xxx_configuration</c> table models.
/// One row in the database maps to one instance of this class at runtime.
/// </summary>
public class GenericJobConfig
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    /// <summary>Primary key of the <c>generic_job_configuration</c> row.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Client type string that maps to a registered <see cref="Interfaces.IClientPlugin"/>.
    /// Examples: "INVITEDCLUB", "SEVITA", "MEDIA".
    /// </summary>
    public string ClientType { get; set; } = string.Empty;

    /// <summary>
    /// Numeric job identifier used in workitem queries and SQS message payloads.
    /// </summary>
    public int JobId { get; set; }

    /// <summary>Human-readable job name for logging and dashboards.</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Default user ID used when routing workitems and writing audit logs.
    /// Defaults to 100 (the legacy system user).
    /// </summary>
    public int DefaultUserId { get; set; } = 100;

    /// <summary>
    /// When <c>false</c>, the Core_Engine skips this job entirely without logging an error.
    /// </summary>
    public bool IsActive { get; set; }

    // -----------------------------------------------------------------------
    // Queue IDs
    // -----------------------------------------------------------------------

    /// <summary>
    /// Comma-separated list of source queue (StatusId) values.
    /// Workitems with a <c>StatusId</c> in this list are eligible for processing.
    /// Example: "101,102"
    /// </summary>
    public string SourceQueueId { get; set; } = string.Empty;

    /// <summary>Target queue ID when a workitem is posted successfully.</summary>
    public int SuccessQueueId { get; set; }

    /// <summary>Target queue ID when a workitem fails to post (primary failure path).</summary>
    public int PrimaryFailQueueId { get; set; }

    /// <summary>
    /// Optional secondary failure queue ID (e.g. InvitedClub uses a separate
    /// Edenred fail queue for image-not-found failures).
    /// </summary>
    public int? SecondaryFailQueueId { get; set; }

    /// <summary>Optional queue ID for workitems that require manual review.</summary>
    public int? QuestionQueueId { get; set; }

    /// <summary>Optional queue ID for workitems that are permanently terminated.</summary>
    public int? TerminatedQueueId { get; set; }

    // -----------------------------------------------------------------------
    // Table References
    // -----------------------------------------------------------------------

    /// <summary>
    /// Name of the client-specific index header table (e.g. "WFInvitedClubsIndexHeader").
    /// Used in dynamic SQL for <c>PostInProcess</c> flag updates and workitem queries.
    /// Values come from the trusted <c>generic_job_configuration</c> table only.
    /// </summary>
    public string HeaderTable { get; set; } = string.Empty;

    /// <summary>
    /// Name of the client-specific index detail table (e.g. "WFInvitedClubsIndexDetails").
    /// </summary>
    public string DetailTable { get; set; } = string.Empty;

    /// <summary>
    /// Column name in the detail table that links to the header UID.
    /// Example: Sevita uses a specific column name for this join.
    /// </summary>
    public string DetailUidColumn { get; set; } = string.Empty;

    /// <summary>
    /// Name of the client-specific history table (e.g. "post_to_invitedclub_history").
    /// </summary>
    public string HistoryTable { get; set; } = string.Empty;

    /// <summary>
    /// SQL Server connection string for the Workflow database.
    /// Populated by <c>SecretsManagerConfigurationProvider</c> at startup.
    /// </summary>
    public string DbConnectionString { get; set; } = string.Empty;

    // -----------------------------------------------------------------------
    // Authentication
    // -----------------------------------------------------------------------

    /// <summary>
    /// Authentication scheme used by this client's external ERP API.
    /// Supported values: "BASIC" (HTTP Basic Auth), "OAUTH2" (client_credentials Bearer),
    /// "APIKEY" (API key header), "NONE" (no authentication).
    /// </summary>
    public string AuthType { get; set; } = "BASIC";

    /// <summary>
    /// Basic Auth username or OAuth2 client_id.
    /// For clients using <c>client_config_json</c> for OAuth2, this may be empty.
    /// </summary>
    public string AuthUsername { get; set; } = string.Empty;

    /// <summary>
    /// Basic Auth password or OAuth2 client_secret.
    /// For clients using <c>client_config_json</c> for OAuth2, this may be empty.
    /// </summary>
    public string AuthPassword { get; set; } = string.Empty;

    // -----------------------------------------------------------------------
    // Post Service
    // -----------------------------------------------------------------------

    /// <summary>Base URL of the external ERP API endpoint for invoice posting.</summary>
    public string PostServiceUrl { get; set; } = string.Empty;

    // -----------------------------------------------------------------------
    // Scheduling
    // -----------------------------------------------------------------------

    /// <summary>
    /// When <c>false</c>, the Core_Engine skips scheduled post execution for this job.
    /// Manual posts are not affected by this flag.
    /// </summary>
    public bool AllowAutoPost { get; set; }

    /// <summary>
    /// When <c>false</c>, the Core_Engine skips feed download execution for this job.
    /// </summary>
    public bool DownloadFeed { get; set; }

    /// <summary>
    /// Timestamp of the last successful scheduled post run.
    /// Used by <c>SchedulerService.ShouldExecute</c> to enforce the 30-minute window.
    /// </summary>
    public DateTime LastPostTime { get; set; }

    /// <summary>
    /// Timestamp of the last successful feed download run.
    /// Used to determine incremental vs. full refresh for feed strategies.
    /// </summary>
    public DateTime LastDownloadTime { get; set; }

    // -----------------------------------------------------------------------
    // Paths
    // -----------------------------------------------------------------------

    /// <summary>
    /// File system or S3 path for generated output files (CSV exports, Excel reports).
    /// </summary>
    public string OutputFilePath { get; set; } = string.Empty;

    /// <summary>File system or S3 path where feed downloads are stored.</summary>
    public string FeedDownloadPath { get; set; } = string.Empty;

    /// <summary>
    /// Base path for invoice images on legacy jobs (local file system path).
    /// Used when <see cref="IsLegacyJob"/> is <c>true</c>.
    /// </summary>
    public string ImageParentPath { get; set; } = string.Empty;

    /// <summary>
    /// Base path for invoice images on new-UI jobs (S3 or network path).
    /// Used when <see cref="IsLegacyJob"/> is <c>false</c>.
    /// </summary>
    public string NewUiImageParentPath { get; set; } = string.Empty;

    /// <summary>
    /// When <c>true</c>, images are retrieved from the local file system using
    /// <see cref="ImageParentPath"/>. When <c>false</c>, images are retrieved from S3.
    /// </summary>
    public bool IsLegacyJob { get; set; }

    // -----------------------------------------------------------------------
    // Client-specific extras
    // -----------------------------------------------------------------------

    /// <summary>
    /// JSON blob containing all client-specific configuration that does not fit
    /// the generic columns. Deserialized at runtime via <see cref="GetClientConfig{T}"/>.
    /// <example>
    /// InvitedClub stores: ImagePostRetryLimit, EdenredFailQueueId, FeedDownloadTime, etc.
    /// Sevita stores: IsPORecord, PostJsonPath, ApiAccessTokenUrl, ClientId, ClientSecret, etc.
    /// </example>
    /// </summary>
    public string ClientConfigJson { get; set; } = string.Empty;

    // -----------------------------------------------------------------------
    // Methods
    // -----------------------------------------------------------------------

    /// <summary>
    /// Deserializes <see cref="ClientConfigJson"/> into a typed client-specific config object.
    /// Returns a default instance of <typeparamref name="T"/> when the JSON is empty or null.
    /// </summary>
    /// <typeparam name="T">
    /// The client-specific config type (e.g. <c>InvitedClubConfig</c>, <c>SevitaConfig</c>).
    /// Must have a public parameterless constructor.
    /// </typeparam>
    /// <example>
    /// <code>
    /// var invitedClubConfig = config.GetClientConfig&lt;InvitedClubConfig&gt;();
    /// var sevitaConfig = config.GetClientConfig&lt;SevitaConfig&gt;();
    /// </code>
    /// </example>
    public T GetClientConfig<T>() where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(ClientConfigJson))
            return new T();

        return JsonSerializer.Deserialize<T>(
            ClientConfigJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new T();
    }
}
