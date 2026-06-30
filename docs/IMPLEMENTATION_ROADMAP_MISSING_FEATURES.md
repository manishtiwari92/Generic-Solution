# Implementation Roadmap — Missing Features from Architecture Diagram

## Scope

This document covers features visible in the architecture diagram that are **not yet implemented** in our Generic-Solution.

**Production scale context:**
- 40+ clients (InvitedClub, Sevita, Media, Greenthal, Akron, MDS, and many more)
- Invoice volumes per client: 50K, 1 lakh (100K), 5 lakh (500K), 10 lakh (1M)
- Each client has different business logic (plugin-based)
- Platform is generic with plugin architecture — enhancements benefit all clients

**Excluded from this document** (decided not to implement):
- ~~AWS X-Ray~~ — Not needed; CorrelationId in logs is sufficient
- ~~Slack / Teams Notifications~~ — Email via SNS is sufficient
- ~~AWS CloudTrail~~ — Default account-level trail is enough
- ~~AWS Config~~ — Not needed for current team size
- ~~Backup & DR / RDS Multi-AZ / S3 Versioning~~ — RDS is pre-existing (managed separately)
- ~~VPC Endpoints~~ — Keep existing NAT Gateway approach

---

## Feature 1: AWS Step Functions + .NET Lambda (Invoice Posting Orchestration)

**Diagram location:** Left box — "INVOICE POSTING FLOW (CONFIG DRIVEN)" with Start Workflow Lambda + Step Functions

### What We Have Currently

Our posting flow runs inside a single C# class (`AutoPostOrchestrator`) on an ECS Fargate worker:

```
CURRENT FLOW:

MANUAL:    Workflow UI → .NET API → PostWorker (ECS) → MediatR → AutoPostOrchestrator → Plugin
SCHEDULED: EventBridge → ips-post-queue → PostWorker (ECS) → MediatR → AutoPostOrchestrator → Plugin

AutoPostOrchestrator (single C# class — sequential processing):
  ├─ Load config from DB
  ├─ Check schedule window
  ├─ OnBeforePostAsync (plugin hook)
  ├─ Fetch workitems
  ├─ plugin.ExecutePostAsync() ← sequential loop (one item at a time)
  ├─ Write execution history
  └─ Publish CloudWatch metrics
```

**Problem at scale:** If a client has 1 lakh (100,000) invoices at 3 seconds each = 300,000 seconds = **3.5 days** to process one batch sequentially. Completely unacceptable.

### Target Architecture — Step Functions + .NET Lambda (1 Item Per Invocation)

**Key design decisions:**
- **Step Functions** for orchestration, visibility, and retry logic
- **.NET Lambda** for processing (1 item per Lambda invocation — always completes in 5-15 seconds, well under 15-minute limit)
- **No ECS for posting** — fully serverless. ECS remains only for long-running feed downloads.
- **All manual posts are async** — API returns 202 immediately, user polls for status

```
TARGET FLOW:

MANUAL:    Workflow UI → .NET API → {client}-post-queue → Start Workflow Lambda → Step Functions
SCHEDULED: EventBridge → {client}-post-queue (DIRECT TO SQS) → Start Workflow Lambda → Step Functions

Each client has its own dedicated SQS queue (dynamically provisioned).
The Start Workflow Lambda subscribes to ALL client queues via event source mappings.
All manual posts are ASYNC — user gets HTTP 202 + executionId immediately.
```

### Trigger Paths

```
┌─────────────────────────────────────────────────────────────────────┐
│                                                                      │
│  SCHEDULED PATH: "DIRECT TO SQS (Scheduled)"                        │
│  EventBridge ──→ {client}-post-queue ──→ Start Workflow Lambda       │
│                                                                      │
│  MANUAL PATH: "VIA API (Manual)"                                     │
│  Workflow UI ──→ .NET API ──→ {client}-post-queue ──→ Start Lambda   │
│                                                                      │
│  Each client has its OWN SQS queue (per-client isolation):           │
│    invitedclub-post-queue                                            │
│    sevita-post-queue                                                 │
│    greenthal-post-queue                                              │
│    ...dynamically created per active job                             │
│                                                                      │
│  The Start Workflow Lambda subscribes to ALL client queues.           │
└─────────────────────────────────────────────────────────────────────┘
```

