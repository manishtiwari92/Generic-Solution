-- =============================================================================
-- validate_invitedclub_routing.sql
-- UAT Validation — InvitedClub Routing Outcome Comparison
-- =============================================================================
--
-- PURPOSE:
--   Compare routing outcomes (StatusId / queue position) between the old
--   Windows Service and the new IPS AutoPost Platform for the same ItemIds,
--   processed in the last 24 hours.
--
-- HOW IT WORKS:
--   The old platform writes routing history via WORKITEM_ROUTE SP with
--   operationType = 'Post To InvitedClubs' and sourceObject = 'Contents'.
--
--   The new platform writes routing history via the same WORKITEM_ROUTE SP
--   but with an additional platform_source marker in generic_execution_history.
--
--   Both platforms update Workitems.StatusId for the same ItemId.
--   We compare the final StatusId recorded in post_to_invitedclub_history
--   (old platform) vs generic_post_history (new platform) for the same ItemId.
--
-- RESULT SET COLUMNS:
--   ItemId        — the workitem identifier
--   OldStatusId   — destination queue from old platform (post_to_invitedclub_history)
--   NewStatusId   — destination queue from new platform (generic_post_history)
--   Mismatch      — 'YES' if the two StatusIds differ, 'NO' if they match
--
-- EXPECTED OUTCOME:
--   Zero rows with Mismatch = 'YES' indicates the new platform routes identically.
-- =============================================================================

USE [Workflow];
GO

SET NOCOUNT ON;
GO

PRINT '=== InvitedClub Routing Outcome Validation ===';
PRINT CONCAT('Timestamp : ', CONVERT(VARCHAR, GETUTCDATE(), 126));
PRINT CONCAT('Window    : Last 24 hours (since ', CONVERT(VARCHAR, DATEADD(HOUR, -24, GETUTCDATE()), 126), ')');
PRINT '';
GO

-- ---------------------------------------------------------------------------
-- CTE: Old platform routing outcomes
--   Source: post_to_invitedclub_history
--   The old Windows Service inserts a row per ItemId with the destination
--   queue captured as the Workitems.StatusId at time of routing.
-- ---------------------------------------------------------------------------
WITH OldPlatform AS (
    SELECT
        h.ItemId,
        -- The old platform routes via WORKITEM_ROUTE which updates Workitems.StatusId.
        -- We read the final StatusId from Workitems for items processed by old platform.
        w.StatusId AS OldStatusId,
        h.PostDate
    FROM dbo.post_to_invitedclub_history h
    INNER JOIN dbo.Workitems w
        ON w.ItemId = h.ItemId
    WHERE
        h.PostDate >= DATEADD(HOUR, -24, GETUTCDATE())
        AND (h.ManuallyPosted IS NULL OR h.ManuallyPosted = 0)
),

-- ---------------------------------------------------------------------------
-- CTE: New platform routing outcomes
--   Source: generic_post_history
--   The new platform inserts a row per ItemId with step_name and post_date.
-- ---------------------------------------------------------------------------
NewPlatform AS (
    SELECT
        gph.item_id                 AS ItemId,
        -- destination_queue_id is stored in generic_post_history for the new platform
        CAST(gph.destination_queue_id AS INT) AS NewStatusId,
        gph.post_date
    FROM dbo.generic_post_history gph
    INNER JOIN dbo.generic_job_configuration gjc
        ON gjc.id = gph.job_config_id
    WHERE
        gjc.client_type = 'INVITEDCLUB'
        AND gph.post_date >= DATEADD(HOUR, -24, GETUTCDATE())
        AND gph.step_name = 'ROUTE'
)

-- ---------------------------------------------------------------------------
-- Comparison: items processed by BOTH platforms in the last 24 hours
-- ---------------------------------------------------------------------------
SELECT
    COALESCE(o.ItemId, n.ItemId)    AS ItemId,
    o.OldStatusId,
    n.NewStatusId,
    CASE
        WHEN o.OldStatusId IS NULL      THEN 'OLD_MISSING'
        WHEN n.NewStatusId IS NULL      THEN 'NEW_MISSING'
        WHEN o.OldStatusId = n.NewStatusId THEN 'NO'
        ELSE 'YES'
    END                             AS Mismatch
FROM OldPlatform o
FULL OUTER JOIN NewPlatform n
    ON o.ItemId = n.ItemId
ORDER BY
    CASE
        WHEN o.OldStatusId IS NULL OR n.NewStatusId IS NULL THEN 0
        WHEN o.OldStatusId <> n.NewStatusId THEN 1
        ELSE 2
    END,
    COALESCE(o.ItemId, n.ItemId);
GO

-- ---------------------------------------------------------------------------
-- Summary counts
-- ---------------------------------------------------------------------------
PRINT '';
PRINT '=== Summary ===';

WITH OldPlatform AS (
    SELECT h.ItemId, w.StatusId AS OldStatusId
    FROM dbo.post_to_invitedclub_history h
    INNER JOIN dbo.Workitems w ON w.ItemId = h.ItemId
    WHERE h.PostDate >= DATEADD(HOUR, -24, GETUTCDATE())
      AND (h.ManuallyPosted IS NULL OR h.ManuallyPosted = 0)
),
NewPlatform AS (
    SELECT gph.item_id AS ItemId, CAST(gph.destination_queue_id AS INT) AS NewStatusId
    FROM dbo.generic_post_history gph
    INNER JOIN dbo.generic_job_configuration gjc ON gjc.id = gph.job_config_id
    WHERE gjc.client_type = 'INVITEDCLUB'
      AND gph.post_date >= DATEADD(HOUR, -24, GETUTCDATE())
      AND gph.step_name = 'ROUTE'
),
Comparison AS (
    SELECT
        COALESCE(o.ItemId, n.ItemId) AS ItemId,
        o.OldStatusId,
        n.NewStatusId,
        CASE
            WHEN o.OldStatusId IS NULL OR n.NewStatusId IS NULL THEN 'MISSING'
            WHEN o.OldStatusId = n.NewStatusId THEN 'MATCH'
            ELSE 'MISMATCH'
        END AS Result
    FROM OldPlatform o
    FULL OUTER JOIN NewPlatform n ON o.ItemId = n.ItemId
)
SELECT
    COUNT(*)                                        AS TotalItemsCompared,
    SUM(CASE WHEN Result = 'MATCH'    THEN 1 ELSE 0 END) AS Matches,
    SUM(CASE WHEN Result = 'MISMATCH' THEN 1 ELSE 0 END) AS Mismatches,
    SUM(CASE WHEN Result = 'MISSING'  THEN 1 ELSE 0 END) AS MissingFromOnePlatform
FROM Comparison;
GO
