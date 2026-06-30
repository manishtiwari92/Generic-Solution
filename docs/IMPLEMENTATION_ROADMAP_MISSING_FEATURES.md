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

### Effort: ~14 days

| Work Item | Effort |
|---|---|
| Design state machine (ASL JSON) | 2 days |
| Create Start Workflow Lambda | 1 day |
| Create PrepareExecution Lambda | 1 day |
| Create ECS Batch Processor task definition (refactor from orchestrator) | 3 days |
| Create AggregateResults Lambda | 1 day |
| Create Notify + ErrorHandler Lambdas | 1 day |
| CloudFormation (state machine, IAM roles, task definitions) | 2 days |
| Testing (unit + integration + parallel scenarios) | 3 days |
| **Total** | **~14 days** |

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

## Feature 3: VPC Endpoints

**Diagram location:** Bottom security bar — "VPC Endpoints — S3, Secrets Manager, SQS, CloudWatch Logs"

### What We Have Currently

All AWS service calls go through NAT Gateway:

```
ECS Task (private subnet) → NAT Gateway → Internet → AWS Service
```

Every byte costs $0.045/GB in NAT data processing charges.

### What VPC Endpoints Would Add

```
ECS Task (private subnet) → VPC Endpoint (private) → AWS Service
```

Traffic never leaves the AWS network.

### Benefits

| Benefit | Description |
|---|---|
| Cost reduction | Eliminates NAT data processing charges for AWS service traffic |
| Lower latency | ~1-5ms faster per call (stays within AWS data center) |
| Better security | Traffic never traverses public internet |

### Cons

| Con | Description |
|---|---|
| Interface Endpoint cost | ~$7.20/month per AZ per endpoint. With 2 AZs × 4 endpoints = ~$57.60/month fixed. |
| S3 Gateway Endpoint | FREE — no hourly charge. Only savings, no cost. |
| Break-even | If NAT data transfer < $57/month, VPC endpoints cost MORE. Need to measure first. |

### Cost Breakdown

| Service | Endpoint Type | Monthly Cost (2 AZs) |
|---|---|---|
| S3 | Gateway (free) | $0 |
| SQS | Interface | $14.40 |
| Secrets Manager | Interface | $14.40 |
| CloudWatch Logs | Interface | $14.40 |
| CloudWatch Metrics | Interface | $14.40 |
| **Total** | | **~$57.60/month** |

### Effort: ~2.5 days

### Recommendation

**Add S3 Gateway Endpoint immediately** (free, zero downside). Hold on Interface Endpoints until NAT costs exceed $60/month. At current volume (2 clients), NAT costs are likely $5-15/month.

---

## Feature 4: Async Manual Post with Progress Tracking

**Diagram location:** Top center — "MANUAL POST (ASYNC FLOW)" steps 3-5: "Message placed on Client Post Queue (SQS)", "Job processed asynchronously", "Status pushed to UI"

### What We Have Currently

Manual posts are **synchronous**:

```
User clicks → HTTP POST /api/post/42/items/101,102,103
              (waits 5-30 seconds)
HTTP 200: { "recordsProcessed": 3, "recordsSuccess": 2, "recordsFailed": 1, "itemResults": [...] }
```

Works well for 1-50 items. For 100+ items, browser/proxy timeouts become a risk.

### What Async Would Add

```
HTTP POST → HTTP 202 Accepted: { "executionId": "exec-abc-123", "status": "queued" }

GET /api/status/exec-abc-123 → { "status": "processing", "progress": "250/500" }

GET /api/status/exec-abc-123 → { "status": "completed", "recordsSuccess": 480, ... }
```

### Benefits

| Benefit | Description |
|---|---|
| No browser timeouts | Large batches (500+) won't hit 30s/60s proxy timeout |
| Better UX | User sees progress ("250 of 500...") instead of spinning wheel |
| Retry for manual posts | If worker crashes mid-batch, SQS message retries. Sync mode loses the request. |
| Consistent architecture | Scheduled and manual use same SQS → Worker pipeline |

### Cons

| Con | Description |
|---|---|
| Complex UI integration | Workflow UI must implement polling or WebSocket listener |
| Latency for small batches | For 3-item post, async adds unnecessary round trips (submit + poll instead of one call) |
| Queue delay | If post queue has 50 scheduled messages waiting, manual post goes behind them |
| Dual code path | Either maintain sync + async, or force all through async (worse UX for small batches) |

### Effort: ~8-10 days (backend) + UI team work

