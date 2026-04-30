using System.Data;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace IPS.AutoPost.Core.DataAccess;

/// <summary>
/// Loads and updates job configuration from <c>generic_job_configuration</c>
/// and related tables using <see cref="SqlHelper"/>.
/// </summary>
public class ConfigurationRepository : IConfigurationRepository
{
    private readonly string _connectionString;

    public ConfigurationRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Workflow")
            ?? throw new InvalidOperationException(
                "Connection string 'Workflow' is not configured. " +
                "Ensure SecretsManagerConfigurationProvider has resolved the /IPS/Common/{env}/Database/Workflow secret.");
    }

    // -----------------------------------------------------------------------
    // GetByJobIdAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<GenericJobConfig?> GetByJobIdAsync(int jobId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                id,
                client_type,
                job_id,
                job_name,
                default_user_id,
                is_active,
                source_queue_id,
                success_queue_id,
                primary_fail_queue_id,
                secondary_fail_queue_id,
                question_queue_id,
                terminated_queue_id,
                header_table,
                detail_table,
                detail_uid_column,
                history_table,
                auth_type,
                auth_username,
                auth_password,
                post_service_url,
                allow_auto_post,
                download_feed,
                last_post_time,
                last_download_time,
                output_file_path,
                feed_download_path,
                image_parent_path,
                new_ui_image_parent_path,
                is_legacy_job,
                client_config_json
            FROM generic_job_configuration
            WHERE job_id = @JobId
              AND is_active = 1
            """;

        var ds = await SqlHelper.ExecuteDatasetAsync(
            _connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@JobId", SqlDbType.Int, jobId));

        if (ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            return null;

        return MapRow(ds.Tables[0].Rows[0]);
    }

    // -----------------------------------------------------------------------
    // GetBySourceQueueIdAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<GenericJobConfig?> GetBySourceQueueIdAsync(int statusId, CancellationToken ct = default)
    {
        // source_queue_id is a comma-separated list (e.g. "101,102").
        // Use CHARINDEX to find the statusId within the list.
        const string sql = """
            SELECT
                id,
                client_type,
                job_id,
                job_name,
                default_user_id,
                is_active,
                source_queue_id,
                success_queue_id,
                primary_fail_queue_id,
                secondary_fail_queue_id,
                question_queue_id,
                terminated_queue_id,
                header_table,
                detail_table,
                detail_uid_column,
                history_table,
                auth_type,
                auth_username,
                auth_password,
                post_service_url,
                allow_auto_post,
                download_feed,
                last_post_time,
                last_download_time,
                output_file_path,
                feed_download_path,
                image_parent_path,
                new_ui_image_parent_path,
                is_legacy_job,
                client_config_json
            FROM generic_job_configuration
            WHERE is_active = 1
              AND CHARINDEX(CAST(@StatusId AS VARCHAR(20)), source_queue_id) > 0
            """;

        var ds = await SqlHelper.ExecuteDatasetAsync(
            _connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@StatusId", SqlDbType.Int, statusId));

        if (ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            return null;

        return MapRow(ds.Tables[0].Rows[0]);
    }

    // -----------------------------------------------------------------------
    // GetEdenredApiUrlConfigAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<EdenredApiUrlConfig> GetEdenredApiUrlConfigAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP 1
                AssetApiUrl,
                BucketName,
                S3AccessKey,
                S3SecretKey,
                S3Region
            FROM EdenredApiUrlConfig
            """;

        var ds = await SqlHelper.ExecuteDatasetAsync(
            _connectionString,
            CommandType.Text,
            sql,
            ct);

        if (ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            return new EdenredApiUrlConfig();

        var row = ds.Tables[0].Rows[0];
        return new EdenredApiUrlConfig
        {
            AssetApiUrl  = row["AssetApiUrl"]  as string ?? string.Empty,
            BucketName   = row["BucketName"]   as string ?? string.Empty,
            S3AccessKey  = row["S3AccessKey"]  as string ?? string.Empty,
            S3SecretKey  = row["S3SecretKey"]  as string ?? string.Empty,
            S3Region     = row["S3Region"]     as string ?? string.Empty
        };
    }

    // -----------------------------------------------------------------------
    // UpdateLastPostTimeAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task UpdateLastPostTimeAsync(int jobConfigId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE generic_job_configuration
               SET last_post_time = GETUTCDATE()
             WHERE id = @Id
            """;

        await SqlHelper.ExecuteNonQueryAsync(
            _connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@Id", SqlDbType.Int, jobConfigId));
    }

    // -----------------------------------------------------------------------
    // UpdateLastDownloadTimeAsync
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task UpdateLastDownloadTimeAsync(int jobConfigId, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE generic_job_configuration
               SET last_download_time = GETUTCDATE()
             WHERE id = @Id
            """;

        await SqlHelper.ExecuteNonQueryAsync(
            _connectionString,
            CommandType.Text,
            sql,
            ct,
            SqlHelper.Param("@Id", SqlDbType.Int, jobConfigId));
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maps a DataRow from <c>generic_job_configuration</c> to a <see cref="GenericJobConfig"/>.
    /// The connection string is injected from the host configuration — it is not stored
    /// in the database row.
    /// </summary>
    private GenericJobConfig MapRow(DataRow row)
    {
        return new GenericJobConfig
        {
            Id                    = Convert.ToInt32(row["id"]),
            ClientType            = row["client_type"]            as string ?? string.Empty,
            JobId                 = Convert.ToInt32(row["job_id"]),
            JobName               = row["job_name"]               as string ?? string.Empty,
            DefaultUserId         = Convert.ToInt32(row["default_user_id"]),
            IsActive              = Convert.ToBoolean(row["is_active"]),
            SourceQueueId         = row["source_queue_id"]        as string ?? string.Empty,
            SuccessQueueId        = Convert.ToInt32(row["success_queue_id"]),
            PrimaryFailQueueId    = Convert.ToInt32(row["primary_fail_queue_id"]),
            SecondaryFailQueueId  = row["secondary_fail_queue_id"] == DBNull.Value
                                        ? null
                                        : Convert.ToInt32(row["secondary_fail_queue_id"]),
            QuestionQueueId       = row["question_queue_id"] == DBNull.Value
                                        ? null
                                        : Convert.ToInt32(row["question_queue_id"]),
            TerminatedQueueId     = row["terminated_queue_id"] == DBNull.Value
                                        ? null
                                        : Convert.ToInt32(row["terminated_queue_id"]),
            HeaderTable           = row["header_table"]           as string ?? string.Empty,
            DetailTable           = row["detail_table"]           as string ?? string.Empty,
            DetailUidColumn       = row["detail_uid_column"]      as string ?? string.Empty,
            HistoryTable          = row["history_table"]          as string ?? string.Empty,
            DbConnectionString    = _connectionString,
            AuthType              = row["auth_type"]              as string ?? "BASIC",
            AuthUsername          = row["auth_username"]          as string ?? string.Empty,
            AuthPassword          = row["auth_password"]          as string ?? string.Empty,
            PostServiceUrl        = row["post_service_url"]       as string ?? string.Empty,
            AllowAutoPost         = Convert.ToBoolean(row["allow_auto_post"]),
            DownloadFeed          = Convert.ToBoolean(row["download_feed"]),
            LastPostTime          = row["last_post_time"] == DBNull.Value
                                        ? DateTime.MinValue
                                        : Convert.ToDateTime(row["last_post_time"]),
            LastDownloadTime      = row["last_download_time"] == DBNull.Value
                                        ? DateTime.MinValue
                                        : Convert.ToDateTime(row["last_download_time"]),
            OutputFilePath        = row["output_file_path"]       as string ?? string.Empty,
            FeedDownloadPath      = row["feed_download_path"]     as string ?? string.Empty,
            ImageParentPath       = row["image_parent_path"]      as string ?? string.Empty,
            NewUiImageParentPath  = row["new_ui_image_parent_path"] as string ?? string.Empty,
            IsLegacyJob           = Convert.ToBoolean(row["is_legacy_job"]),
            ClientConfigJson      = row["client_config_json"]     as string ?? string.Empty
        };
    }
}
