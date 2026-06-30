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
- ~~Slack Notifications~~ — Email via SNS is sufficient
- ~~AWS CloudTrail~~ — Default account-level trail is enough
- ~~AWS Config~~ — Not needed for a 2-person team
- ~~Backup & DR / RDS Multi-AZ / S3 Versioning~~ — RDS is pre-existing (managed separately); S3 versioning is on deployment bucket only

---

## Feature 1: AWS Step Functions — Hybrid Design (Orchestration + ECS Processing)

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

### What the Diagram Proposes — Hybrid Architecture (Recommended)

**Key principle:** Use Step Functions for **orchestration and visibility only**. Keep the heavy processing in **ECS Fargate** where there are no timeout or payload size limits.

```
TARGET FLOW:

MANUAL:    Workflow UI → .NET API → invoice-post-queue → Start Workflow Lambda → Step Functions
SCHEDULED: EventBridge → invoice-post-queue (DIRECT TO SQS) → Start Workflow Lambda → Step Functions

Both paths converge at the same SQS queue.
The Start Workflow Lambda handles both trigger types identically.
```

### Trigger Paths (Explicit)

```
┌─────────────────────────────────────────────────────────────────────┐
│                                                                      │
│  SCHEDULED PATH: "DIRECT TO SQS (Scheduled)"                        │
│  EventBridge ──→ invoice-post-queue ──→ Start Workflow Lambda        │
│                                                                      │
│  MANUAL PATH: "VIA API (Manual)"                                     │
│  Workflow UI ──→ .NET API ──→ invoice-post-queue ──→ Start Lambda    │
│                                                                      │
│  Both paths converge at the same SQS queue.                          │
│  The Start Workflow Lambda handles both trigger types identically.    │
└─────────────────────────────────────────────────────────────────────┘
```

### Detailed Hybrid Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│ SQS: invoice-post-queue                                                  │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ START WORKFLOW LAMBDA (lightweight — 10 seconds max)                      │
│   • Validate SQS message                                                 │
│   • Load basic config (job_id, client_type, is_active)                   │
│   • Start Step Functions execution                                       │
│   • Pass only metadata: { jobId, clientType, triggerType }               │
│   • Does NOT load workitems or reference data                            │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │
                                ▼