### Recommendation

**Not needed now.** Typical manual batches are 1-50 items (5-30 seconds). Implement when users regularly post 200+ items manually or report browser timeouts.

---

## Feature 5: Notifications — SNS in Both Flows + Push Status to UI

**Diagram location:**
- Top section — "NOTIFICATIONS: Amazon SNS → Email Notifications, Slack Notifications, Teams Notifications, Other Channels"
- Bottom of Invoice Posting Flow — "Amazon SNS Notifications" (post completion alerts)
- Bottom of Feed Download Flow — "Amazon SNS Notifications: Feed Download Status, Success / Failure Alerts"

### What We Have Currently

- **Scheduled posts:** Results written to `generic_execution_history`. No push notification to anyone. Users must check the Status API.
- **Manual posts:** Synchronous response — user gets the result immediately.
- **Failure alerts:** CloudWatch Alarm → SNS → Email to ops team (DLQ depth, PostFailedCount).
- **Feed download completion:** No notification at all. Results logged to CloudWatch only.

SNS is used in **one place only** — CloudWatch Alarms for failures.

### What the Diagram Shows

**SNS appears TWICE in the architecture** — serving two different purposes:

```
1. TOP-LEVEL NOTIFICATIONS (centralized notification hub):
   Amazon SNS → Email / Slack / Teams / Other
   Purpose: Operational alerts, job status summaries, SLA notifications

2. BOTTOM OF INVOICE POSTING FLOW:
   After Step Functions completes → Amazon SNS Notification
   Purpose: "Job 1001 (InvitedClub) completed: 95 success, 5 failed"

3. BOTTOM OF FEED DOWNLOAD FLOW:
   After ECS Fargate feed completes → Amazon SNS Notification
   Purpose: "Feed Download Status: Supplier feed completed, 1,200 records downloaded"
            "Feed Download FAILED: COA feed timeout after 30 minutes"
```

### What We Need to Add

| Notification Point | Trigger | Message Content |
|---|---|---|
| Post batch completion (success) | AutoPostOrchestrator finishes batch | "InvitedClub Job 1001: 50 posted, 2 failed" |
| Post batch failure | Unhandled exception in posting | "InvitedClub Job 1001: FAILED — Oracle Fusion timeout" |
| Feed download success | FeedResult.Success returned | "InvitedClub Supplier Feed: 1,200 records downloaded" |
| Feed download failure | FeedResult.Failed returned | "InvitedClub COA Feed: FAILED — connection timeout" |

### Benefits

| Benefit | Description |
|---|---|
| Ops awareness | Team knows immediately when a scheduled batch completes or fails |
| Feed visibility | Currently feed downloads complete silently — no one knows unless they check logs |
| Proactive alerts | Don't wait for CloudWatch alarm thresholds — notify on every failure immediately |
| Dual notification approach | CloudWatch Alarms for infrastructure issues (DLQ, crashes) + SNS for business outcomes (post results, feed status) |

### Cons

| Con | Description |
|---|---|
| Notification volume | 4 scheduled batches/day × 2 clients + 2 feed downloads/day = 10 notifications/day |
| SNS cost | Negligible — first 1,000 emails/month are free |
| UI integration (optional) | If pushing to Workflow UI, need a notification panel or WebSocket |

### Effort: ~3-4 days

| Work Item | Effort |
|---|---|
| Create SNS topic for posting notifications | 0.5 day |
| Create SNS topic for feed notifications | 0.5 day |
| Add SNS publish call in AutoPostOrchestrator after batch completion | 1 day |
| Add SNS publish call in FeedWorker after feed download completion | 1 day |
| Add to CloudFormation (topics, subscriptions, IAM) | 0.5 day |
| **Total** | **~3.5 days** |

### Recommendation

**Medium priority.** The feed download notification is the most valuable — currently feeds complete silently and failures go unnoticed until someone manually checks. Post completion notifications are less critical since we already have CloudWatch Alarms for failures. Implement when the ops team wants proactive visibility into feed health.

---

## Feature 6: Additional ERP Clients (Media, Greenthal, Akron, MDS, RapidPay)

**Diagram location:** Purple box — "Supported Invoice Posting Clients" list and "ERP / External APIs" list

### What We Have Currently

Two plugins implemented:
- `InvitedClubPlugin` (Oracle Fusion — 3-step posting + feed downloads)
- `SevitaPlugin` (OAuth2 REST — validation + line grouping)

