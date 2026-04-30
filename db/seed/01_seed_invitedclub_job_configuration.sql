-- ============================================================
-- Seed Script: generic_job_configuration — InvitedClub
-- ============================================================
-- Source table : post_to_invitedclub_configuration
-- Source SP    : get_invitedclub_configuration (@IsNewUI BIT)
-- Target table : generic_job_configuration
-- client_type  : 'INVITEDCLUB'
--
-- IMPORTANT: This script is IDEMPOTENT.
--   - It uses MERGE so it can be re-run safely.
--   - It does NOT modify post_to_invitedclub_configuration.
--   - It does NOT alter any existing stored procedures.
--   - Credentials (auth_username / auth_password) are migrated
--     as-is from the source table. After migration, rotate them
--     into AWS Secrets Manager and clear the DB columns.
--
-- Column mapping:
--   post_to_invitedclub_configuration  ->  generic_job_configuration
--   id                                 ->  (used as correlation key only)
--   job_id                             ->  job_id
--   user_id                            ->  default_user_id
--   source_queue_id                    ->  source_queue_id  (CAST to VARCHAR)
--   edenred_fail_post_queue_id         ->  secondary_fail_queue_id
--   invited_fail_post_queue_id         ->  primary_fail_queue_id
--   question_queue_id                  ->  question_queue_id
--   success_queue_id                   ->  success_queue_id
--   allow_auto_post                    ->  allow_auto_post
--   last_post_time                     ->  last_post_time
--   image_parent_path                  ->  image_parent_path
--   new_ui_image_parent_path           ->  new_ui_image_parent_path
--   history_table                      ->  history_table
--   db_connection_string               ->  db_connection_string
--   download_feed                      ->  download_feed
--   last_download_time                 ->  last_download_time
--   feed_download_path                 ->  feed_download_path
--   auth_user_name                     ->  auth_username
--   auth_password                      ->  auth_password
--   download_service_url               ->  (stored in client_config_json)
--   post_service_url                   ->  post_service_url
--   is_legacy_job                      ->  is_legacy_job
--   image_post_retry_limit             ->  (stored in client_config_json)
--   feed_download_time                 ->  (stored in client_config_json)
--   last_supplier_download_time        ->  (stored in client_config_json)
--
--   header_table                       ->  'WFInvitedClubsIndexHeader'  (fixed)
--   detail_table                       ->  'WFInvitedClubsIndexDetails' (fixed)
--   detail_uid_column                  ->  'UID'                        (fixed)
--   auth_type                          ->  'BASIC'                      (fixed)
--   client_type                        ->  'INVITEDCLUB'                (fixed)
--   job_name                           ->  'InvitedClub AutoPost'       (fixed)
-- ============================================================

USE [Workflow];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