╔═════════════════════════════════════════════════════════════════════════════╗
║ AWS STEP FUNCTIONS (orchestration only — no heavy work)                    ║
║                                                                            ║
║  State 1: PREPARE EXECUTION (Lambda — 30 sec max)                          ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ • Count pending workitems: SELECT COUNT(*) WHERE PostInProcess=0   │    ║
║  │ • Decide chunk size (e.g., 1000 per chunk)                        │    ║
║  │ • Return: { totalItems: 100000, chunkSize: 1000, chunks: 100 }   │    ║
║  │ • Does NOT fetch actual workitem data (avoids 256KB payload limit)│    ║
║  └───────────────────────────────────┬───────────────────────────────┘    ║
║                                      │                                     ║
║  State 2: PROCESS CHUNKS (Map State — parallel ECS tasks)                  ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ MaxConcurrency: 50 (configurable per client)                       │    ║
║  │                                                                    │    ║
║  │ For each chunk (0 to 99):                                         │    ║
║  │   ┌─────────────────────────────────────────────────────────────┐ │    ║
║  │   │ ECS FARGATE TASK (ecs:runTask.sync — NO timeout!)           │ │    ║
║  │   │                                                              │ │    ║
║  │   │   Receives: { jobId, clientType, chunkIndex, chunkSize }    │ │    ║
║  │   │   Loads ALL data itself from DB:                            │ │    ║
║  │   │     • Client config                                         │ │    ║
║  │   │     • Reference data (ValidIds, etc.) — any size            │ │    ║
║  │   │     • Workitems: OFFSET + FETCH NEXT (pagination)           │ │    ║
║  │   │   Processes 1000 items via plugin                           │ │    ║
║  │   │   Writes results to DB (execution history per item)         │ │    ║
║  │   │   Returns: { success: 950, failed: 50 }                    │ │    ║
║  │   │                                                              │ │    ║
║  │   │   NO timeout limit (can run for hours)                      │ │    ║
║  │   │   NO payload size limit (loads its own data)                │ │    ║
║  │   │   NO cold start (ECS Fargate = container)                   │ │    ║
║  │   └─────────────────────────────────────────────────────────────┘ │    ║
║  │                                                                    │    ║
║  │ 50 ECS tasks running simultaneously = 50,000 items in parallel    │    ║
║  └───────────────────────────────────┬───────────────────────────────┘    ║
║                                      │                                     ║
║  State 3: AGGREGATE RESULTS (Lambda — 10 sec)                              ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ • Read results from DB (sum of all chunk results)                  │    ║
║  │ • Write final execution history                                   │    ║
║  │ • Publish CloudWatch metrics                                      │    ║
║  │ • Return: { total: 100000, success: 95000, failed: 5000 }        │    ║
║  └───────────────────────────────────┬───────────────────────────────┘    ║
║                                      │                                     ║
║  State 4: NOTIFY (Lambda — 5 sec)                                          ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ • Publish to SNS: "Client X: 100,000 processed, 95,000 success"  │    ║
║  └───────────────────────────────────────────────────────────────────┘    ║
║                                                                            ║
║  CATCH: ERROR HANDLER (Lambda — 5 sec)                                     ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ • Log failure, mark execution as FAILED, send failure SNS         │    ║
║  └───────────────────────────────────────────────────────────────────┘    ║
║                                                                            ║
╚═════════════════════════════════════════════════════════════════════════════╝
```

### How This Avoids ALL Issues

| Issue | How It's Avoided |
|---|---|
| **256 KB payload limit** | Only metadata passed between steps (~100 bytes: jobId, chunkIndex, chunkSize). ECS tasks load their own data from DB. |
| **Lambda 15-min timeout** | Heavy processing runs in ECS Fargate (NO timeout). Lambda only does lightweight coordination (5-30 seconds). |
| **Cold start latency** | Lightweight Lambdas stay warm (40 clients = frequent calls). ECS has no cold start concept. |
| **Large reference data (ValidIds)** | Each ECS task loads its own reference data from DB. Nothing passed between steps. No size limit. |
| **Lakh-level volumes** | Map State with MaxConcurrency=50 → 50 parallel ECS tasks → hours instead of days. |

### What Runs Where (Clear Separation)

| Component | Runs In | Why | Time |
|---|---|---|---|
| Validate SQS message | Lambda (Start Workflow) | Lightweight, stateless | 5 sec |
| Count pending workitems | Lambda (PrepareExecution) | Single DB query, returns a number | 10 sec |
| **Process workitems (API calls)** | **ECS Fargate** | No timeout, no payload limit, loads own data | 10-60 min |
| **Load ValidIds / reference data** | **ECS Fargate** (inside the task) | Can be any size | Part of task |
| Sum up results | Lambda (AggregateResults) | Reads from DB, lightweight math | 5 sec |
| Send notification | Lambda (SendNotification) | SNS publish | 2 sec |
| Error handling | Lambda (HandleError) | Log + notify | 2 sec |

**Rule:** If it's coordination/metadata → Lambda. If it's heavy processing/data → ECS Fargate.

### Performance at Scale

| Client Volume | Chunks | Concurrent ECS Tasks | Total Processing Time |
|---|---|---|---|
| 50 items | 1 | 1 | 3 minutes |
| 1,000 items | 1 | 1 | 30 minutes |
| 10,000 items | 10 | 10 | 30 minutes (parallel) |
| 50,000 items | 50 | 50 | 30 minutes (parallel) |
| 1 lakh (100K) | 100 | 50 (2 rounds) | ~60 minutes |
| 5 lakh (500K) | 500 | 50 (10 rounds) | ~5 hours |
| 10 lakh (1M) | 1000 | 50 (20 rounds) | ~10 hours |

**vs Current sequential approach:**
- 1 lakh items × 3 sec = 83 hours (3.5 days!)
- 5 lakh items × 3 sec = 416 hours (17 days!)

### Visual Debugging Experience (AWS Console)

```
Execution: MegaCorp-5001-20260630-080000
Status: RUNNING (2 hours elapsed)

