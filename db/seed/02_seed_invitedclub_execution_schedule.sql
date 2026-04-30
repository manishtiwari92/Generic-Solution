-- ============================================================
-- Seed Script: generic_execution_schedule — InvitedClub
-- ============================================================
-- Source SP    : GetExecutionSchedule (@file_creation_config_id, @job_id)
-- Target table : generic_execution_schedule
-- Depends on   : 01_seed_invitedclub_job_configuration.sql (must run first)
--
-- IMPORTANT: This script is IDEMPOTENT.
--   - It uses MERGE keyed on (job_config_id, schedule_type, execution_time).
--   - It does NOT modify any existing stored procedures.
--   - It does NOT alter post_to_invitedclub_configuration.
--
-- Strategy:
--   For each InvitedClub row in generic_job_configuration, call
--   GetExecutionSchedule(@file_creation_config_id = gjc.id, @job_id = gjc.job_id)
--   via INSERT ... EXEC and collect the returned execution_time rows as
--   'POST' schedule entries.
--
--   Additionally, if download_feed = 1, insert a 'DOWNLOAD' schedule entry
--   using feed_download_time from client_config_json (the feedDownloadTime
--   field migrated in script 01).
--
-- Column mapping:
--   GetExecutionSchedule result.ExecutionTime  ->  execution_time  (HH:mm)
--   schedule_type                              ->  'POST'
--   job_config_id                              ->  generic_job_configuration.id
--   is_active                                  ->  1
-- ============================================================

USE [Workflow];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

-- -------------------------------------------------------
-- Step 1: Temp table to accumulate schedule rows from
--         GetExecutionSchedule for all InvitedClub jobs.
-- -------------------------------------------------------
CREATE TABLE #invitedclub_schedules (
    job_config_id   INT          NOT NULL,
    schedule_type   VARCHAR(20)  NOT NULL,
    execution_time  VARCHAR(10)  NULL,
    cron_expression VARCHAR(100) NULL
);

-- Temp table to capture the SP result set per iteration.
-- GetExecutionSchedule returns at minimum an ExecutionTime column.
CREATE TABLE #sp_result (
    ExecutionTime VARCHAR(10) NULL
);

-- -------------------------------------------------------
-- Step 2: Loop over each InvitedClub job configuration and
--         call GetExecutionSchedule to get its POST schedules.
-- -------------------------------------------------------
DECLARE @job_config_id  INT;
DECLARE @job_id         INT;

DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT id, job_id
    FROM   dbo.generic_job_configuration
    WHERE  client_type = 'INVITEDCLUB'
    AND    is_active   = 1;

OPEN cur;
FETCH NEXT FROM cur INTO @job_config_id, @job_id;

WHILE @@FETCH_STATUS = 0
BEGIN
    -- Clear previous iteration results.
    TRUNCATE TABLE #sp_result;

    -- Call the existing SP with the exact parameter names it expects.
    -- INSERT ... EXEC is the standard SQL Server pattern for capturing SP results.
    INSERT INTO #sp_result (ExecutionTime)
    EXEC dbo.GetExecutionSchedule
        @file_creation_config_id = @job_config_id,
        @job_id                  = @job_id;

    -- Copy non-empty rows into the accumulator.
    INSERT INTO #invitedclub_schedules (job_config_id, schedule_type, execution_time, cron_expression)
    SELECT
        @job_config_id,
        'POST',
        r.ExecutionTime,
        NULL            -- legacy SP returns HH:mm only; no cron expression
    FROM #sp_result r
    WHERE r.ExecutionTime IS NOT NULL
    AND   LTRIM(RTRIM(r.ExecutionTime)) <> '';

    FETCH NEXT FROM cur INTO @job_config_id, @job_id;
END

CLOSE cur;
DEALLOCATE cur;
DROP TABLE #sp_result;

-- -------------------------------------------------------
-- Step 3: Add DOWNLOAD schedule rows for InvitedClub jobs
--         that have download_feed = 1.
--         The feed_download_time is stored in client_config_json
--         as the "feedDownloadTime" property (HH:mm format).
-- -------------------------------------------------------
INSERT INTO #invitedclub_schedules (job_config_id, schedule_type, execution_time, cron_expression)
SELECT
    gjc.id,
    'DOWNLOAD',
    JSON_VALUE(gjc.client_config_json, '$.feedDownloadTime'),
    NULL
FROM dbo.generic_job_configuration gjc
WHERE gjc.client_type = 'INVITEDCLUB'
AND   gjc.download_feed = 1
AND   JSON_VALUE(gjc.client_config_json, '$.feedDownloadTime') IS NOT NULL
AND   LTRIM(RTRIM(JSON_VALUE(gjc.client_config_json, '$.feedDownloadTime'))) <> '';

-- -------------------------------------------------------
-- Step 4: MERGE temp table into generic_execution_schedule.
-- -------------------------------------------------------
MERGE INTO dbo.generic_execution_schedule AS tgt
USING (
    SELECT DISTINCT
        job_config_id,
        schedule_type,
        execution_time,
        cron_expression
    FROM #invitedclub_schedules
    WHERE execution_time  IS NOT NULL
    OR    cron_expression IS NOT NULL
) AS src
ON (
    tgt.job_config_id = src.job_config_id
    AND tgt.schedule_type = src.schedule_type
    AND (
        (tgt.execution_time  = src.execution_time  AND src.execution_time  IS NOT NULL)
        OR
        (tgt.cron_expression = src.cron_expression AND src.cron_expression IS NOT NULL)
    )
)
WHEN MATCHED THEN
    UPDATE SET
        tgt.is_active = 1
WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        job_config_id,
        schedule_type,
        execution_time,
        cron_expression,
        is_active
    )
    VALUES (
        src.job_config_id,
        src.schedule_type,
        src.execution_time,
        src.cron_expression,
        1
    );

-- -------------------------------------------------------
-- Step 5: Verification
-- -------------------------------------------------------
DECLARE @schedule_count INT = (
    SELECT COUNT(*)
    FROM   dbo.generic_execution_schedule ges
    JOIN   dbo.generic_job_configuration  gjc ON ges.job_config_id = gjc.id
    WHERE  gjc.client_type = 'INVITEDCLUB'
);

PRINT CONCAT('generic_execution_schedule rows for INVITEDCLUB: ', @schedule_count);

IF @schedule_count = 0
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(
        'No schedule rows were inserted for INVITEDCLUB — rolling back. '
        + 'Verify GetExecutionSchedule returns data for these job IDs.',
        16, 1);
    RETURN;
END

DROP TABLE #invitedclub_schedules;

COMMIT TRANSACTION;
PRINT 'InvitedClub generic_execution_schedule seed completed successfully.';
GO
