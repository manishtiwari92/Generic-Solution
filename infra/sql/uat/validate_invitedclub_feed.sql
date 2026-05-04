-- =============================================================================
-- validate_invitedclub_feed.sql
-- UAT Validation — InvitedClub Feed Download Verification
-- =============================================================================
--
-- PURPOSE:
--   Verify that the new IPS AutoPost Platform's InvitedClub feed download
--   populated the feed tables correctly and updated the relevant timestamps.
--
-- CHECKS:
--   1. Row counts in InvitedClubSupplier, InvitedClubSupplierAddress,
--      InvitedClubSupplierSite, InvitedClubCOA — warns if any table has 0 rows
--   2. last_supplier_download_time in post_to_invitedclub_configuration was
--      updated after the feed run
--   3. generic_execution_history has a successful FEED execution record
--      (confirms the CSV export file path was written)
--
-- USAGE:
--   Run this script after the first InvitedClub feed download by the new platform.
--   Compare row counts with the baseline captured before the feed run.
--
-- BASELINE CAPTURE (run BEFORE the feed):
--   SELECT 'InvitedClubSupplier'        AS TableName, COUNT(*) AS RowCount FROM dbo.InvitedClubSupplier
--   UNION ALL
--   SELECT 'InvitedClubSupplierAddress', COUNT(*) FROM dbo.InvitedClubSupplierAddress
--   UNION ALL
--   SELECT 'InvitedClubSupplierSite',    COUNT(*) FROM dbo.InvitedClubSupplierSite
--   UNION ALL
--   SELECT 'InvitedClubCOA',             COUNT(*) FROM dbo.InvitedClubCOA;
-- =============================================================================

USE [Workflow];
GO

SET NOCOUNT ON;
GO

PRINT '=== InvitedClub Feed Download Validation ===';
PRINT CONCAT('Timestamp : ', CONVERT(VARCHAR, GETUTCDATE(), 126));
PRINT '';
GO

-- ---------------------------------------------------------------------------
-- Check 1: Row counts in feed tables
-- ---------------------------------------------------------------------------
PRINT '--- Check 1: Feed Table Row Counts ---';

DECLARE @feed_table_counts TABLE (
    TableName   VARCHAR(100),
    RowCount    INT,
    Status      VARCHAR(20)
);

-- InvitedClubSupplier
DECLARE @supplier_count INT = (SELECT COUNT(*) FROM dbo.InvitedClubSupplier);
INSERT INTO @feed_table_counts VALUES ('InvitedClubSupplier', @supplier_count,
    CASE WHEN @supplier_count = 0 THEN 'WARNING: 0 rows' ELSE 'OK' END);

-- InvitedClubSupplierAddress
DECLARE @address_count INT = (SELECT COUNT(*) FROM dbo.InvitedClubSupplierAddress);
INSERT INTO @feed_table_counts VALUES ('InvitedClubSupplierAddress', @address_count,
    CASE WHEN @address_count = 0 THEN 'WARNING: 0 rows' ELSE 'OK' END);

-- InvitedClubSupplierSite
DECLARE @site_count INT = (SELECT COUNT(*) FROM dbo.InvitedClubSupplierSite);
INSERT INTO @feed_table_counts VALUES ('InvitedClubSupplierSite', @site_count,
    CASE WHEN @site_count = 0 THEN 'WARNING: 0 rows' ELSE 'OK' END);

-- InvitedClubCOA
DECLARE @coa_count INT = (SELECT COUNT(*) FROM dbo.InvitedClubCOA);
INSERT INTO @feed_table_counts VALUES ('InvitedClubCOA', @coa_count,
    CASE WHEN @coa_count = 0 THEN 'WARNING: 0 rows' ELSE 'OK' END);

-- Display results
SELECT
    TableName,
    RowCount,
    Status
FROM @feed_table_counts
ORDER BY TableName;

-- Warn on any zero-row tables
DECLARE @zero_count INT = (SELECT COUNT(*) FROM @feed_table_counts WHERE RowCount = 0);
IF @zero_count > 0
BEGIN
    PRINT CONCAT('WARNING: ', @zero_count, ' table(s) have 0 rows. Feed may not have run or may have failed.');
END
ELSE
BEGIN
    PRINT 'All feed tables have rows. Feed data appears to have been loaded.';
END
GO

-- ---------------------------------------------------------------------------
-- Check 2: last_supplier_download_time was updated after the feed run
-- ---------------------------------------------------------------------------
PRINT '';
PRINT '--- Check 2: last_supplier_download_time in post_to_invitedclub_configuration ---';