├─ ✅ StartWorkflowLambda (2s)
├─ ✅ PrepareExecution (5s) — { totalItems: 100000, chunks: 100 }
├─ ⏳ ProcessChunks (Map State — 100 iterations, MaxConcurrency: 50)
│     ├─ ✅ Chunk 0 (18 min) — { success: 950, failed: 50 }
│     ├─ ✅ Chunk 1 (15 min) — { success: 980, failed: 20 }
│     ├─ ❌ Chunk 2 (FAILED — Oracle returned 503)
│     │     └─ Retry 1/2 in 30 seconds...
│     ├─ ✅ Chunk 3 (22 min) — { success: 940, failed: 60 }
│     ├─ ⏳ Chunk 48 (running — 12 min elapsed)
│     ├─ ⏳ Chunk 49 (running — 8 min elapsed)
│     └─ ⏸ Chunks 50-99 (queued, waiting for slot)
├─ ⏸ AggregateResults (waiting)
└─ ⏸ SendNotification (waiting)

Progress: 48/100 chunks complete (48%)
```

Click on any chunk → see its ECS task logs, input, output, duration. **No log searching required.**

### Cost at Scale (Example: 1 Lakh Invoices)

```
Step Functions:
  State transitions: 1 (Prepare) + 100 (Map iterations) + 1 (Aggregate) + 1 (Notify) = 103
  Cost: 103 × $0.000025 = $0.003

Lambda (lightweight steps only):
  4 invocations × 10 sec average × 256MB = ~$0.0002

ECS Fargate (heavy processing):
  50 concurrent tasks × 20 min each × (1 vCPU + 2GB RAM)
  = 50 × 0.33 hours × $0.04/hour = $0.66