### Per-Client SQS Queues (Dynamic Provisioning)

Instead of one shared `invoice-post-queue`, each client gets its own dedicated queue:

```
Queue naming: ips-post-{client_type_lowercase}-{env}
DLQ naming:   ips-post-{client_type_lowercase}-dlq-{env}

Examples:
  ips-post-invitedclub-prod     + ips-post-invitedclub-dlq-prod
  ips-post-sevita-prod          + ips-post-sevita-dlq-prod
  ips-post-greenthal-prod       + ips-post-greenthal-dlq-prod
  (created dynamically — not hardcoded in CloudFormation)
```

**How queues are created and deleted dynamically:**

The **Scheduler Lambda** (which already runs every 10 minutes and manages EventBridge rules) also manages queue lifecycle:

```
Scheduler Lambda runs (every 10 min):
  Reads all rows from generic_job_configuration

  FOR EACH active job (is_active = 1):
    → Derive queue name: ips-post-{clientType}-{env}
    → Call SQS CreateQueue (idempotent — creates if not exists, returns URL if exists)
    → Call SQS CreateQueue for DLQ: ips-post-{clientType}-dlq-{env}
    → Set RedrivePolicy on main queue (link to DLQ)
    → Ensure CloudWatch alarm exists on DLQ
    → Ensure Lambda event source mapping exists for this queue
    → Create/update EventBridge rule targeting THIS client's queue

  FOR EACH inactive job (is_active = 0):
    → Disable EventBridge rule (no new messages)
    → Remove Lambda event source mapping (stop polling this queue)
    → Queue remains (messages drain naturally, expire after 14 days)
    → After 30 days idle: optionally delete queue via cleanup job
```

**Benefits of per-client queues:**

| Benefit | Description |
|---|---|
| **Complete client isolation** | InvitedClub's 10K messages can't starve Sevita's 50 messages |
| **Per-client DLQ** | Instantly know WHICH client has failures (separate DLQ per client) |
| **Independent retry** | One client's retries don't block another client's processing |
| **Per-client scaling** | Can set different MaxConcurrency per client based on their ERP API limits |
| **Better monitoring** | Dashboard shows queue depth PER client — not one combined number |
| **Zero-infra onboarding** | Insert DB row → Scheduler Lambda creates queue automatically within 10 min |
| **Zero-infra deactivation** | Set is_active=0 → EventBridge disabled → queue drains → eventually cleaned up |

**Queue URL/ARN — derived at runtime (no DB columns needed):**

Since the queue name follows a predictable pattern, we **derive** the URL and ARN dynamically — no need to store them in the database:

```
Pattern:
  Queue name: ips-post-{client_type_lowercase}-{env}
  Queue URL:  https://sqs.{region}.amazonaws.com/{account_id}/ips-post-{client_type_lowercase}-{env}
  Queue ARN:  arn:aws:sqs:{region}:{account_id}:ips-post-{client_type_lowercase}-{env}
  DLQ name:   ips-post-{client_type_lowercase}-dlq-{env}

Example (InvitedClub, production, us-east-1, account 123456789012):
  Queue name: ips-post-invitedclub-prod
  Queue URL:  https://sqs.us-east-1.amazonaws.com/123456789012/ips-post-invitedclub-prod
  Queue ARN:  arn:aws:sqs:us-east-1:123456789012:ips-post-invitedclub-prod
  DLQ:        ips-post-invitedclub-dlq-prod

Code:
  var queueName = $"ips-post-{clientType.ToLower()}-{env}";
  var queueUrl = $"https://sqs.{region}.amazonaws.com/{accountId}/{queueName}";
```

