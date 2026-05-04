-- =============================================================================
-- enable_invitedclub_parallel_run.sql
-- UAT Parallel Run Setup — InvitedClub
-- =============================================================================
--
-- PURPOSE:
--   Enable the new IPS AutoPost Platform to run in parallel with the existing
--   Windows Service for InvitedClub invoice posting.
--
-- PARALLEL RUN ARCHITECTURE:
--   During the UAT parallel run, BOTH systems process the same workitems:
--
--   OLD SYSTEM (Windows Service):
--     - Reads configuration from: post_to_invitedclub_configuration
--     - Controlled by: post_to_invitedclub_configuration.allow_auto_post
--     - Writes history to: post_to_invitedclub_history
--
--   NEW SYSTEM (IPS AutoPost Platform — ECS Fargate):
--     - Reads configuration from: generic_job_configuration
--     - Controlled by: generic_job_configuration.allow_auto_post  <-- this script
--     - Writes history to: post_to_invitedclub_history (same table, new platform_source='NEW')
--
--   Both systems read from the same Workitems table and route via WORKITEM_ROUTE SP.
--   Routing outcomes are compared using validate_invitedclub_routing.sql.
--   History records are compared using validate_invitedclub_history.sql.
--
-- ROLLBACK:
--   To disable the new platform: UPDATE generic_job_configuration
--     SET allow_auto_post = 0 WHERE client_type = 'INVITEDCLUB'
--
-- PREREQUISITES:
--   - Seed scripts 8.1 and 8.2 must have been run (InvitedClub config + schedule)
--   - ECS Fargate services must be deployed and healthy
--   - UAT environment only — do NOT run in production without sign-off
-- =============================================================================

USE [Workflow];
GO

SET NOCOUNT ON;
GO

PRINT '=== InvitedClub Parallel Run Enable ===';
PRINT CONCAT('Timestamp: ', CONVERT(VARCHAR, GETUTCDATE(), 126));
GO

-- ---------------------------------------------------------------------------
-- Step 1: Enable allow_auto_post for InvitedClub in generic_job_configuration
-- ---------------------------------------------------------------------------
BEGIN TRANSACTION;

UPDATE dbo.generic_job_configuration
SET
    allow_auto_post = 1,
    modified_date   = GETUTCDATE()
WHERE
    client_type = 'INVITEDCLUB';

DECLARE @rows_updated INT = @@ROWCOUNT;
PRINT CONCAT('Rows updated: ', @rows_updated);

IF @rows_updated = 0
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(
        'No rows updated. Ensure seed script 8.1 (01_seed_invitedclub_job_configuration.sql) has been run first.',
        16, 1
    );
    RETURN;
END

COMMIT TRANSACTION;
PRINT 'allow_auto_post = 1 set for all INVITEDCLUB jobs.';
GO

-- ---------------------------------------------------------------------------
-- Step 2: Verification SELECT — confirm the change
-- ---------------------------------------------------------------------------
PRINT '';
PRINT '=== Verification: generic_job_configuration (INVITEDCLUB) ===';

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
    secondary_fail_queue_id,
    last_post_time,
    modified_date
FROM dbo.generic_job_configuration
WHERE client_type = 'INVITEDCLUB'
ORDER BY job_id;
GO

PRINT '';
PRINT 'InvitedClub parallel run ENABLED.';
PRINT 'Both the Windows Service and the new ECS platform will now process InvitedClub workitems.';
PRINT 'Run validate_invitedclub_routing.sql after the next scheduled post to compare outcomes.';
GO
