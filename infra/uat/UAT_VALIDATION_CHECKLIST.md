# UAT Validation Checklist — IPS AutoPost Platform

**Environment:** UAT  
**Platform:** IPS AutoPost Platform (.NET 10 / AWS ECS Fargate)  
**Date:** _______________  
**Validated by:** _______________  

---

## Overview

This checklist covers the 10 validation steps required to confirm the IPS AutoPost Platform is operating correctly in UAT before enabling the parallel run with the existing Windows Service.

Complete each step in order. Mark Pass/Fail and add notes for any issues found.

---

## Step 1 — Deploy All Three CloudFormation Stacks

**What to run:**
```bash
cd Generic-Solution
chmod +x infra/scripts/deploy-uat.sh

# Dry run first (prints commands without executing)
./infra/scripts/deploy-uat.sh --env uat --dry-run

# Actual deployment
./infra/scripts/deploy-uat.sh --env uat
```

**What to check:**
- Stack 1 (`ips-autopost-infrastructure-uat`): Status = `CREATE_COMPLETE` or `UPDATE_COMPLETE`
- Stack 2 (`ips-autopost-application-uat`): Status = `CREATE_COMPLETE` or `UPDATE_COMPLETE`
- Stack 3 (`ips-autopost-monitoring-uat`): Status = `CREATE_COMPLETE` or `UPDATE_COMPLETE`
- ECS services `ips-autopost-feed-uat` and `ips-autopost-post-uat` show `RUNNING` with 1 task each
- SQS queues `ips-feed-queue-uat` and `ips-post-queue-uat` exist
- CloudWatch log groups `/ips/autopost/feed/uat` and `/ips/autopost/post/uat` exist

**Result:** [ ] Pass  [ ] Fail

**Notes:**
```
_______________________________________________________________
_______________________________________________________________
```

---

## Step 2 — Run Database Migration Scripts

**What to run:**
```bash
export UAT_CONNECTION_STRING="Server=ips-rds-database-1.cmrmduasa2gk.us-east-1.rds.amazonaws.com;Database=Workflow;User ID=IPSAppsUser;Password=<password>;Max Pool Size=2000;"

chmod +x infra/scripts/run-migrations-uat.sh
./infra/scripts/run-migrations-uat.sh
```

**What to check:**
- EF Core migration output shows `Done` with no errors
- Seed script 8.1 (InvitedClub config): `InvitedClub generic_job_configuration seed completed successfully`
- Seed script 8.2 (InvitedClub schedule): schedule rows inserted
- Seed script 8.3 (Sevita config): `Sevita generic_job_configuration seed completed successfully`
- Seed script 8.4 (Sevita schedule): schedule rows inserted
- Verification: `Verification PASSED: All 10 generic tables exist`

**Result:** [ ] Pass  [ ] Fail

**Notes:**
```
_______________________________________________________________
_______________________________________________________________
```

---

## Step 3 — Enable InvitedClub Parallel Run

**What to run:**
```sql
-- Connect to UAT Workflow DB and run:
-- infra/sql/uat/enable_invitedclub_parallel_run.sql
sqlcmd -S ips-rds-database-1.cmrmduasa2gk.us-east-1.rds.amazonaws.com \
       -d Workflow -U IPSAppsUser -P <password> \
       -i infra/sql/uat/enable_invitedclub_parallel_run.sql
```

**What to check:**
- Output: `allow_auto_post = 1 set for all INVITEDCLUB jobs`
- Verification SELECT shows `allow_auto_post = 1` for all INVITEDCLUB rows
- `modified_date` is updated to current UTC time
- No error messages or rollback

**Result:** [ ] Pass  [ ] Fail

**Notes:**
```
_______________________________________________________________
_______________________________________________________________
```

---

## Step 4 — Verify InvitedClub Routing Outcomes

**What to run:**
```sql
-- Run AFTER the next scheduled InvitedClub post (or trigger manually via API)
-- infra/sql/uat/validate_invitedclub_routing.sql
sqlcmd -S ips-rds-database-1.cmrmduasa2gk.us-east-1.rds.amazonaws.com \
       -d Workflow -U IPSAppsUser -P <password> \
       -i infra/sql/uat/validate_invitedclub_routing.sql
```

**What to check:**
- Result set shows 0 rows with `Mismatch = 'YES'`
- Summary shows `Mismatches = 0`
- Both platforms processed the same ItemIds
- `OldStatusId` and `NewStatusId` match for every ItemId