**No database columns added.** The Scheduler Lambda:
1. Reads `client_type` and `is_active` from `generic_job_configuration`
2. Derives the queue name from the naming pattern
3. Calls `CreateQueue` (idempotent — returns existing queue if it already exists)
4. Uses the derived ARN for EventBridge rule targeting

**No `queue_provisioned` flag needed.** SQS `CreateQueue` is idempotent — calling it on an existing queue returns the URL without error. The Scheduler Lambda can safely call `CreateQueue` every run without tracking state.

### Detailed Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Per-Client SQS Queues (dynamically created):                             │
│   ips-post-invitedclub-prod                                              │
│   ips-post-sevita-prod                                                   │
│   ips-post-greenthal-prod                                                │
│   ... (one per active client)                                            │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │ (Lambda event source mapping — all queues)
                                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ START WORKFLOW LAMBDA (.NET — lightweight)                                │
│   • Validate SQS message                                                 │
│   • Load basic config (job_id, client_type, is_active)                   │
│   • Start Step Functions execution                                       │
│   • Pass only metadata: { jobId, clientType, triggerType, executionId }  │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │
                                ▼
╔═════════════════════════════════════════════════════════════════════════════╗
║ AWS STEP FUNCTIONS                                                         ║
║                                                                            ║
║  State 1: PREPARE EXECUTION (.NET Lambda)                                  ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ • Load full job config from DB                                     │    ║
║  │ • OnBeforePostAsync logic (load ValidIds for Sevita)              │    ║
║  │ • Fetch all pending ItemIds: SELECT ItemId WHERE PostInProcess=0  │    ║
║  │ • Return: { itemIds: [101, 102, 103, ...], totalCount: 5000 }    │    ║
║  │                                                                    │    ║
║  │ NOTE: Only returns ItemId array (integers) — NOT full workitem    │    ║
║  │ data. 5000 integers = ~25KB. Well within 256KB payload limit.     │    ║
║  └───────────────────────────────────┬───────────────────────────────┘    ║
║                                      │                                     ║
║  State 2: PROCESS ITEMS (Map State — 1 item per Lambda)                    ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ MaxConcurrency: 50 (configurable per client)                       │    ║
║  │ Input: each itemId from the array                                 │    ║
║  │                                                                    │    ║
║  │   ┌─────────────────────────────────────────────────────────────┐ │    ║
║  │   │ BATCH PROCESSOR LAMBDA (.NET — 1 item per invocation)       │ │    ║
║  │   │                                                              │ │    ║
║  │   │   Receives: { jobId, clientType, itemId }                   │ │    ║
║  │   │                                                              │ │    ║
║  │   │   • Loads config from DB                                    │ │    ║
║  │   │   • Loads workitem header + detail data                     │ │    ║
║  │   │   • Sets PostInProcess = 1                                  │ │    ║
║  │   │   • Gets image from S3 (if applicable)                     │ │    ║
║  │   │   • Calls ERP API (Oracle Fusion / Sevita)                 │ │    ║
║  │   │   • Routes workitem to success/fail queue                  │ │    ║
║  │   │   • Writes history per item                                │ │    ║
║  │   │   • Clears PostInProcess = 0                               │ │    ║
║  │   │   • Returns: { itemId, success, queueId, errorMsg }        │ │    ║
║  │   │                                                              │ │    ║
║  │   │   Time: 5-15 seconds (well under 15 min limit)             │ │    ║
║  │   └─────────────────────────────────────────────────────────────┘ │    ║
║  │                                                                    │    ║
║  │ 50 Lambdas running simultaneously = 50 items processed in parallel│    ║
║  │                                                                    │    ║
║  │ Retry per item:                                                   │    ║
║  │   If Oracle returns 503 → wait 5s → retry same item (up to 2x)  │    ║
║  │   If still fails → mark failed, continue with next items         │    ║
║  └───────────────────────────────────┬───────────────────────────────┘    ║
║                                      │                                     ║
║  State 3: AGGREGATE RESULTS (.NET Lambda)                                  ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ • Collect all item results from Map State outputs                  │    ║
║  │ • Count success/failed                                            │    ║
║  │ • Write generic_execution_history                                 │    ║
║  │ • Update LastPostTime                                             │    ║
║  │ • Publish CloudWatch metrics                                      │    ║
║  │ • Send batch-level emails:                                        │    ║
║  │   → InvitedClub: image failure email to helpdesk (if any)        │    ║
║  │   → Sevita: notification email with failed records table          │    ║
║  │ • Check output file config:                                       │    ║
║  │   → If generate_output_file=true: call .NET API to generate file │    ║
║  │ • Return: { total: 5000, success: 4800, failed: 200 }            │    ║
║  └───────────────────────────────────┬───────────────────────────────┘    ║
║                                      │                                     ║
║  State 4: NOTIFY (.NET Lambda)                                             ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ • Publish ONE email via SNS (entire execution summary)            │    ║
║  │ • "Client X: 5,000 processed, 4,800 success, 200 failed"         │    ║
║  │ • ONE notification per execution — NOT per item                   │    ║
║  └───────────────────────────────────────────────────────────────────┘    ║
║                                                                            ║
║  CATCH: ERROR HANDLER (.NET Lambda)                                        ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ • Log failure, mark execution as FAILED in DB                     │    ║
║  │ • Send failure email via SNS                                      │    ║
║  └───────────────────────────────────────────────────────────────────┘    ║
║                                                                            ║
╚═════════════════════════════════════════════════════════════════════════════╝
```

### Why .NET Lambda (1 Item) Instead of ECS

| Aspect | .NET Lambda (1 item) | ECS (1000 items/chunk) |
|---|---|---|
| **Timeout risk** | None — 7 sec per item << 15 min limit | None (no limit) |
| **Infrastructure** | Zero — fully serverless | Docker, ECR, task definitions, scaling config |
| **Cost for small batches** | ~$0.001 for 50 items | Overkill — ECS spin-up cost dominates |
| **Cost for large batches** | ~$1.50 for 1 lakh items | ~$0.66 for 1 lakh items |
| **Scale to zero** | Yes — no cost when idle | No — MinCapacity=1 always running |
| **Deployment** | Zip upload or container to Lambda | Docker build + ECR push + ECS deploy |
| **Per-item isolation** | Complete — each item is a separate invocation | Items share a process |
| **Per-item retry** | Step Functions retries that specific item | Entire chunk retried |
| **Cold start** | 1.5-3 sec first call, then warm. With 40 clients → mostly warm. | None |

### Time Per Item (Will It Fit Under 15 Minutes?)

| Step | InvitedClub (worst case) | Sevita (worst case) |
|---|---|---|
| Load config from DB | 100ms | 100ms |
| Load workitem data | 100ms | 100ms |
| Get image from S3 | 500ms | 300ms |
| Set PostInProcess | 50ms | 50ms |
| POST Invoice to ERP | 3 seconds | 2 seconds |
| POST Attachment | 2 seconds | N/A |
| POST CalculateTax | 1 second | N/A |
| Route workitem | 50ms | 50ms |
| Write history | 50ms | 50ms |
| Clear PostInProcess | 50ms | 50ms |
| **TOTAL** | **~7 seconds** | **~3 seconds** |

15 minutes = 900 seconds. We use 7 seconds max. **Zero risk of timeout.**

### Performance at Scale

| Client Volume | Lambda Invocations | MaxConcurrency | Total Processing Time |
|---|---|---|---|
| 50 items | 50 | 50 | ~10 seconds (all parallel) |
| 1,000 items | 1,000 | 50 | ~2.5 minutes |
| 10,000 items | 10,000 | 50 | ~25 minutes |
| 50,000 items | 50,000 | 50 | ~2 hours |
| 1 lakh (100K) | 100,000 | 50 | ~4 hours |
| 5 lakh (500K) | 500,000 | 100 (increased) | ~10 hours |

**vs Current sequential approach:**
- 1 lakh items × 3 sec = 83 hours (3.5 days!)
- With Lambda parallel: 1 lakh items ÷ 50 concurrent × 7 sec = ~4 hours

### Handling ValidIds (Sevita) with 1 Item Per Lambda

Each Lambda invocation loads ValidIds independently from DB:
- 50 concurrent Lambdas × 1 DB query each = 50 redundant queries
- Each query returns ~2000 IDs = ~70KB, takes ~50ms
- Acceptable trade-off for simplicity (50 extra queries × 50ms = negligible)

Alternative: Prepare step caches ValidIds to S3, each Lambda reads from S3 (~50ms read). Either approach works.

### Output File Generation (Post-Processing After All Items)

New configuration on `generic_job_configuration`:
```
generate_output_file: true/false
output_file_format: "CSV" | "Excel" | "JSON"
output_file_path: "s3://ips-output-files/{client_type}/"
```

The **Aggregate Results Lambda** (State 3) handles this:
1. Collects all item results from Map State
2. If `generate_output_file = true`:
   - Calls .NET API endpoint: `POST /api/report/{jobId}/generate`
   - API queries processed items from DB and generates the file
   - Uploads to S3 at `output_file_path`
3. Returns file URL in the execution result

### Batch-Level Post-Processing (What Happens After ALL Items)

Currently in our system, after all items are processed:

| Client | Current Behavior | Where It Moves |
|---|---|---|
| **InvitedClub** | Sends "Image Failure Email" if any items had missing images (HTML table → helpdesk) | → **Aggregate Results Lambda** (State 3) |
| **Sevita** | Sends "Failure Notification Email" if any items have `IsSendNotification=true` | → **Aggregate Results Lambda** (State 3) |
| **Orchestrator** | Writes `generic_execution_history`, updates `LastPostTime`, publishes CloudWatch metrics | → **Aggregate Results Lambda** (State 3) |

All batch-level post-processing moves to the Aggregate Results Lambda. Per-item work (routing, history per item, PostInProcess) stays in the Batch Processor Lambda.

### Visual Debugging (AWS Console)

```
Execution: InvitedClub-1001-20260630-080000
Status: RUNNING