-- -------------------------------------------------------
-- Step 1: Migrate each row from post_to_invitedclub_configuration
--         into generic_job_configuration using MERGE.
-- -------------------------------------------------------
MERGE INTO dbo.generic_job_configuration AS tgt
USING (
    SELECT
        src.id                          AS src_id,
        src.job_id,
        COALESCE(src.user_id, 100)      AS default_user_id,
        CAST(src.source_queue_id AS VARCHAR(500))
                                        AS source_queue_id,
        src.success_queue_id,
        src.invited_fail_post_queue_id  AS primary_fail_queue_id,
        src.edenred_fail_post_queue_id  AS secondary_fail_queue_id,
        src.question_queue_id,
        src.allow_auto_post,
        src.last_post_time,
        src.image_parent_path,
        src.new_ui_image_parent_path,
        src.history_table,
        src.db_connection_string,
        src.download_feed,
        src.last_download_time,
        src.feed_download_path,
        src.auth_user_name              AS auth_username,
        src.auth_password,
        src.post_service_url,
        src.is_legacy_job,
        -- Build client_config_json from InvitedClub-specific fields
        -- that have no generic column equivalent.
        (
            SELECT
                src.image_post_retry_limit      AS imagePostRetryLimit,
                src.edenred_fail_post_queue_id  AS edenredFailQueueId,
                src.invited_fail_post_queue_id  AS invitedFailQueueId,
                src.feed_download_time          AS feedDownloadTime,
                src.last_supplier_download_time AS lastSupplierDownloadTime,
                src.download_service_url        AS downloadServiceUrl
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        )                               AS client_config_json
    FROM dbo.post_to_invitedclub_configuration AS src
) AS src
ON (
    tgt.client_type = 'INVITEDCLUB'
    AND tgt.job_id  = src.job_id
)
WHEN MATCHED THEN
    UPDATE SET
        tgt.default_user_id         = src.default_user_id,
        tgt.source_queue_id         = src.source_queue_id,
        tgt.success_queue_id        = src.success_queue_id,
        tgt.primary_fail_queue_id   = src.primary_fail_queue_id,
        tgt.secondary_fail_queue_id = src.secondary_fail_queue_id,
        tgt.question_queue_id       = src.question_queue_id,
        tgt.allow_auto_post         = src.allow_auto_post,
        tgt.last_post_time          = src.last_post_time,
        tgt.image_parent_path       = src.image_parent_path,
        tgt.new_ui_image_parent_path= src.new_ui_image_parent_path,
        tgt.history_table           = src.history_table,
        tgt.db_connection_string    = src.db_connection_string,
        tgt.download_feed           = src.download_feed,
        tgt.last_download_time      = src.last_download_time,
        tgt.feed_download_path      = src.feed_download_path,
        tgt.auth_username           = src.auth_username,
        tgt.auth_password           = src.auth_password,
        tgt.post_service_url        = src.post_service_url,
        tgt.is_legacy_job           = src.is_legacy_job,
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
        secondary_fail_queue_id,
        question_queue_id,
        header_table,
        detail_table,
        detail_uid_column,
        history_table,
        db_connection_string,
        auth_type,
        auth_username,
        auth_password,
        post_service_url,
        allow_auto_post,
        download_feed,
        last_post_time,
        last_download_time,
        feed_download_path,
        image_parent_path,
        new_ui_image_parent_path,
        is_legacy_job,
        client_config_json,
        created_date
    )
    VALUES (
        'INVITEDCLUB',
        src.job_id,
        'InvitedClub AutoPost',
        src.default_user_id,
        1,                              -- is_active = true
        src.source_queue_id,
        src.success_queue_id,
        src.primary_fail_queue_id,
        src.secondary_fail_queue_id,
        src.question_queue_id,
        'WFInvitedClubsIndexHeader',    -- fixed header table
        'WFInvitedClubsIndexDetails',   -- fixed detail table
        'UID',                          -- fixed detail UID column
        src.history_table,
        src.db_connection_string,
        'BASIC',                        -- Oracle Fusion uses HTTP Basic Auth
        src.auth_username,
        src.auth_password,
        src.post_service_url,
        src.allow_auto_post,
        src.download_feed,
        src.last_post_time,
        src.last_download_time,
        src.feed_download_path,
        src.image_parent_path,
        src.new_ui_image_parent_path,
        src.is_legacy_job,
        src.client_config_json,
        GETUTCDATE()
    );

-- -------------------------------------------------------
-- Step 2: Verification — print row counts for confirmation.
-- -------------------------------------------------------
DECLARE @src_count  INT = (SELECT COUNT(*) FROM dbo.post_to_invitedclub_configuration);
DECLARE @tgt_count  INT = (SELECT COUNT(*) FROM dbo.generic_job_configuration WHERE client_type = 'INVITEDCLUB');

PRINT CONCAT('post_to_invitedclub_configuration rows : ', @src_count);
PRINT CONCAT('generic_job_configuration INVITEDCLUB  : ', @tgt_count);

IF @src_count <> @tgt_count
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('Row count mismatch — rolling back. Expected %d rows, got %d.', 16, 1, @src_count, @tgt_count);
    RETURN;
END

COMMIT TRANSACTION;
PRINT 'InvitedClub generic_job_configuration seed completed successfully.';
GO