Framework ready for more:
- `IClientPlugin` interface
- `PluginRegistry` (client_type → plugin mapping)
- `GenericRestPlugin` framework (`DynamicRecord` + `generic_field_mapping` table)
- `generic_job_configuration` for config-driven onboarding

### What the Diagram Shows

5+ additional clients: Media (Advantage/MarginWorld), Greenthal, Akron, MDS, RapidPay, Other ERPs, (40+ clients)

### What's Needed Per New Client

| Work Item | Effort |
|---|---|
| Write plugin class (implements `IClientPlugin`) | 2-5 days per client |
| Create client-specific models (request/response) | 1-2 days |
| Insert `generic_job_configuration` rows | 0.5 day |
| Insert `generic_execution_schedule` rows | 0.5 day |
| Write unit + integration tests | 2-3 days |
| **Total per client** | **~6-11 days** |

### Recommendation

**Out of scope for now.** InvitedClub + Sevita are the priority. Other clients will be added one at a time after the platform is proven in production. The plugin architecture ensures each new client is isolated and doesn't affect existing ones.

---

## Feature 7: External Feed Sources (SOAP, FTP/SFTP)

**Diagram location:** Green box right side — "EXTERNAL FEED SOURCES: SOAP APIs, FTP/SFTP Servers, HTTP APIs, Other Sources"

### What We Have Currently

Only **REST API** feeds implemented (InvitedClub's Oracle Fusion REST endpoints for Suppliers, Addresses, Sites, COA).

The `generic_feed_configuration` table supports `feed_source_type` values: `REST`, `FTP`, `SFTP`, `S3`, `FILE` — but only `REST` has a working implementation.

### What's Missing

| Source Type | Example Client | What's Needed |
|---|---|---|
| SOAP | Media (Advantage) | SOAP client + XML parsing |
| FTP/SFTP | Future clients | FTP/SFTP download service |
| S3 | Future clients | S3 file watcher/polling |
| FILE | Legacy local files | File system reader |

### Effort

| Work Item | Effort |
|---|---|
| Generic SOAP feed download service | 3-4 days |
| Generic FTP/SFTP download service | 2-3 days |
| S3-based feed reader | 1-2 days |
| **Total** | **~7-9 days** (when needed) |

### Recommendation

**Not needed now.** InvitedClub uses REST. Sevita has no feed download (`FeedResult.NotApplicable()`). Build these when a client that requires SOAP/FTP is onboarded.

---

## Priority Summary

| # | Feature | Effort | Benefit at 40+ Clients / Lakh Volumes | Priority |
|---|---|---|---|---|
| 1 | **Step Functions Hybrid (ECS processing)** | 14 days | Critical — parallel processing, visual debugging, client isolation | **HIGH — implement before scaling** |
| 2 | **S3 Gateway Endpoint** (free) | 0.5 day | Better security, minor cost savings | **HIGH — Do immediately** |
| 3 | **SNS Notifications (Post + Feed completion)** | 3.5 days | Feed failure visibility, ops awareness across 40 clients | **HIGH — essential at scale** |
| 4 | **Parallel Batch Processing** | — | Included in Step Functions hybrid (Map State parallelism) | **Covered by Feature 1** |
| 5 | **Async Manual Post** | 8-10 days | Prevents timeouts on lakh-level manual posts | **Medium — needed for large volumes** |
| 6 | **VPC Interface Endpoints** | 2.5 days | Significant cost savings at 40+ client traffic volume | **Medium — measure NAT costs** |
| 7 | **Additional ERP Clients** | 6-11 days each | Platform growth | **Ongoing — one at a time** |
| 8 | **SOAP/FTP/SFTP Feed Sources** | 7-9 days | Required by some future clients | **When needed** |

---

## Suggested Implementation Order

### Phase 1 — Foundation for Scale (3 weeks)
1. **S3 Gateway Endpoint** (0.5 day) — free, zero risk
2. **Step Functions Hybrid Architecture** (14 days) — the backbone for 40+ clients and lakh-level volumes
3. **SNS Notifications** (3.5 days) — visibility across all clients

### Phase 2 — Production Hardening (when volume grows)
4. **Async Manual Post** — when users post 200+ items manually
5. **VPC Interface Endpoints** — when NAT costs exceed $60/month (likely at 40+ clients)

### Phase 3 — Client Expansion (ongoing)
6. **Additional ERP Clients** — one at a time, plugin by plugin
7. **SOAP/FTP/SFTP** — when a client needs it