├─ ✅ StartWorkflowLambda (2s)
├─ ✅ PrepareExecution (3s) — { totalItems: 5000 }
├─ ⏳ ProcessItems (Map State — 5000 iterations, MaxConcurrency: 50)
│     ├─ ✅ Item 101 (6s) — { success: true, queueId: 200 }
│     ├─ ✅ Item 102 (5s) — { success: true, queueId: 200 }
│     ├─ ❌ Item 103 (8s) — { success: false, errorMsg: "Image not found" }
│     ├─ ✅ Item 104 (7s) — { success: true, queueId: 200 }
│     ├─ ⏳ Item 105 (running — 4s elapsed)
│     └─ ... (4946 remaining)
├─ ⏸ AggregateResults (waiting)
└─ ⏸ Notify (waiting)

Progress: 54/5000 items complete (1.1%)
```

### Cost at Scale

```
Example: InvitedClub — 5,000 invoices

Step Functions:
  State transitions: 1 (Prepare) + 5000 (Map items) + 1 (Aggregate) + 1 (Notify) = 5003
  Cost: 5003 × $0.000025 = $0.13

Lambda (all steps):
  Start Workflow: 1 × 5s × 256MB = $0.0001
  PrepareExecution: 1 × 3s × 256MB = $0.0001
  Batch Processors: 5000 × 7s × 256MB = $0.73
  AggregateResults: 1 × 10s × 512MB = $0.0001
  Notify: 1 × 2s × 128MB = negligible