Total per execution: ~$0.67
Monthly (4 runs/day × 30 days): ~$80 for a 1-lakh client
```

### Benefits

| Benefit | Description |
|---|---|
| **Parallel processing** | 50 ECS tasks run simultaneously. 1 lakh items in ~60 min instead of 83 hours. |
| **Visual debugging** | See exactly which chunk failed, click to inspect. No log searching across 40 clients. |
| **Per-chunk retry** | If Oracle Fusion returns 503, Step Functions retries that specific chunk (not the whole batch). |
| **No limits** | ECS tasks have no timeout, no payload limit, load their own data. |
| **Client isolation** | Each client runs its own Step Functions execution. One client's failure doesn't block another. |
| **Notification built-in** | State 4 (Notify) sends SNS after every batch — no separate implementation needed. |
| **Resume from failure** | If chunk 45 fails, restart from chunk 45 — don't reprocess chunks 0-44. |

### Cons (Honest Assessment at Scale)

| Con | Real Impact at 40+ Clients |
|---|---|
| **Team learning curve** | Need to learn Step Functions ASL, ECS RunTask integration, IAM for cross-service. ~1 week learning for the team. |
| **Local development** | Can't run Step Functions locally with `F5`. Need AWS SAM CLI or deploy to a dev account for testing. |
| **Deployment pipeline** | CI/CD deploys state machine + Lambdas + ECS task definition instead of just 1 Docker image. More stages but manageable. |
| **Vendor lock-in** | Step Functions ASL is AWS-specific. If we ever move off AWS (unlikely), the orchestration needs rewriting. ECS plugin code is portable. |

### Effort

| Work Item | Description |
|---|---|
| Design state machine (ASL JSON) | Define the workflow states, transitions, retry policies, and Map State configuration |
| Create Start Workflow Lambda | SQS consumer that validates messages and starts Step Functions executions |
| Create PrepareExecution Lambda | Counts pending workitems and calculates chunk configuration |
| Create ECS Batch Processor task definition | Refactor plugin execution logic from AutoPostOrchestrator into a standalone ECS-runnable container |
| Create AggregateResults Lambda | Reads chunk results from DB, writes final execution history, publishes metrics |
| Create Notify + ErrorHandler Lambdas | SNS publish for success/failure notifications |
| CloudFormation | State machine, IAM roles, task definitions, permissions |
| Testing | Unit + integration + parallel scenarios |

### Recommendation

**HIGH PRIORITY — implement before scaling to 40+ clients.** At lakh-level volumes, sequential processing is non-viable (days instead of hours). The hybrid design gives parallel processing with zero timeout/size constraints. Visual debugging across 40 clients is essential for operations. Start with InvitedClub + Sevita to validate the architecture, then all new clients automatically benefit.

---

## Feature 2: Parallel Batch Processing

**Diagram location:** "Batch Processors (Parallel)" in Step Functions and "Parallel Workers" in ECS Fargate

> **NOTE:** This feature is now INCLUDED in the Step Functions Hybrid Design (Feature 1). The Map State with `MaxConcurrency=50` provides parallel processing by launching multiple ECS Fargate tasks simultaneously. No separate implementation needed.

### What We Have Currently

Within a single batch, workitems are processed **sequentially**:

```csharp
foreach (var workitem in workitems)
{
    SetPostInProcess(workitem, 1);
    try
    {
        var invoiceId = await PostInvoiceAsync(workitem);
        var attachmentId = await PostAttachmentAsync(invoiceId);
        if (useTax) await PostCalculateTaxAsync(invoiceId);
        await RouteToSuccessQueue(workitem);
    }
    finally { ClearPostInProcess(workitem); }
}
```

### How Hybrid Design Solves This

```
Step Functions Map State (MaxConcurrency=50):
  ├─ ECS Task 0: processes items 1-1000 (parallel within task: 5 concurrent)
  ├─ ECS Task 1: processes items 1001-2000
  ├─ ECS Task 2: processes items 2001-3000
  ├─ ...
  └─ ECS Task 49: processes items 49001-50000

  50 chunks × 1000 items × 5 concurrent per chunk = 250,000 items/hour
  vs current: ~1,200 items/hour (sequential)
```

**Result:** ~200x faster at scale. No separate "Parallel Batch Processing" feature needed — it's built into the Step Functions architecture.

---

## Feature 4: Async Manual Post with Progress Tracking

**Diagram location:** Top center — "MANUAL POST (ASYNC FLOW)": "Request sent to ASP.NET Core API", "Message placed on Client Post Queue (SQS)", "Job processed asynchronously", "Status pushed to UI"

### What We Have Currently

Manual posts are **synchronous** — user waits until all items are processed:

```
User clicks → HTTP POST /api/post/42/items/101,102,103
              (waits 5-30 seconds)
HTTP 200: { "recordsProcessed": 3, "recordsSuccess": 2, "recordsFailed": 1, "itemResults": [...] }
```

### Decision: Always Async (No Sync Path)

ALL manual posts — whether 1 item or 50,000 items — will follow the same async flow:

```
1. User clicks "Post Now" (1 item or 50,000 items — same behavior)
2. API accepts request → returns HTTP 202 + executionId IMMEDIATELY
3. Message goes to SQS → Start Workflow Lambda → Step Functions → ECS processes
4. User polls GET /api/status/{executionId} for progress and final result
```

### Why Always Async (Not Hybrid)

| Reason | Explanation |
|---|---|
| **One code path** | No if/else between sync and async. Simpler to build, test, and maintain. |
| **Consistent UX** | User always sees the same behavior regardless of batch size. No confusion about "why did it respond instantly for 5 items but give me a tracker for 50?" |
| **Same pipeline for manual + scheduled** | Both go through SQS → Step Functions → ECS. Same retry, same monitoring, same debugging. |
| **No browser timeout risk — ever** | Even for 1 item, the 202 response is instant. Zero risk of timeout. |
| **Crash recovery** | If the server crashes mid-processing, SQS retries. With sync, the request is lost forever. |
| **Future-proof** | When volumes grow from 50 items to 50,000 items, nothing changes in the architecture. |

### What the User Sees

```
[Click "Post Now" — 3 items selected]
     ↓