**Result:** [ ] Pass  [ ] Fail

**Notes:**
```
_______________________________________________________________
_______________________________________________________________
Mismatch count: ___
Total items compared: ___
```

---

## Step 5 — Verify InvitedClub History Records

**What to run:**
```sql
-- Run AFTER the next scheduled InvitedClub post
-- infra/sql/uat/validate_invitedclub_history.sql
sqlcmd -S ips-rds-database-1.cmrmduasa2gk.us-east-1.rds.amazonaws.com \
       -d Workflow -U IPSAppsUser -P <password> \
       -i infra/sql/uat/validate_invitedclub_history.sql
```

**What to check:**
- Result set shows 0 column-level differences
- `InvoiceId` matches between old and new platform for the same ItemId
- `AttachedDocumentId` matches
- `InvoiceRequestJson.hasInvoiceLines` matches (structural check)
- `RoutingStatusId` matches
- Summary shows equal item counts for old and new platform

**Result:** [ ] Pass  [ ] Fail

**Notes:**
```
_______________________________________________________________
_______________________________________________________________
Differences found: ___
Old platform items: ___
New platform items: ___
```

---

## Step 6 — Enable Sevita Parallel Run

**What to run:**
```sql
-- infra/sql/uat/enable_sevita_parallel_run.sql
sqlcmd -S ips-rds-database-1.cmrmduasa2gk.us-east-1.rds.amazonaws.com \
       -d Workflow -U IPSAppsUser -P <password> \
       -i infra/sql/uat/enable_sevita_parallel_run.sql
```

**What to check:**
- Output: `allow_auto_post = 1 set for all SEVITA jobs`
- Verification SELECT shows `allow_auto_post = 1` for all SEVITA rows
- Routing outcome validation query (embedded in script) shows 0 mismatches after first Sevita post
- `sevita_posted_records_history` has entries from both platforms

**Result:** [ ] Pass  [ ] Fail

**Notes:**
```
_______________________________________________________________
_______________________________________________________________
```

---

## Step 7 — Validate CloudWatch Metrics

**What to run:**
```powershell
# Requires AWS CLI v2 and PowerShell 7+
# Set AWS credentials first: aws configure or assume role

.\infra\scripts\validate-cloudwatch-metrics.ps1 -Environment uat

# Or with a longer lookback window:
.\infra\scripts\validate-cloudwatch-metrics.ps1 -Environment uat -LookbackMinutes 120
```

**What to check:**
- `[PASS] Metric 'PostSuccessCount' exists in namespace 'IPS/AutoPost/uat'`
- `[PASS] Metric 'PostFailedCount' exists in namespace 'IPS/AutoPost/uat'`
- `[PASS] Metric 'PostSuccessCount' has required dimensions: ClientType=..., JobId=...`
- `[PASS] Metric 'PostFailedCount' has required dimensions: ClientType=..., JobId=...`
- `[PASS]` data points exist in the last hour (requires at least one post to have run)
- Final result: `RESULT: PASS`

**Result:** [ ] Pass  [ ] Fail

**Notes:**
```
_______________________________________________________________
_______________________________________________________________
PASS count: ___
FAIL count: ___
```

---

## Step 8 — Validate DLQ Alarms

**What to run:**
```bash
# IMPORTANT: Temporarily reduce ips-post-queue-uat visibility timeout to 30s before running
# aws sqs set-queue-attributes --queue-url <url> --attributes VisibilityTimeout=30
# Restore to 7200 after the test.

chmod +x infra/scripts/validate-dlq-alarms.sh
./infra/scripts/validate-dlq-alarms.sh --env uat

# To keep DLQ messages for inspection:
./infra/scripts/validate-dlq-alarms.sh --env uat --skip-cleanup
```

**What to check:**
- `[PASS] Test message arrived in ips-post-dlq-uat after Xs`
- `[PASS] CloudWatch alarm 'ips-autopost-post-dlq-uat' transitioned to ALARM state after Xs`
- `[PASS] DLQ ips-post-dlq-uat purged successfully`
- Alarm state visible in CloudWatch console: `ips-autopost-post-dlq-uat` = ALARM

**Result:** [ ] Pass  [ ] Fail

**Notes:**
```
_______________________________________________________________
_______________________________________________________________
Time for message to reach DLQ: ___s
Time for alarm to trigger: ___s
```

---

## Step 9 — Validate Manual Post API