Total per execution: ~$0.86
Monthly (4 runs/day × 30 days): ~$103 for a 5,000-item client
```

For comparison — current ECS approach (always-on PostWorker): ~$17/month fixed regardless of volume.

At lakh-level volumes, Lambda cost grows but so does the value (parallel processing). The serverless model means clients with 50 items/month pay almost nothing ($0.05/month) instead of sharing the $17 fixed ECS cost.

### Benefits

| Benefit | Description |
|---|---|
| **Fully serverless** | No Docker, no ECR, no ECS task definitions for posting. Zero infra when idle. |
| **Per-item parallelism** | 50 items simultaneously. 5000 items in ~12 minutes instead of 4 hours. |
| **Per-item retry** | If Oracle returns 503 for item 103, only item 103 retries. Others continue. |
| **Per-item visibility** | See exactly which item failed in the Step Functions console. Click to inspect. |
| **Scale to zero** | No posting cost when no invoices. Small clients pay almost nothing. |
| **Simple deployment** | One Lambda function (zip/container). No Docker build pipeline for posting. |
| **Complete item isolation** | One item's failure never affects another. No shared state between items. |

### Cons

| Con | Impact |
|---|---|
| **Cold start (first call)** | 1.5-3 seconds. With 40+ clients calling frequently → Lambdas stay warm. |
| **Redundant DB loads** | Each Lambda loads config independently. 50 concurrent = 50 config queries. Acceptable. |
| **Higher cost at very high volume** | 10 lakh items = ~$17/run in Lambda. But delivers results in hours vs days. |
| **Step Functions cost at lakh scale** | 100K state transitions = $2.50 per run. Low compared to total Lambda cost. |
| **Vendor lock-in** | Step Functions ASL is AWS-specific. Lambda code (.NET) is portable. |

### Architecture Summary

```
POSTING (serverless, per-client queues):
  EventBridge → {client}-post-queue → Start Lambda → Step Functions → Lambda per item → Aggregate → Notify
  
  Queues created dynamically by Scheduler Lambda based on generic_job_configuration.
  Each client isolated. Each client has own DLQ.