[Instant: "Request accepted! Tracking: EXEC-7890"]
     ↓
[After 5 seconds, status shows: "Complete: 3 success, 0 failed"]
```

For small batches (1-10 items), the async processing completes in seconds. The user's first status check already shows "completed." It feels almost instant — just with an extra click to see the result.

```
[Click "Post Now" — 10,000 items selected]
     ↓
[Instant: "Request accepted! Tracking: EXEC-4567"]
     ↓
[Progress bar: "Processing... 2500 of 10000 (25%)"]
     ↓
[Later: "Complete: 9500 success, 500 failed. View details →"]
```

### API Changes

```
CURRENT:
  POST /api/post/{jobId}/items/{itemIds}
  → HTTP 200 (waits until done, returns full result)

NEW:
  POST /api/post/{jobId}/items/{itemIds}
  → HTTP 202 Accepted (instant)
  → Body: { "executionId": "exec-abc-123", "status": "queued" }

  GET /api/status/{executionId}
  → HTTP 200
  → Body: { "status": "processing", "progress": "2500/5000", "percentComplete": 50 }
  
  GET /api/status/{executionId}  (when done)
  → HTTP 200
  → Body: { "status": "completed", "recordsSuccess": 4800, "recordsFailed": 200, "itemResults": [...] }
```

### Benefits

| Benefit | Description |
|---|---|
| No browser timeouts — ever | Even 50,000 items works. Response is always instant. |
| Crash recovery | SQS retries if worker crashes. Nothing is lost. |
| One pipeline for everything | Manual and scheduled use identical SQS → Step Functions → ECS path. |
| Progress visibility | User sees real-time progress instead of a spinning wheel. |
| Simpler code | One async path instead of maintaining both sync and async. |
| Scales infinitely | 1 item or 1 lakh items — same architecture, same behavior. |

### Cons

| Con | Description |
|---|---|
| Extra click for small batches | User posts 3 items → gets tracking ID → must check status for result. One extra interaction vs instant response. |
| UI change required | Workflow UI must replace the "wait for response" pattern with a "submit + poll progress" pattern. Needs a progress panel / notification system. |
| Queue delay possible | If the SQS queue has 100 scheduled messages waiting, a manual 3-item post waits behind them. Solution: priority queue or separate manual-post queue. |

### Recommendation

**Implement as part of Step Functions Hybrid (Feature 1).** The async flow is inherent in the Step Functions architecture — the API puts a message on SQS, Step Functions processes it, and the user polls for status. No separate implementation needed beyond the API returning 202 instead of 200.

---

## Feature 5: Notifications — SNS Email After Every Execution

**Diagram location:**
- Top section — "NOTIFICATIONS: Amazon SNS → Email Notifications"
- Bottom of Invoice Posting Flow — "Amazon SNS Notifications" (post completion email)
- Bottom of Feed Download Flow — "Amazon SNS Notifications: Feed Download Status, Success / Failure Alerts"

**Channel: Email only.** No Slack, Teams, or other channels.

### What We Have Currently

- **Scheduled posts:** Results written to `generic_execution_history`. Nobody is notified. Silent.
- **Manual posts:** User polls status (async) — they triggered it, so they know.
- **Failure alerts:** CloudWatch Alarm → SNS → Email ONLY when threshold exceeded (PostFailedCount > 5).
- **Feed download completion:** Completely silent. Nobody knows unless they check logs.

SNS is used in ONE place only — CloudWatch Alarms for infrastructure failures.

### What We're Adding

**After EVERY scheduled execution (post or feed), send an email summarizing what happened:**

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
  Item 4590 — RequesterId is empty
```

