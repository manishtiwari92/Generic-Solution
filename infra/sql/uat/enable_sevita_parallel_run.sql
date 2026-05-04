-- =============================================================================
-- enable_sevita_parallel_run.sql
-- UAT Parallel Run Setup — Sevita
-- =============================================================================
--
-- PURPOSE:
--   Enable the new IPS AutoPost Platform to run in parallel with the existing
--   Windows Service for Sevita invoice posting.
--
-- PARALLEL RUN ARCHITECTURE:
--   OLD SYSTEM (Windows Service):
--     - Reads configuration from: post_to_sevita_configuration
--     - Controlled by: post_to_sevita_configuration.allow_auto_post
--     - Writes history to: sevita_posted_records_history
--
--   NEW SYSTEM (IPS AutoPost Platform — ECS Fargate):
--     - Reads configuration from: generic_job_configuration
--     - Controlled by: generic_job_configuration.allow_auto_post  <-- this script
--     - Writes history to: sevita_posted_records_history (same table)
--
-- ROLLBACK:
--   UPDATE generic_job_configuration SET allow_auto_post = 0 WHERE client_type = 'SEVITA'
--
-- PREREQUISITES:
--   - Seed scripts 8.3 and 8.4 must have been run (Sevita config + schedule)
--   - ECS Fargate services must be deployed and healthy
--   - UAT environment only
-- =============================================================================

USE [Workflow];
GO

SET NOCOUNT ON;
GO

PRINT '=== Sevita Parallel Run Enable ===';
PRINT CONCAT('Timestamp: ', CONVERT(VARCHAR, GETUTCDATE(), 126));
GO

-- ---------------------------------------------------------------------------
-- Step 1: Enable allow_auto_post for Sevita in generic_job_configuration
-- ---------------------------------------------------------------------------
BEGIN TRANSACTION;

UPDATE dbo.generic_job_configuration
SET
    allow_auto_post = 1,
    modified_date   = GETUTCDATE()
WHERE
    client_type = 'SEVITA';

DECLARE @rows_updated INT = @@ROWCOUNT;
PRINT CONCAT('Rows updated: ', @rows_updated);

IF @rows_updated = 0
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(
        'No rows updated. Ensure seed script 8.3 (03_seed_sevita_job_configuration.sql) has been run first.',
        16, 1
    );
    RETURN;
END

COMMIT TRANSACTION;
PRINT 'allow_auto_post = 1 set for all SEVITA jobs.';
GO

-- ---------------------------------------------------------------------------
-- Step 2: Routing outcome validation query
--   Compares sevita_posted_records_history between old and new platform
--   for items processed in the last 24 hours.
-- ---------------------------------------------------------------------------
PRINT '';
PRINT '=== Routing Outcome Validation: sevita_posted_records_history ===';
PRINT CONCAT('Window: Last 24 hours (since ', CONVERT(VARCHAR, DATEADD(HOUR, -24, GETUTCDATE()), 126), ')');
PRINT '';

WITH OldPlatform AS (
    -- Old Windows Service: rows in sevita_posted_records_history without new platform marker
    SELECT
        h.ItemId,
        w.StatusId          AS RoutingStatusId,
        h.InvoiceId,
        h.PostDate
    FROM dbo.sevita_posted_records_history h
    INNER JOIN dbo.Workitems w ON w.ItemId = h.ItemId
    WHERE
        h.PostDate >= DATEADD(HOUR, -24, GETUTCDATE())
        AND (h.ManuallyPosted IS NULL OR h.ManuallyPosted = 0)
        -- Old platform rows do not have a platform_source column;
        -- distinguish by absence of generic_post_history entry for same ItemId
        AND NOT EXISTS (
            SELECT 1
            FROM dbo.generic_post_history gph
            INNER JOIN dbo.generic_job_configuration gjc ON gjc.id = gph.job_config_id
            WHERE gjc.client_type = 'SEVITA'
              AND gph.item_id = h.ItemId
              AND gph.post_date >= DATEADD(HOUR, -24, GETUTCDATE())
        )
),
NewPlatform AS (
    -- New platform: rows in generic_post_history for SEVITA
    SELECT
        gph.item_id                             AS ItemId,
        CAST(gph.destination_queue_id AS INT)   AS RoutingStatusId,
        JSON_VALUE(gph.post_response, '$.InvoiceId') AS InvoiceId,
        gph.post_date                           AS PostDate
    FROM dbo.generic_post_history gph
    INNER JOIN dbo.generic_job_configuration gjc ON gjc.id = gph.job_config_id
    WHERE
        gjc.client_type = 'SEVITA'
        AND gph.post_date >= DATEADD(HOUR, -24, GETUTCDATE())
        AND gph.step_name = 'ROUTE'
)
SELECT
    COALESCE(o.ItemId, n.ItemId)    AS ItemId,
    o.RoutingStatusId               AS OldStatusId,
    n.RoutingStatusId               AS NewStatusId,
    CASE
        WHEN o.RoutingStatusId IS NULL                      THEN 'OLD_MISSING'
        WHEN n.RoutingStatusId IS NULL                      THEN 'NEW_MISSING'
        WHEN o.RoutingStatusId = n.RoutingStatusId          THEN 'NO'
        ELSE 'YES'
    END                             AS Mismatch,
    o.InvoiceId                     AS OldInvoiceId,
    n.InvoiceId                     AS NewInvoiceId
FROM OldPlatform o
FULL OUTER JOIN NewPlatform n ON o.ItemId = n.ItemId
ORDER BY
    CASE
        WHEN o.RoutingStatusId IS NULL OR n.RoutingStatusId IS NULL THEN 0
        WHEN o.RoutingStatusId <> n.RoutingStatusId THEN 1
        ELSE 2
    END,
    COALESCE(o.ItemId, n.ItemId);
GO

-- ---------------------------------------------------------------------------
-- Step 3: Verification SELECT — confirm the configuration change
-- ---------------------------------------------------------------------------
PRINT '';
PRINT '=== Verification: generic_job_configuration (SEVITA) ===';

SELECT
    id,
    client_type,
    job_id,
    job_name,
    is_active,
    allow_auto_post,
    source_queue_id,
    success_queue_id,
    primary_fail_queue_id,
    last_post_time,
    modified_date
FROM dbo.generic_job_configuration
WHERE client_type = 'SEVITA'
ORDER BY job_id;
GO

PRINT '';
PRINT 'Sevita parallel run ENABLED.';
PRINT 'Run validate_invitedclub_routing.sql equivalent for Sevita after the next scheduled post.';
GO