FEEDS (ECS Fargate — shared queue, unchanged):
  SQS → FeedWorker (ECS Fargate) — feeds can run 30-40 min, needs ECS

MANUAL POST (always async, per-client queue):
  API → {client}-post-queue → same Step Functions flow → user polls /api/status/{executionId}
```

### Recommendation

**HIGH PRIORITY — implement before scaling to 40+ clients.** The Lambda-per-item approach is simpler than ECS chunks (no Docker for posting, no task definitions, no ECR), gives per-item visibility and retry, scales to zero when idle, and handles lakh-level volumes through parallelism.

---

## Feature 2: Async Manual Post (Always Async, No Sync Path)

> **NOTE:** This feature is INCLUDED in Feature 1. The Step Functions architecture naturally makes all posts async — API returns 202, user polls for status. No separate implementation needed.

### Decision

ALL manual posts — whether 1 item or 50,000 items — follow the same async flow:

```
1. User clicks "Post Now"
2. API returns HTTP 202 + executionId IMMEDIATELY
3. Message goes to SQS → Step Functions processes in background
4. User polls GET /api/status/{executionId} for progress and final result
```

### API Contract

```
POST /api/post/{jobId}/items/{itemIds}
→ HTTP 202 Accepted
→ Body: { "executionId": "exec-abc-123", "status": "queued" }

GET /api/status/{executionId}
→ HTTP 200
→ Body: { "status": "processing", "progress": "2500/5000", "percentComplete": 50 }

GET /api/status/{executionId}  (when done)
→ HTTP 200
→ Body: { "status": "completed", "recordsSuccess": 4800, "recordsFailed": 200, "itemResults": [...] }
```

---

## Feature 3: SNS Email Notifications After Every Execution

**Diagram location:**
- Top section — "NOTIFICATIONS: Amazon SNS → Email Notifications"
- Bottom of Invoice Posting Flow — "Amazon SNS Notifications" (post completion email)
- Bottom of Feed Download Flow — "Amazon SNS Notifications: Feed Download Status, Success / Failure Alerts"

**Channel: Email only.** No Slack, Teams, or other channels.

### What We Have Currently

- **Scheduled posts:** Silent. Nobody notified.
- **Feed downloads:** Completely silent.
- **Failure alerts:** CloudWatch Alarm → SNS → Email only when threshold exceeded.

### What We're Adding

**ONE email per execution** (not per item, not per chunk) when the entire process completes:

```
SUBJECT: [IPS AutoPost] InvitedClub Post — Completed (95 success, 5 failed)