**What to run:**
```bash
# Set environment variables
export UAT_API_URL=https://api.ips-autopost-uat.example.com
export UAT_API_KEY=<your-api-key>
export UAT_JOB_ID=<job-id>
export UAT_ITEM_IDS=<comma-separated-item-ids>
export UAT_EXPECTED_SUCCESS_QUEUE=<success-queue-id>
export UAT_EXPECTED_FAIL_QUEUE=<fail-queue-id>

# Run the integration tests (remove Skip attribute first)
# Edit: tests/IPS.AutoPost.Core.Tests/Integration/ManualPostApiValidationTests.cs
# Remove: Skip = "UAT only — remove Skip to run manually against UAT"

cd Generic-Solution
dotnet test tests/IPS.AutoPost.Core.Tests \
  --filter "FullyQualifiedName~ManualPostApiValidationTests" \
  --logger "console;verbosity=detailed"
```

**What to check:**
- `ManualPost_ResponseShape_MatchesPostBatchResultContract` — PASS
- `ManualPost_SuccessItems_DestinationQueueMatchesSuccessQueue` — PASS
- `ManualPost_FailedItems_DestinationQueueMatchesFailQueue` — PASS
- `ManualPost_NonExistentJobId_Returns404WithErrorMessage` — PASS
- `ManualPost_MissingApiKey_Returns401` — PASS
- `ManualPost_ItemResultsCount_MatchesRecordsProcessed` — PASS
- Response shape: `recordsProcessed`, `recordsSuccess`, `recordsFailed`, `itemResults` all present
- Each `itemResult` has: `itemId`, `isSuccess`, `responseCode`, `responseMessage`, `destinationQueue`

**Result:** [ ] Pass  [ ] Fail

**Notes:**
```
_______________________________________________________________
_______________________________________________________________
Tests passed: ___/6
```

---

## Step 10 — Validate InvitedClub Feed Download

**What to run:**
```sql
-- Run AFTER the first InvitedClub feed download by the new platform
-- infra/sql/uat/validate_invitedclub_feed.sql
sqlcmd -S ips-rds-database-1.cmrmduasa2gk.us-east-1.rds.amazonaws.com \
       -d Workflow -U IPSAppsUser -P <password> \
       -i infra/sql/uat/validate_invitedclub_feed.sql
```

**What to check:**
- Check 1: All 4 feed tables have rows (no `WARNING: 0 rows`)
  - `InvitedClubSupplier`: > 0 rows
  - `InvitedClubSupplierAddress`: > 0 rows
  - `InvitedClubSupplierSite`: > 0 rows
  - `InvitedClubCOA`: > 0 rows
- Check 2: `last_supplier_download_time` shows `UPDATED (within last 24h)`
- Check 3: At least 1 successful FEED execution in `generic_execution_history`
- Check 4: `output_file_path` is populated (CSV export path recorded)

**Result:** [ ] Pass  [ ] Fail

**Notes:**
```
_______________________________________________________________
_______________________________________________________________
InvitedClubSupplier rows: ___
InvitedClubSupplierAddress rows: ___
InvitedClubSupplierSite rows: ___
InvitedClubCOA rows: ___
last_supplier_download_time: ___
```

---

## Sign-off

| Step | Description | Result | Validated By | Date |
|------|-------------|--------|--------------|------|
| 1 | Deploy CloudFormation stacks | [ ] Pass [ ] Fail | | |
| 2 | Run database migrations | [ ] Pass [ ] Fail | | |
| 3 | Enable InvitedClub parallel run | [ ] Pass [ ] Fail | | |
| 4 | Verify InvitedClub routing outcomes | [ ] Pass [ ] Fail | | |
| 5 | Verify InvitedClub history records | [ ] Pass [ ] Fail | | |
| 6 | Enable Sevita parallel run | [ ] Pass [ ] Fail | | |
| 7 | Validate CloudWatch metrics | [ ] Pass [ ] Fail | | |
| 8 | Validate DLQ alarms | [ ] Pass [ ] Fail | | |
| 9 | Validate manual post API | [ ] Pass [ ] Fail | | |
| 10 | Validate InvitedClub feed download | [ ] Pass [ ] Fail | | |

**Overall UAT Result:** [ ] PASS — Ready for production  [ ] FAIL — Issues to resolve

**Sign-off:**  
Name: _______________  
Date: _______________  
Comments: _______________________________________________________________
