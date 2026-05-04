-- =============================================================================
-- validate_invitedclub_history.sql
-- UAT Validation — InvitedClub History Record Comparison
-- =============================================================================
--
-- PURPOSE:
--   Compare post_to_invitedclub_history records written by the old Windows
--   Service vs the new IPS AutoPost Platform for the same ItemIds, processed
--   in the last 24 hours.
--
-- COLUMNS COMPARED:
--   - InvoiceId              — Oracle Fusion invoice identifier (HTTP 201 response)
--   - AttachedDocumentId     — Oracle Fusion attachment identifier (HTTP 201 response)
--   - InvoiceRequestJson     — structure comparison (key presence, not exact value)
--   - Routing                — destination queue (StatusId after WORKITEM_ROUTE)
--
-- RESULT SET:
--   One row per ItemId where a column-level difference is detected.
--   Columns: ItemId, ColumnName, OldValue, NewValue
--
-- NOTE:
--   InvoiceRequestJson exact values may differ between platforms due to
--   timestamp fields. This script checks structural equivalence (key presence)
--   using JSON_VALUE comparisons on known fields.
-- =============================================================================

USE [Workflow];
GO

SET NOCOUNT ON;
GO

PRINT '=== InvitedClub History Record Comparison ===';
PRINT CONCAT('Timestamp : ', CONVERT(VARCHAR, GETUTCDATE(), 126));
PRINT CONCAT('Window    : Last 24 hours (since ', CONVERT(VARCHAR, DATEADD(HOUR, -24, GETUTCDATE()), 126), ')');
PRINT '';
GO

-- ---------------------------------------------------------------------------
-- CTE: Old platform history (post_to_invitedclub_history)
-- ---------------------------------------------------------------------------
WITH OldHistory AS (
    SELECT
        h.ItemId,
        h.InvoiceId,
        h.AttachedDocumentId,
        h.InvoiceRequestJson,
        -- Routing: final StatusId from Workitems after old platform processed
        w.StatusId                                          AS RoutingStatusId,
        -- Structural checks on InvoiceRequestJson
        JSON_VALUE(h.InvoiceRequestJson, '$.invoiceNumber') AS InvoiceNumber,
        JSON_VALUE(h.InvoiceRequestJson, '$.invoiceDate')   AS InvoiceDate,
        JSON_VALUE(h.InvoiceRequestJson, '$.vendorId')      AS VendorId,
        CASE WHEN JSON_QUERY(h.InvoiceRequestJson, '$.invoiceLines') IS NOT NULL
             THEN 1 ELSE 0 END                              AS HasInvoiceLines,
        CASE WHEN JSON_QUERY(h.InvoiceRequestJson, '$.attachments') IS NOT NULL
             THEN 1 ELSE 0 END                              AS HasAttachments,
        h.PostDate
    FROM dbo.post_to_invitedclub_history h
    INNER JOIN dbo.Workitems w ON w.ItemId = h.ItemId
    WHERE
        h.PostDate >= DATEADD(HOUR, -24, GETUTCDATE())
        AND (h.ManuallyPosted IS NULL OR h.ManuallyPosted = 0)
),

-- ---------------------------------------------------------------------------
-- CTE: New platform history (generic_post_history for INVITEDCLUB)
-- ---------------------------------------------------------------------------
NewHistory AS (
    SELECT
        gph.item_id                                                     AS ItemId,
        -- InvoiceId stored in post_response JSON from the invoice POST step
        JSON_VALUE(gph.post_response, '$.InvoiceId')                    AS InvoiceId,
        -- AttachedDocumentId stored in post_response JSON from attachment step
        JSON_VALUE(gph.post_response, '$.AttachedDocumentId')           AS AttachedDocumentId,
        gph.post_request                                                AS InvoiceRequestJson,
        CAST(gph.destination_queue_id AS INT)                           AS RoutingStatusId,
        -- Structural checks on post_request JSON
        JSON_VALUE(gph.post_request, '$.invoiceNumber')                 AS InvoiceNumber,
        JSON_VALUE(gph.post_request, '$.invoiceDate')                   AS InvoiceDate,
        JSON_VALUE(gph.post_request, '$.vendorId')                      AS VendorId,
        CASE WHEN JSON_QUERY(gph.post_request, '$.invoiceLines') IS NOT NULL
             THEN 1 ELSE 0 END                                          AS HasInvoiceLines,
        CASE WHEN JSON_QUERY(gph.post_request, '$.attachments') IS NOT NULL
             THEN 1 ELSE 0 END                                          AS HasAttachments,
        gph.post_date
    FROM dbo.generic_post_history gph
    INNER JOIN dbo.generic_job_configuration gjc ON gjc.id = gph.job_config_id
    WHERE
        gjc.client_type = 'INVITEDCLUB'
        AND gph.post_date >= DATEADD(HOUR, -24, GETUTCDATE())
        AND gph.step_name = 'POST_INVOICE'
),

