-- ============================================================
-- Seed Script: generic_execution_schedule — Sevita
-- ============================================================
-- Source SP    : GetExecutionSchedule (@file_creation_config_id, @job_id)
-- Source field : post_to_sevita_configuration.post_time  (HH:mm fallback)
-- Target table : generic_execution_schedule
-- Depends on   : 03_seed_sevita_job_configuration.sql (must run first)
--
-- IMPORTANT: This script is IDEMPOTENT.
--   - It uses MERGE keyed on (job_config_id, schedule_type, execution_time).
--   - It does NOT modify any existing stored procedures.
--   - It does NOT alter post_to_sevita_configuration.
--
-- Strategy:
--   For each Sevita row in generic_job_configuration, call
--   GetExecutionSchedule(@file_creation_config_id = gjc.id, @job_id = gjc.job_id)
--   via INSERT ... EXEC and collect the returned execution_time rows as
--   'POST' schedule entries.
--
--   Fallback: If GetExecutionSchedule returns no rows for a job, use the
--   post_time field from client_config_json (migrated from
--   post_to_sevita_configuration.post_time in script 03).
--
--   Sevita has no feed download step (download_feed = 0), so no
--   'DOWNLOAD' schedule rows are created.
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
-- Step 1: Temp table to accumulate schedule rows.
-- -------------------------------------------------------
CREATE TABLE #sevita_schedules (
    job_config_id   INT          NOT NULL,
    schedule_type   VARCHAR(20)  NOT NULL,
    execution_time  VARCHAR(10)  NULL,
    cron_expression VARCHAR(100) NULL
);

-- Temp table to capture the SP result set per iteration.
CREATE TABLE #sp_result (
    ExecutionTime VARCHAR(10) NULL
);

-- -------------------------------------------------------
-- Step 2: Loop over each Sevita job configuration and
--         call GetExecutionSchedule to get its POST schedules.
-- -------------------------------------------------------
DECLARE @job_config_id  INT;
DECLARE @job_id         INT;
DECLARE @sp_row_count   INT;

DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT id, job_id
    FROM   dbo.generic_job_configuration
    WHERE  client_type = 'SEVITA'
    AND    is_active   = 1;

OPEN cur;
FETCH NEXT FROM cur INTO @job_config_id, @job_id;

WHILE @@FETCH_STATUS = 0
BEGIN
    TRUNCATE TABLE #sp_result;

    -- Call the existing SP with the exact parameter names it expects.
    INSERT INTO #sp_result (ExecutionTime)
    EXEC dbo.GetExecutionSchedule
        @file_creation_config_id = @job_config_id,
        @job_id                  = @job_id;

    SET @sp_row_count = @@ROWCOUNT;

    IF @sp_row_count > 0
    BEGIN
        -- Use SP result rows.
        INSERT INTO #sevita_schedules (job_config_id, schedule_type, execution_time, cron_expression)
        SELECT
            @job_config_id,
            'POST',
            r.ExecutionTime,
            NULL
        FROM #sp_result r
        WHERE r.ExecutionTime IS NOT NULL
        AND   LTRIM(RTRIM(r.ExecutionTime)) <> '';
    END
    ELSE
    BEGIN
        -- Fallback: use post_time from client_config_json.
        -- post_time was migrated from post_to_sevita_configuration.post_time in script 03.
        INSERT INTO #sevita_schedules (job_config_id, schedule_type, execution_time, cron_expression)
        SELECT
            gjc.id,
            'POST',
            JSON_VALUE(gjc.client_config_json, '$.postTime'),
            NULL
        FROM dbo.generic_job_configuration gjc
        WHERE gjc.id = @job_config_id
        AND   JSON_VALUE(gjc.client_config_json, '$.postTime') IS NOT NULL
        AND   LTRIM(RTRIM(JSON_VALUE(gjc.client_config_json, '$.postTime'))) <> '';
    END

    FETCH NEXT FROM cur INTO @job_config_id, @job_id;
END

CLOSE cur;
DEALLOCATE cur;
DROP TABLE #sp_result;

-- -------------------------------------------------------
-- Step 3: MERGE temp table into generic_execution_schedule.
-- -------------------------------------------------------
MERGE INTO dbo.generic_execution_schedule AS tgt
USING (
    SELECT DISTINCT
        job_config_id,
        schedule_type,
        execution_time,
        cron_expression
    FROM #sevita_schedules
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
-- Step 4: Verification
-- -------------------------------------------------------
DECLARE @schedule_count INT = (
    SELECT COUNT(*)
    FROM   dbo.generic_execution_schedule ges
    JOIN   dbo.generic_job_configuration  gjc ON ges.job_config_id = gjc.id
    WHERE  gjc.client_type = 'SEVITA'
);

PRINT CONCAT('generic_execution_schedule rows for SEVITA: ', @schedule_count);

IF @schedule_count = 0
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(
        'No schedule rows were inserted for SEVITA — rolling back. '
        + 'Verify GetExecutionSchedule returns data for these job IDs, '
        + 'or that post_time is populated in post_to_sevita_configuration.',
        16, 1);
    RETURN;
END

DROP TABLE #sevita_schedules;

COMMIT TRANSACTION;
PRINT 'Sevita generic_execution_schedule seed completed successfully.';
GO