SELECT
    id,
    job_id,
    last_supplier_download_time,
    last_download_time,
    CASE
        WHEN last_supplier_download_time >= DATEADD(HOUR, -24, GETUTCDATE())
            THEN 'UPDATED (within last 24h)'
        WHEN last_supplier_download_time IS NULL
            THEN 'WARNING: NULL — feed has never run'
        ELSE CONCAT('STALE — last updated: ', CONVERT(VARCHAR, last_supplier_download_time, 126))
    END AS DownloadStatus
FROM dbo.post_to_invitedclub_configuration
ORDER BY id;
GO

-- ---------------------------------------------------------------------------
-- Check 3: generic_execution_history has a successful FEED execution
-- ---------------------------------------------------------------------------
PRINT '';
PRINT '--- Check 3: generic_execution_history — FEED executions (last 24h) ---';

SELECT
    geh.id,
    geh.execution_type,
    geh.trigger_type,
    geh.status,
    geh.records_processed,
    geh.records_success,
    geh.records_failed,
    geh.start_time,
    geh.end_time,
    geh.duration_seconds,
    gjc.client_type,
    gjc.job_id,
    gjc.job_name
FROM dbo.generic_execution_history geh
INNER JOIN dbo.generic_job_configuration gjc
    ON gjc.id = geh.job_config_id
WHERE
    gjc.client_type = 'INVITEDCLUB'
    AND geh.execution_type = 'FEED'
    AND geh.start_time >= DATEADD(HOUR, -24, GETUTCDATE())
ORDER BY geh.start_time DESC;

DECLARE @feed_exec_count INT = (
    SELECT COUNT(*)
    FROM dbo.generic_execution_history geh
    INNER JOIN dbo.generic_job_configuration gjc ON gjc.id = geh.job_config_id
    WHERE gjc.client_type = 'INVITEDCLUB'
      AND geh.execution_type = 'FEED'
      AND geh.start_time >= DATEADD(HOUR, -24, GETUTCDATE())
);

DECLARE @feed_success_count INT = (
    SELECT COUNT(*)
    FROM dbo.generic_execution_history geh
    INNER JOIN dbo.generic_job_configuration gjc ON gjc.id = geh.job_config_id
    WHERE gjc.client_type = 'INVITEDCLUB'
      AND geh.execution_type = 'FEED'
      AND geh.status = 'SUCCESS'
      AND geh.start_time >= DATEADD(HOUR, -24, GETUTCDATE())
);

PRINT CONCAT('FEED executions in last 24h : ', @feed_exec_count);
PRINT CONCAT('Successful FEED executions  : ', @feed_success_count);

IF @feed_exec_count = 0
BEGIN
    PRINT 'WARNING: No FEED executions found in the last 24 hours.';
    PRINT '  Possible causes: feed job has not been triggered, or ECS task is not running.';
END
ELSE IF @feed_success_count = 0
BEGIN
    PRINT 'WARNING: FEED executions found but none succeeded. Check error_details in generic_execution_history.';
END
ELSE
BEGIN
    PRINT 'Feed execution history confirmed. CSV export path should have been written.';
END
GO

-- ---------------------------------------------------------------------------
-- Check 4: Verify CSV export path was written (generic_execution_history output_file_path)
-- ---------------------------------------------------------------------------
PRINT '';
PRINT '--- Check 4: CSV Export File Path in generic_execution_history ---';

SELECT
    geh.id,
    geh.start_time,
    geh.status,
    geh.output_file_path,
    CASE
        WHEN geh.output_file_path IS NOT NULL AND geh.output_file_path <> ''
            THEN 'CSV path recorded'
        ELSE 'WARNING: No CSV path recorded'
    END AS CsvStatus
FROM dbo.generic_execution_history geh
INNER JOIN dbo.generic_job_configuration gjc ON gjc.id = geh.job_config_id
WHERE
    gjc.client_type = 'INVITEDCLUB'
    AND geh.execution_type = 'FEED'
    AND geh.start_time >= DATEADD(HOUR, -24, GETUTCDATE())
ORDER BY geh.start_time DESC;
GO

-- ---------------------------------------------------------------------------
-- Summary
-- ---------------------------------------------------------------------------
PRINT '';
PRINT '=== Feed Validation Complete ===';
PRINT 'Review the result sets above for any WARNING rows.';
PRINT 'Expected: All 4 feed tables have rows, last_supplier_download_time is recent,';
PRINT '          and at least one successful FEED execution exists in generic_execution_history.';
GO