```
SUBJECT: [IPS AutoPost] InvitedClub Feed — Completed (1,200 suppliers)

Job: InvitedClub Supplier Feed (Job ID: 1001)
Type: Scheduled Feed Download
Time: 07:00:00 — 07:05:30 UTC (5 min 30 sec)

Results:
  Suppliers downloaded: 1,200
  Supplier Addresses: 3,450
  COA records: 850
```

### How It Works

```
1. POST NOTIFICATIONS:
   Step Functions State 4 (Notify) → publishes to SNS Topic → Email sent

2. FEED NOTIFICATIONS:
   FeedWorker completes → publishes to SNS Topic → Email sent

Both topics have one subscriber: ops-team@edenredpay.com
```

### Difference: Alerts vs Notifications

| | Alerts (already have) | Notifications (adding) |
|---|---|---|
| **Purpose** | Something is WRONG | Here's what HAPPENED |
| **Trigger** | Threshold exceeded (failure count > 5) | Every execution (success or failure) |
| **Example** | "DLQ has messages — investigate!" | "InvitedClub batch complete: 95 success, 5 failed" |
| **Urgency** | Immediate action needed | Informational — check when convenient |
| **Volume** | Rare (only on failures) | Frequent (every batch, every feed) |

### What We'll Build

| Component | Description |
|---|---|
| SNS Topic: `ips-post-notifications-{env}` | Receives messages after every post batch |
| SNS Topic: `ips-feed-notifications-{env}` | Receives messages after every feed download |
| Email subscription | ops-team email subscribes to both topics |
| Publish after post batch | In Step Functions Notify state |
| Publish after feed download | In FeedWorker after feed completion |
| Email template | Job name, client type, counts, duration, failed items |

### Benefits

| Benefit | Description |
|---|---|
| **Feed failure visibility** | Currently completely silent. This is the biggest gap. |
| **Daily ops visibility** | Team sees what ran overnight without checking logs. |
| **Faster incident response** | Email at 3:15 AM says "InvitedClub failed" — no need to discover it at 9 AM. |

### Cons

| Con | Description |
|---|---|
| **Email volume at scale** | 40 clients × 4 runs/day = 160 emails/day. May want "failures only" mode or daily digest. |
| **Not urgent** | For critical issues, CloudWatch Alarms (already exist) are faster and more reliable. |

### Recommendation

**Implement as part of Step Functions Hybrid (Feature 1).** The Notify state naturally publishes to SNS after every execution. For feeds, add SNS publish in FeedWorker. Email only.

---

## Out of Scope (Future — When New Clients Are Onboarded)

- **Additional ERP Clients** (Media, Greenthal, Akron, MDS, RapidPay, etc.) — will be added one at a time via plugin architecture when those clients are ready. Not needed for InvitedClub + Sevita.
- **External Feed Sources (SOAP, FTP/SFTP)** — only REST feeds are needed for InvitedClub. Sevita has no feed download. SOAP/FTP/SFTP services will be built when a client that requires them is onboarded.

---

## Priority Summary

| # | Feature | Benefit at 40+ Clients / Lakh Volumes | Priority |
|---|---|---|---|
| 1 | **Step Functions Hybrid (ECS processing)** | Critical — parallel processing, visual debugging, client isolation | **HIGH — implement before scaling** |
| 2 | **SNS Email Notifications (Post + Feed completion)** | Feed failure visibility, ops awareness across 40 clients | **HIGH — essential at scale** |
| 3 | **Parallel Batch Processing** | Included in Step Functions hybrid (Map State parallelism) | **Covered by Feature 1** |
| 4 | **Async Manual Post** | Included in Step Functions hybrid (API returns 202, user polls status) | **Covered by Feature 1** |

---

## Suggested Implementation Order

### Phase 1 — Foundation for Scale
1. **Step Functions Hybrid Architecture** — the backbone for 40+ clients and lakh-level volumes (includes parallel processing + async manual post)
2. **SNS Email Notifications** — visibility across all clients (email only)
