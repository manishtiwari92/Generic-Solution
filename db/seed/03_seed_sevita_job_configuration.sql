-- ============================================================
-- Seed Script: generic_job_configuration — Sevita
-- ============================================================
-- Source table : post_to_sevita_configuration
-- Source SP    : get_sevita_configurations
-- Target table : generic_job_configuration
-- client_type  : 'SEVITA'
--
-- IMPORTANT: This script is IDEMPOTENT.
--   - It uses MERGE so it can be re-run safely.
--   - It does NOT modify post_to_sevita_configuration.
--   - It does NOT alter any existing stored procedures.
--   - Credentials (client_id / client_secret) are migrated
--     as-is from the source table. After migration, rotate them
--     into AWS Secrets Manager and clear the DB columns.
--
-- Column mapping (from get_sevita_configurations result set):
--   post_to_sevita_configuration   ->  generic_job_configuration
--   configuration_id               ->  (used as correlation key only)
--   job_id                         ->  job_id
--   JobName                        ->  job_name
--   default_user_id                ->  default_user_id
--   source_queue_id                ->  source_queue_id  (CAST to VARCHAR)
--   failed_queue_id                ->  primary_fail_queue_id
--   success_queue_id               ->  success_queue_id
--   IndexheadTable                 ->  header_table
--   IndexDetailTable               ->  detail_table
--   detail_uid_column              ->  detail_uid_column
--   history_table                  ->  history_table
--   invoice_post_url               ->  post_service_url
--   allow_auto_post                ->  allow_auto_post
--   last_post_time                 ->  last_post_time
--   is_legacy_job                  ->  is_legacy_job
--   image_parent_path              ->  image_parent_path
--   auth_type                      ->  'OAUTH2'  (fixed — Sevita uses OAuth2 client_credentials)
--   client_id                      ->  auth_username  (OAuth2 client_id stored here)
--   client_secret                  ->  auth_password  (OAuth2 client_secret stored here)
--
--   Sevita-specific fields stored in client_config_json:
--     api_access_token_url         ->  apiAccessTokenUrl
--     client_id                    ->  clientId
--     client_secret                ->  clientSecret
--     token_expires_in_min         ->  tokenExpirationMin
--     is_PO_record                 ->  isPORecord
--     post_json_path               ->  postJsonPath
--     t_drive_location             ->  tDriveLocation
--     new_ui_t_drive_location      ->  newUiTDriveLocation
--     RemotePath                   ->  remotePath
--     route_comment                ->  routeComment
--     post_time                    ->  postTime
--
--   Email config stored in client_config_json (no generic email columns):
--     failed_post_email_to         ->  failedPostEmailTo
--     failed_post_email_cc         ->  failedPostEmailCc
--     failed_post_email_bcc        ->  failedPostEmailBcc
--     failed_post_email_subject    ->  failedPostEmailSubject
--     failed_post_email_template   ->  failedPostEmailTemplate
--     smtp_server                  ->  smtpServer
--     smtp_server_port             ->  smtpServerPort
--     send_username                ->  smtpUsername
--     send_password                ->  smtpPassword
--     email_from                   ->  emailFrom
--     smtp_use_ssl                 ->  smtpUseSsl
-- ============================================================

USE [Workflow];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