-- ---------------------------------------------------------------------------
-- CTE: Column-level differences (UNPIVOT approach via UNION ALL)
-- ---------------------------------------------------------------------------
Differences AS (
    -- InvoiceId mismatch
    SELECT
        COALESCE(o.ItemId, n.ItemId) AS ItemId,
        'InvoiceId'                  AS ColumnName,
        CAST(o.InvoiceId AS VARCHAR(200))   AS OldValue,
        CAST(n.InvoiceId AS VARCHAR(200))   AS NewValue
    FROM OldHistory o
    FULL OUTER JOIN NewHistory n ON o.ItemId = n.ItemId
    WHERE ISNULL(o.InvoiceId, '') <> ISNULL(n.InvoiceId, '')

    UNION ALL

    -- AttachedDocumentId mismatch
    SELECT
        COALESCE(o.ItemId, n.ItemId),
        'AttachedDocumentId',
        CAST(o.AttachedDocumentId AS VARCHAR(200)),
        CAST(n.AttachedDocumentId AS VARCHAR(200))
    FROM OldHistory o
    FULL OUTER JOIN NewHistory n ON o.ItemId = n.ItemId
    WHERE ISNULL(CAST(o.AttachedDocumentId AS VARCHAR(200)), '')
       <> ISNULL(CAST(n.AttachedDocumentId AS VARCHAR(200)), '')

    UNION ALL

    -- Routing (StatusId) mismatch
    SELECT
        COALESCE(o.ItemId, n.ItemId),
        'RoutingStatusId',
        CAST(o.RoutingStatusId AS VARCHAR(50)),
        CAST(n.RoutingStatusId AS VARCHAR(50))
    FROM OldHistory o
    FULL OUTER JOIN NewHistory n ON o.ItemId = n.ItemId
    WHERE ISNULL(CAST(o.RoutingStatusId AS VARCHAR(50)), '')
       <> ISNULL(CAST(n.RoutingStatusId AS VARCHAR(50)), '')

    UNION ALL

    -- InvoiceNumber structural check
    SELECT
        COALESCE(o.ItemId, n.ItemId),
        'InvoiceRequestJson.invoiceNumber',
        o.InvoiceNumber,
        n.InvoiceNumber
    FROM OldHistory o
    FULL OUTER JOIN NewHistory n ON o.ItemId = n.ItemId
    WHERE ISNULL(o.InvoiceNumber, '') <> ISNULL(n.InvoiceNumber, '')

    UNION ALL

    -- HasInvoiceLines structural check
    SELECT
        COALESCE(o.ItemId, n.ItemId),
        'InvoiceRequestJson.hasInvoiceLines',
        CAST(o.HasInvoiceLines AS VARCHAR(10)),
        CAST(n.HasInvoiceLines AS VARCHAR(10))
    FROM OldHistory o
    FULL OUTER JOIN NewHistory n ON o.ItemId = n.ItemId
    WHERE ISNULL(o.HasInvoiceLines, 0) <> ISNULL(n.HasInvoiceLines, 0)

    UNION ALL

    -- HasAttachments structural check
    SELECT
        COALESCE(o.ItemId, n.ItemId),
        'InvoiceRequestJson.hasAttachments',
        CAST(o.HasAttachments AS VARCHAR(10)),
        CAST(n.HasAttachments AS VARCHAR(10))
    FROM OldHistory o
    FULL OUTER JOIN NewHistory n ON o.ItemId = n.ItemId
    WHERE ISNULL(o.HasAttachments, 0) <> ISNULL(n.HasAttachments, 0)
)

-- ---------------------------------------------------------------------------
-- Final result: column-level differences
-- ---------------------------------------------------------------------------
SELECT
    ItemId,
    ColumnName,
    OldValue,
    NewValue
FROM Differences
ORDER BY ItemId, ColumnName;
GO

-- ---------------------------------------------------------------------------
-- Summary
-- ---------------------------------------------------------------------------
PRINT '';
PRINT '=== Summary ===';

DECLARE @old_count INT = (
    SELECT COUNT(DISTINCT h.ItemId)
    FROM dbo.post_to_invitedclub_history h
    WHERE h.PostDate >= DATEADD(HOUR, -24, GETUTCDATE())
      AND (h.ManuallyPosted IS NULL OR h.ManuallyPosted = 0)
);

DECLARE @new_count INT = (
    SELECT COUNT(DISTINCT gph.item_id)
    FROM dbo.generic_post_history gph
    INNER JOIN dbo.generic_job_configuration gjc ON gjc.id = gph.job_config_id
    WHERE gjc.client_type = 'INVITEDCLUB'
      AND gph.post_date >= DATEADD(HOUR, -24, GETUTCDATE())
      AND gph.step_name = 'POST_INVOICE'
);

PRINT CONCAT('Old platform items (last 24h) : ', @old_count);
PRINT CONCAT('New platform items (last 24h) : ', @new_count);
GO
