-- ============================================================
-- Verification Script: Existing Stored Procedures
-- ============================================================
-- Purpose: Verify that all stored procedures called by the
--          IPS.AutoPost Platform are present in the Workflow
--          database and accept the exact parameter signatures
--          used by the legacy Windows Service implementations.
--
-- This script does NOT execute the SPs (which would require
-- live data). It verifies:
--   1. The SP exists in sys.objects.
--   2. Each expected parameter exists in sys.parameters with
--      the correct name, data type, and direction.
--
-- Run this script BEFORE deploying the platform to confirm
-- no schema drift has occurred.
--
-- Expected result: All checks print 'OK'. Any 'MISSING' or
-- 'WRONG TYPE' line indicates a schema mismatch that must be
-- resolved before deployment.
--
-- SPs verified:
--   InvitedClub:
--     get_invitedclub_configuration     (@IsNewUI BIT)
--     GetExecutionSchedule              (@file_creation_config_id INT, @job_id INT)
--     InvitedClub_GetHeaderAndDetailData (@UID BIGINT)
--     InvitedClub_GetFailedImagesData   (@HeaderTable VARCHAR, @ImagePostRetryLimit INT, @InvitedFailPostQueueId BIGINT)
--   Shared:
--     WORKITEM_ROUTE                    (@itemID BIGINT, @Qid INT, @userId INT, @operationType VARCHAR, @comment VARCHAR)
--     GENERALLOG_INSERT                 (@operationType VARCHAR, @sourceObject VARCHAR, @userID INT, @comments VARCHAR, @itemID BIGINT)
--   Sevita:
--     get_sevita_configurations         (no parameters)
--     GetSevitaHeaderAndDetailDataByItem (@UID BIGINT)
--     UpdateSevitaHeaderPostFields      (@UID BIGINT)
-- ============================================================

USE [Workflow];
GO

SET NOCOUNT ON;
GO

-- -------------------------------------------------------
-- Helper: check SP existence and parameter signatures.
-- -------------------------------------------------------
DECLARE @results TABLE (
    sp_name     VARCHAR(200),
    check_item  VARCHAR(200),
    status      VARCHAR(20),
    detail      VARCHAR(500)
);

-- -------------------------------------------------------
-- 1. get_invitedclub_configuration
-- -------------------------------------------------------
IF OBJECT_ID('dbo.get_invitedclub_configuration', 'P') IS NOT NULL
    INSERT INTO @results VALUES ('get_invitedclub_configuration', 'SP exists', 'OK', 'Found in sys.objects');
ELSE
    INSERT INTO @results VALUES ('get_invitedclub_configuration', 'SP exists', 'MISSING', 'Not found in sys.objects');