-- -------------------------------------------------------
-- Step 1: Migrate each row from post_to_sevita_configuration
--         into generic_job_configuration using MERGE.
-- -------------------------------------------------------
MERGE INTO dbo.generic_job_configuration AS tgt
USING (
    SELECT
        src.configuration_id,
        src.job_id,
        COALESCE(src.JobName, 'Sevita AutoPost')    AS job_name,
        COALESCE(src.default_user_id, 100)          AS default_user_id,
        CAST(src.source_queue_id AS VARCHAR(500))   AS source_queue_id,
        src.success_queue_id,
        src.failed_queue_id                         AS primary_fail_queue_id,
        src.IndexheadTable                          AS header_table,
        src.IndexDetailTable                        AS detail_table,
        src.detail_uid_column,
        src.history_table,
        src.invoice_post_url                        AS post_service_url,
        src.allow_auto_post,
        src.last_post_time,
        src.is_legacy_job,
        src.image_parent_path,
        -- auth_username / auth_password store the OAuth2 client_id / client_secret
        -- so the generic auth layer can pass them to SevitaTokenService.
        src.client_id                               AS auth_username,
        src.client_secret                           AS auth_password,
        -- Build client_config_json from Sevita-specific fields.
        (
            SELECT
                src.api_access_token_url            AS apiAccessTokenUrl,
                src.client_id                       AS clientId,
                src.client_secret                   AS clientSecret,
                src.token_expires_in_min            AS tokenExpirationMin,
                src.is_PO_record                    AS isPORecord,
                src.post_json_path                  AS postJsonPath,
                src.t_drive_location                AS tDriveLocation,
                src.new_ui_t_drive_location         AS newUiTDriveLocation,
                src.RemotePath                      AS remotePath,
                src.route_comment                   AS routeComment,
                src.post_time                       AS postTime,
                -- Email configuration
                src.failed_post_email_to            AS failedPostEmailTo,
                src.failed_post_email_cc            AS failedPostEmailCc,
                src.failed_post_email_bcc           AS failedPostEmailBcc,
                src.failed_post_email_subject       AS failedPostEmailSubject,
                src.failed_post_email_template      AS failedPostEmailTemplate,
                src.smtp_server                     AS smtpServer,
                src.smtp_server_port                AS smtpServerPort,
                src.send_username                   AS smtpUsername,
                src.send_password                   AS smtpPassword,
                src.email_from                      AS emailFrom,
                src.smtp_use_ssl                    AS smtpUseSsl
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        )                                           AS client_config_json
    FROM dbo.post_to_sevita_configuration AS src
) AS src
ON (
    tgt.client_type = 'SEVITA'
    AND tgt.job_id  = src.job_id
)
WHEN MATCHED THEN
    UPDATE SET
        tgt.job_name                = src.job_name,
        tgt.default_user_id         = src.default_user_id,
        tgt.source_queue_id         = src.source_queue_id,
        tgt.success_queue_id        = src.success_queue_id,
        tgt.primary_fail_queue_id   = src.primary_fail_queue_id,
        tgt.header_table            = src.header_table,
        tgt.detail_table            = src.detail_table,
        tgt.detail_uid_column       = src.detail_uid_column,
        tgt.history_table           = src.history_table,
        tgt.post_service_url        = src.post_service_url,
        tgt.allow_auto_post         = src.allow_auto_post,
        tgt.last_post_time          = src.last_post_time,
        tgt.is_legacy_job           = src.is_legacy_job,
        tgt.image_parent_path       = src.image_parent_path,
        tgt.auth_username           = src.auth_username,
        tgt.auth_password           = src.auth_password,
        tgt.client_config_json      = src.client_config_json,
        tgt.modified_date           = GETUTCDATE()
WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        client_type,
        job_id,
        job_name,
        default_user_id,
        is_active,
        source_queue_id,
        success_queue_id,
        primary_fail_queue_id,
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
        is_legacy_job,
        image_parent_path,
        client_config_json,
        created_date
    )
    VALUES (
        'SEVITA',
        src.job_id,
        src.job_name,
        src.default_user_id,
        1,                          -- is_active = true
        src.source_queue_id,
        src.success_queue_id,
        src.primary_fail_queue_id,
        src.header_table,
        src.detail_table,
        src.detail_uid_column,
        src.history_table,
        'OAUTH2',                   -- Sevita uses OAuth2 client_credentials
        src.auth_username,
        src.auth_password,
        src.post_service_url,
        src.allow_auto_post,
        0,                          -- Sevita has no feed download step
        src.last_post_time,
        src.is_legacy_job,
        src.image_parent_path,
        src.client_config_json,
        GETUTCDATE()
    );

-- -------------------------------------------------------
-- Step 2: Verification — print row counts for confirmation.
-- -------------------------------------------------------
DECLARE @src_count  INT = (SELECT COUNT(*) FROM dbo.post_to_sevita_configuration);
DECLARE @tgt_count  INT = (SELECT COUNT(*) FROM dbo.generic_job_configuration WHERE client_type = 'SEVITA');

PRINT CONCAT('post_to_sevita_configuration rows : ', @src_count);
PRINT CONCAT('generic_job_configuration SEVITA  : ', @tgt_count);

IF @src_count <> @tgt_count
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('Row count mismatch — rolling back. Expected %d rows, got %d.', 16, 1, @src_count, @tgt_count);
    RETURN;
END

COMMIT TRANSACTION;
PRINT 'Sevita generic_job_configuration seed completed successfully.';
GO