Job: InvitedClub AutoPost (Job ID: 1001)
Type: Scheduled Post
Time: 08:00:05 — 08:02:45 UTC (2 min 40 sec)

Results:
  Records Processed: 100
  Successful: 95
  Failed: 5

Failed Items:
  Item 4523 — Oracle Fusion returned 503
  Item 4567 — Image not found in S3
```

```
SUBJECT: [IPS AutoPost] InvitedClub Feed — Completed (1,200 suppliers)

Job: InvitedClub Supplier Feed (Job ID: 1001)
Type: Scheduled Feed Download
Results:
  Suppliers downloaded: 1,200
  COA records: 850
```

### How It Works

```
POST NOTIFICATIONS:
  Step Functions State 4 (Notify Lambda) → publishes to SNS Topic → Email sent
  Runs ONCE after ALL items are processed (after Aggregate Results)

FEED NOTIFICATIONS:
  FeedWorker completes → publishes to SNS Topic → Email sent
```

### What We'll Build

| Component | Description |
|---|---|
| SNS Topic: `ips-post-notifications-{env}` | Receives messages after every post execution completes |
| SNS Topic: `ips-feed-notifications-{env}` | Receives messages after every feed download completes |
| Email subscription | ops-team email subscribes to both topics |
| Notify Lambda (State 4) | Publishes post execution summary to SNS |
| FeedWorker SNS publish | Publishes feed download summary to SNS |

### Recommendation

**Implement as part of Feature 1.** The Notify Lambda (State 4) in Step Functions naturally publishes to SNS after every execution. For feeds, add SNS publish in FeedWorker after completion. Email only.

---

## Priority Summary

| # | Feature | Benefit | Priority |
|---|---|---|---|
| 1 | **Step Functions + .NET Lambda (1 item per invocation)** | Parallel processing, per-item visibility, serverless, scales to zero | **HIGH — implement before scaling** |
| 2 | **Async Manual Post** | Included in Feature 1 (API returns 202, user polls status) | **Covered by Feature 1** |
| 3 | **SNS Email Notifications** | Ops visibility — know when batches/feeds complete or fail | **HIGH — part of Feature 1** |

---

## Suggested Implementation Order

### Phase 1 — Foundation for Scale
1. **Step Functions + .NET Lambda architecture** — includes:
   - Per-client SQS queues (dynamically created/managed by Scheduler Lambda)
   - Start Workflow Lambda (SQS consumer, subscribes to all client queues)
   - Prepare Execution Lambda (fetch ItemIds, load config)
   - Batch Processor Lambda (1 item per invocation, plugin logic)
   - Aggregate Results Lambda (execution history, metrics, emails, output file)
   - Notify Lambda (SNS email)
   - Error Handler Lambda (failure SNS)
   - Step Functions state machine (ASL JSON)
   - Scheduler Lambda update (queue provisioning + EventBridge rule targeting per-client queue)
   - CloudFormation (state machine, IAM roles, Lambda functions)
2. **API change** — return 202 instead of 200, route to correct client queue based on job config
3. **Status API enhancement** — add progress tracking from Step Functions execution status

---

## Out of Scope (Future — When New Clients Are Onboarded)

- **Additional ERP Clients** (Media, Greenthal, Akron, MDS, RapidPay, etc.) — will be added one at a time via plugin architecture when those clients are ready. Not needed for InvitedClub + Sevita.
- **External Feed Sources (SOAP, FTP/SFTP)** — only REST feeds are needed for InvitedClub. Sevita has no feed download. SOAP/FTP/SFTP services will be built when a client that requires them is onboarded.