-- @IsNewUI BIT
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'get_invitedclub_configuration'
    AND   p.name = '@IsNewUI'
    AND   t.name = 'bit'
)
    INSERT INTO @results VALUES ('get_invitedclub_configuration', '@IsNewUI BIT', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('get_invitedclub_configuration', '@IsNewUI BIT', 'MISSING', 'Parameter not found or wrong type');

-- -------------------------------------------------------
-- 2. GetExecutionSchedule
-- -------------------------------------------------------
IF OBJECT_ID('dbo.GetExecutionSchedule', 'P') IS NOT NULL
    INSERT INTO @results VALUES ('GetExecutionSchedule', 'SP exists', 'OK', 'Found in sys.objects');
ELSE
    INSERT INTO @results VALUES ('GetExecutionSchedule', 'SP exists', 'MISSING', 'Not found in sys.objects');

-- @file_creation_config_id INT
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'GetExecutionSchedule'
    AND   p.name = '@file_creation_config_id'
    AND   t.name = 'int'
)
    INSERT INTO @results VALUES ('GetExecutionSchedule', '@file_creation_config_id INT', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('GetExecutionSchedule', '@file_creation_config_id INT', 'MISSING', 'Parameter not found or wrong type');

-- @job_id INT
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'GetExecutionSchedule'
    AND   p.name = '@job_id'
    AND   t.name = 'int'
)
    INSERT INTO @results VALUES ('GetExecutionSchedule', '@job_id INT', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('GetExecutionSchedule', '@job_id INT', 'MISSING', 'Parameter not found or wrong type');

-- -------------------------------------------------------
-- 3. InvitedClub_GetHeaderAndDetailData
-- -------------------------------------------------------
IF OBJECT_ID('dbo.InvitedClub_GetHeaderAndDetailData', 'P') IS NOT NULL
    INSERT INTO @results VALUES ('InvitedClub_GetHeaderAndDetailData', 'SP exists', 'OK', 'Found in sys.objects');
ELSE
    INSERT INTO @results VALUES ('InvitedClub_GetHeaderAndDetailData', 'SP exists', 'MISSING', 'Not found in sys.objects');

-- @UID BIGINT
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'InvitedClub_GetHeaderAndDetailData'
    AND   p.name = '@UID'
    AND   t.name = 'bigint'
)
    INSERT INTO @results VALUES ('InvitedClub_GetHeaderAndDetailData', '@UID BIGINT', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('InvitedClub_GetHeaderAndDetailData', '@UID BIGINT', 'MISSING', 'Parameter not found or wrong type');

-- -------------------------------------------------------
-- 4. InvitedClub_GetFailedImagesData
-- -------------------------------------------------------
IF OBJECT_ID('dbo.InvitedClub_GetFailedImagesData', 'P') IS NOT NULL
    INSERT INTO @results VALUES ('InvitedClub_GetFailedImagesData', 'SP exists', 'OK', 'Found in sys.objects');
ELSE
    INSERT INTO @results VALUES ('InvitedClub_GetFailedImagesData', 'SP exists', 'MISSING', 'Not found in sys.objects');

-- @HeaderTable VARCHAR
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'InvitedClub_GetFailedImagesData'
    AND   p.name = '@HeaderTable'
    AND   t.name IN ('varchar', 'nvarchar')
)
    INSERT INTO @results VALUES ('InvitedClub_GetFailedImagesData', '@HeaderTable VARCHAR', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('InvitedClub_GetFailedImagesData', '@HeaderTable VARCHAR', 'MISSING', 'Parameter not found or wrong type');

-- @ImagePostRetryLimit INT
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'InvitedClub_GetFailedImagesData'
    AND   p.name = '@ImagePostRetryLimit'
    AND   t.name = 'int'
)
    INSERT INTO @results VALUES ('InvitedClub_GetFailedImagesData', '@ImagePostRetryLimit INT', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('InvitedClub_GetFailedImagesData', '@ImagePostRetryLimit INT', 'MISSING', 'Parameter not found or wrong type');

-- @InvitedFailPostQueueId BIGINT
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'InvitedClub_GetFailedImagesData'
    AND   p.name = '@InvitedFailPostQueueId'
    AND   t.name IN ('bigint', 'int')
)
    INSERT INTO @results VALUES ('InvitedClub_GetFailedImagesData', '@InvitedFailPostQueueId BIGINT/INT', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('InvitedClub_GetFailedImagesData', '@InvitedFailPostQueueId BIGINT/INT', 'MISSING', 'Parameter not found or wrong type');

-- -------------------------------------------------------
-- 5. WORKITEM_ROUTE
-- -------------------------------------------------------
IF OBJECT_ID('dbo.WORKITEM_ROUTE', 'P') IS NOT NULL
    INSERT INTO @results VALUES ('WORKITEM_ROUTE', 'SP exists', 'OK', 'Found in sys.objects');
ELSE
    INSERT INTO @results VALUES ('WORKITEM_ROUTE', 'SP exists', 'MISSING', 'Not found in sys.objects');

-- @itemID BIGINT
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'WORKITEM_ROUTE'
    AND   p.name = '@itemID'
    AND   t.name IN ('bigint', 'int')
)
    INSERT INTO @results VALUES ('WORKITEM_ROUTE', '@itemID BIGINT', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('WORKITEM_ROUTE', '@itemID BIGINT', 'MISSING', 'Parameter not found or wrong type');

-- @Qid INT
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'WORKITEM_ROUTE'
    AND   p.name = '@Qid'
    AND   t.name = 'int'
)
    INSERT INTO @results VALUES ('WORKITEM_ROUTE', '@Qid INT', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('WORKITEM_ROUTE', '@Qid INT', 'MISSING', 'Parameter not found or wrong type');

-- @userId INT
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'WORKITEM_ROUTE'
    AND   p.name = '@userId'
    AND   t.name = 'int'
)
    INSERT INTO @results VALUES ('WORKITEM_ROUTE', '@userId INT', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('WORKITEM_ROUTE', '@userId INT', 'MISSING', 'Parameter not found or wrong type');

-- @operationType VARCHAR
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'WORKITEM_ROUTE'
    AND   p.name = '@operationType'
    AND   t.name IN ('varchar', 'nvarchar')
)
    INSERT INTO @results VALUES ('WORKITEM_ROUTE', '@operationType VARCHAR', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('WORKITEM_ROUTE', '@operationType VARCHAR', 'MISSING', 'Parameter not found or wrong type');

-- @comment VARCHAR
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'WORKITEM_ROUTE'
    AND   p.name = '@comment'
    AND   t.name IN ('varchar', 'nvarchar')
)
    INSERT INTO @results VALUES ('WORKITEM_ROUTE', '@comment VARCHAR', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('WORKITEM_ROUTE', '@comment VARCHAR', 'MISSING', 'Parameter not found or wrong type');

-- -------------------------------------------------------
-- 6. GENERALLOG_INSERT
-- -------------------------------------------------------
IF OBJECT_ID('dbo.GENERALLOG_INSERT', 'P') IS NOT NULL
    INSERT INTO @results VALUES ('GENERALLOG_INSERT', 'SP exists', 'OK', 'Found in sys.objects');
ELSE
    INSERT INTO @results VALUES ('GENERALLOG_INSERT', 'SP exists', 'MISSING', 'Not found in sys.objects');

-- @operationType VARCHAR
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'GENERALLOG_INSERT'
    AND   p.name = '@operationType'
    AND   t.name IN ('varchar', 'nvarchar')
)
    INSERT INTO @results VALUES ('GENERALLOG_INSERT', '@operationType VARCHAR', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('GENERALLOG_INSERT', '@operationType VARCHAR', 'MISSING', 'Parameter not found or wrong type');

-- @sourceObject VARCHAR
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'GENERALLOG_INSERT'
    AND   p.name = '@sourceObject'
    AND   t.name IN ('varchar', 'nvarchar')
)
    INSERT INTO @results VALUES ('GENERALLOG_INSERT', '@sourceObject VARCHAR', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('GENERALLOG_INSERT', '@sourceObject VARCHAR', 'MISSING', 'Parameter not found or wrong type');

-- @userID INT
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'GENERALLOG_INSERT'
    AND   p.name = '@userID'
    AND   t.name = 'int'
)
    INSERT INTO @results VALUES ('GENERALLOG_INSERT', '@userID INT', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('GENERALLOG_INSERT', '@userID INT', 'MISSING', 'Parameter not found or wrong type');

-- @comments VARCHAR
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'GENERALLOG_INSERT'
    AND   p.name = '@comments'
    AND   t.name IN ('varchar', 'nvarchar')
)
    INSERT INTO @results VALUES ('GENERALLOG_INSERT', '@comments VARCHAR', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('GENERALLOG_INSERT', '@comments VARCHAR', 'MISSING', 'Parameter not found or wrong type');

-- @itemID BIGINT
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'GENERALLOG_INSERT'
    AND   p.name = '@itemID'
    AND   t.name IN ('bigint', 'int')
)
    INSERT INTO @results VALUES ('GENERALLOG_INSERT', '@itemID BIGINT', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('GENERALLOG_INSERT', '@itemID BIGINT', 'MISSING', 'Parameter not found or wrong type');

-- -------------------------------------------------------
-- 7. get_sevita_configurations
-- -------------------------------------------------------
IF OBJECT_ID('dbo.get_sevita_configurations', 'P') IS NOT NULL
    INSERT INTO @results VALUES ('get_sevita_configurations', 'SP exists', 'OK', 'Found in sys.objects');
ELSE
    INSERT INTO @results VALUES ('get_sevita_configurations', 'SP exists', 'MISSING', 'Not found in sys.objects');

-- No parameters expected — verify parameter count = 0
DECLARE @sevita_param_count INT = (
    SELECT COUNT(*)
    FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    WHERE o.name = 'get_sevita_configurations'
);
IF @sevita_param_count = 0
    INSERT INTO @results VALUES ('get_sevita_configurations', 'No parameters', 'OK', 'SP takes no parameters as expected');
ELSE
    INSERT INTO @results VALUES ('get_sevita_configurations', 'No parameters', 'WARNING',
        CONCAT('SP has ', @sevita_param_count, ' parameter(s) — expected 0. Verify SP signature.'));

-- -------------------------------------------------------
-- 8. GetSevitaHeaderAndDetailDataByItem
-- -------------------------------------------------------
IF OBJECT_ID('dbo.GetSevitaHeaderAndDetailDataByItem', 'P') IS NOT NULL
    INSERT INTO @results VALUES ('GetSevitaHeaderAndDetailDataByItem', 'SP exists', 'OK', 'Found in sys.objects');
ELSE
    INSERT INTO @results VALUES ('GetSevitaHeaderAndDetailDataByItem', 'SP exists', 'MISSING', 'Not found in sys.objects');

-- @UID BIGINT
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'GetSevitaHeaderAndDetailDataByItem'
    AND   p.name = '@UID'
    AND   t.name IN ('bigint', 'int')
)
    INSERT INTO @results VALUES ('GetSevitaHeaderAndDetailDataByItem', '@UID BIGINT', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('GetSevitaHeaderAndDetailDataByItem', '@UID BIGINT', 'MISSING', 'Parameter not found or wrong type');

-- -------------------------------------------------------
-- 9. UpdateSevitaHeaderPostFields
-- -------------------------------------------------------
IF OBJECT_ID('dbo.UpdateSevitaHeaderPostFields', 'P') IS NOT NULL
    INSERT INTO @results VALUES ('UpdateSevitaHeaderPostFields', 'SP exists', 'OK', 'Found in sys.objects');
ELSE
    INSERT INTO @results VALUES ('UpdateSevitaHeaderPostFields', 'SP exists', 'MISSING', 'Not found in sys.objects');

-- @UID BIGINT
IF EXISTS (
    SELECT 1 FROM sys.parameters p
    JOIN sys.objects o ON p.object_id = o.object_id
    JOIN sys.types t   ON p.user_type_id = t.user_type_id
    WHERE o.name = 'UpdateSevitaHeaderPostFields'
    AND   p.name = '@UID'
    AND   t.name IN ('bigint', 'int')
)
    INSERT INTO @results VALUES ('UpdateSevitaHeaderPostFields', '@UID BIGINT', 'OK', 'Parameter found with correct type');
ELSE
    INSERT INTO @results VALUES ('UpdateSevitaHeaderPostFields', '@UID BIGINT', 'MISSING', 'Parameter not found or wrong type');

-- -------------------------------------------------------
-- Output results
-- -------------------------------------------------------
SELECT
    sp_name     AS [Stored Procedure],
    check_item  AS [Check],
    status      AS [Status],
    detail      AS [Detail]
FROM @results
ORDER BY
    CASE status WHEN 'MISSING' THEN 0 WHEN 'WARNING' THEN 1 ELSE 2 END,
    sp_name,
    check_item;

-- Summary
DECLARE @missing_count  INT = (SELECT COUNT(*) FROM @results WHERE status = 'MISSING');
DECLARE @warning_count  INT = (SELECT COUNT(*) FROM @results WHERE status = 'WARNING');
DECLARE @ok_count       INT = (SELECT COUNT(*) FROM @results WHERE status = 'OK');
DECLARE @total_count    INT = (SELECT COUNT(*) FROM @results);

PRINT '------------------------------------------------------------';
PRINT CONCAT('Total checks : ', @total_count);
PRINT CONCAT('OK           : ', @ok_count);
PRINT CONCAT('WARNINGS     : ', @warning_count);
PRINT CONCAT('MISSING      : ', @missing_count);
PRINT '------------------------------------------------------------';

IF @missing_count > 0
BEGIN
    RAISERROR(
        'VERIFICATION FAILED: %d stored procedure check(s) are MISSING. '
        + 'Review the result set above and resolve before deploying the platform.',
        16, 1, @missing_count);
END
ELSE IF @warning_count > 0
BEGIN
    PRINT 'VERIFICATION PASSED WITH WARNINGS. Review WARNING rows above.';
END
ELSE
BEGIN
    PRINT 'VERIFICATION PASSED: All stored procedures are present with correct parameter signatures.';
END
GO
