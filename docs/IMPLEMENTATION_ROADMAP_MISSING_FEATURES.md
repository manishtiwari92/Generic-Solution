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

### Target Architecture — Step Functions + .NET Lambda (Multi-Mode Execution)

**Key design decisions:**
- **Step Functions** for orchestration, visibility, and retry logic
- **Three execution modes** based on client type:
  - `PER_ITEM` — 1 Lambda per item (InvitedClub, Sevita, Caliber — external API clients)
  - `BATCH_FILE` — 1 Lambda for entire batch (Northstar, TMK, Jonas, Giti — file generation clients)
  - `HYBRID` — batch file generation + per-item API calls after (GitiSGP, Canon Wire, Worldwide)
- **ECS PostWorker retained as fallback** — batch file clients with 100K+ items stay on ECS (no Lambda timeout risk). Feature flag per client: `execution_target = 'LAMBDA' | 'ECS'`
- **All manual posts are async** — API returns 202 immediately, user polls for status
- **Incremental migration** — clients move from ECS to Lambda one at a time with rollback capability

```
TARGET FLOW:

PER_ITEM (InvitedClub, Sevita, Caliber):
  Trigger → {client}-post-queue → Start Lambda → Step Functions → Map (1 Lambda per item) → Aggregate → Notify

BATCH_FILE (Northstar, TMK, Jonas, Giti, SCSPA, Diray, ClubEssentials, etc.):
  Trigger → {client}-post-queue → Start Lambda → Step Functions → BatchProcessor (1 Lambda, ALL items) → Aggregate → Notify

HYBRID (GitiSGP, Canon Wire, Worldwide):
  Trigger → {client}-post-queue → Start Lambda → Step Functions → BatchFile Lambda → Map (per-item API) → Aggregate → Notify

Each client has its own dedicated SQS queue (dynamically provisioned).
The Start Workflow Lambda subscribes to ALL client queues via event source mappings.
All manual posts are ASYNC — user gets HTTP 202 + executionId immediately.
```

### Execution Mode Configuration

New column on `generic_job_configuration`:

```sql
ALTER TABLE generic_job_configuration ADD execution_mode VARCHAR(20) NOT NULL DEFAULT 'PER_ITEM';
-- Values: 'PER_ITEM', 'BATCH_FILE', 'HYBRID'

ALTER TABLE generic_job_configuration ADD execution_target VARCHAR(10) NOT NULL DEFAULT 'LAMBDA';
-- Values: 'LAMBDA', 'ECS'
-- Allows per-client rollback: set to 'ECS' to route back to ECS PostWorker

ALTER TABLE generic_job_configuration ADD max_concurrency INT NOT NULL DEFAULT 50;
-- Per-client MaxConcurrency for PER_ITEM Map state (respects ERP API rate limits)

ALTER TABLE generic_job_configuration ADD suspected_duplicates_queue_id INT NULL;
-- For clients with duplicate detection (Northstar Golf jobs, Caliber)
```

**Client classification:**

| Client | execution_mode | Why |
|---|---|---|
| InvitedClub | PER_ITEM | 3 API calls to Oracle Fusion per item, ~7s each |
| Sevita | PER_ITEM | OAuth2 API call per item, ~3s each |
| Caliber | PER_ITEM | Invoice + Payment + Document APIs per item, ~5-8s each |
| Northstar | BATCH_FILE | CSV grouped by CompanyCode, no external API |
| TMK | BATCH_FILE | Pipe-delimited with header/sequence, no external API |
| Jonas/ClubJonas | BATCH_FILE | Grouped by month, no external API |
| Giti | BATCH_FILE | H/L/S pipe-delimited + image ZIP, no external API |
| SCSPA | BATCH_FILE | Pipe-delimited + AccountingDate SP, no external API |
| Diray | BATCH_FILE | Digital/non-digital split CSV, no external API |
| ClubEssentials | BATCH_FILE | Custom format file, no external API |
| Cobalt | BATCH_FILE | Custom format per-item files, no external API |
| S2K | BATCH_FILE | Custom format, no external API |
| Quickbooks | BATCH_FILE | Pipe-delimited CSV, no external API |
| Sharp | BATCH_FILE | Custom per-item files, no external API |
| GitiSGP | HYBRID | H/L/S file + ApprovalAuditMergeAPI per item |
| Canon | HYBRID | Pipe-delimited file + WireRequestMergeAPI per item |
| Worldwide | HYBRID | CoverPage merge API per item (no file gen) |

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
│ TRIGGERS (same for all clients)                                          │
│                                                                          │
│ SCHEDULED:                                                               │
│   EventBridge Rule (ips-autopost-{jobId}-post)                           │
│     → drops message into: ips-post-{clientType}-{env}                    │
│                                                                          │
│ MANUAL:                                                                  │
│   Workflow UI → POST /api/post/{jobId}/items/{itemIds}                   │
│     → .NET API looks up client_type + execution_target from jobId        │
│     → IF execution_target == 'ECS': route to ECS PostWorker (existing)   │
│     → IF execution_target == 'LAMBDA': derives queue, sends message      │
│     → returns HTTP 202: { executionId: "exec-abc-123", status: "queued" }│
└───────────────────────────────┬─────────────────────────────────────────┘
                                │
                                ▼
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
│ START WORKFLOW LAMBDA (.NET — lightweight, same for all)                  │
│   • Validate SQS message                                                 │
│   • Load basic config (job_id, client_type, execution_mode)              │
│   • Start Step Functions execution                                       │
│   • Pass: { jobId, clientType, triggerType, executionId, executionMode } │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │
                                ▼
╔═════════════════════════════════════════════════════════════════════════════╗
║ AWS STEP FUNCTIONS — UNIFIED STATE MACHINE (branches by execution_mode)    ║
║                                                                            ║
║  State 1: PREPARE EXECUTION (.NET Lambda — same for all modes)             ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ • Load full job config from DB (including execution_mode)          │    ║
║  │ • OnBeforePostAsync logic:                                        │    ║
║  │   → Sevita: load ValidIds, cache to S3                            │    ║
║  │   → InvitedClub: run RetryPostImages                             │    ║
║  │   → Caliber: check future-dated invoice queue (1st of month)     │    ║
║  │ • Fetch all pending ItemIds: WHERE PostInProcess=0                │    ║
║  │ • Cache config to S3: s3://ips-temp/{executionId}/config.json     │    ║
║  │ • Return: { executionMode, itemIds, totalCount, maxConcurrency,   │    ║
║  │             configS3Key }                                         │    ║
║  │                                                                    │    ║
║  │ NOTE: Batch data is NOT cached to S3. The Batch Processor Lambda  │    ║
║  │ queries DB directly (1 Lambda, 1 query — no pressure).            │    ║
║  └───────────────────────────────────┬───────────────────────────────┘    ║
║                                      │                                     ║
║  ┌───────────────────────────────────┴───────────────────────────────┐    ║
║  │              CHOICE STATE (branch by executionMode)                │    ║
║  │   "PER_ITEM"   → State 2A (Map — 1 Lambda per item)              │    ║
║  │   "BATCH_FILE" → State 2B (Single Lambda — all items)             │    ║
║  │   "HYBRID"     → State 2C (Batch file + per-item API calls)       │    ║
║  └───────┬─────────────────────────────┬──────────────────┬─────────┘    ║
║          ▼                             ▼                  ▼               ║
║                                                                            ║
║  ═══ PATH A: PER_ITEM (InvitedClub, Sevita, Caliber) ═══════════════════  ║
║                                                                            ║
║  State 2A: PROCESS ITEMS (Map State — 1 Lambda per item)                   ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ MaxConcurrency: from config (default 50, per-client override)      │    ║
║  │ Input: each itemId from the array                                 │    ║
║  │                                                                    │    ║
║  │   ┌─────────────────────────────────────────────────────────────┐ │    ║
║  │   │ ITEM PROCESSOR LAMBDA (.NET — 1 item per invocation)        │ │    ║
║  │   │                                                              │ │    ║
║  │   │   Receives: { jobId, clientType, itemId, configS3Key }      │ │    ║
║  │   │                                                              │ │    ║
║  │   │   • Load config from S3 cache (NOT from DB every time)      │ │    ║
║  │   │   • Load workitem header + detail data from DB              │ │    ║
║  │   │   • Set PostInProcess = 1                                   │ │    ║
║  │   │   • Get image from S3 (if applicable)                      │ │    ║
║  │   │   • Call ERP API:                                           │ │    ║
║  │   │     → InvitedClub: PostInvoice→PostAttachment→CalcTax      │ │    ║
║  │   │     → Caliber: PostInvoice→PostPayment→PostDocument         │ │    ║
║  │   │     → Sevita: PostInvoice (JSON array payload)             │ │    ║
║  │   │   • Route workitem (WORKITEM_ROUTE SP)                     │ │    ║
║  │   │   • Write history per item                                 │ │    ║
║  │   │   • Clear PostInProcess = 0 (in finally)                   │ │    ║
║  │   │   • Returns: { itemId, success, queueId, errorMsg }        │ │    ║
║  │   │   Time: 5-15 seconds | Memory: 512MB                       │ │    ║
║  │   └─────────────────────────────────────────────────────────────┘ │    ║
║  │                                                                    │    ║
║  │ Retry per item:                                                   │    ║
║  │   If Oracle returns 503 → wait 5s → retry same item (up to 2x)  │    ║
║  │   If still fails → mark failed, continue with next items         │    ║
║  └───────────────────────────────────┬───────────────────────────────┘    ║
║                                      │                                     ║
║  ═══ PATH B: BATCH_FILE (Northstar, TMK, Jonas, Giti, SCSPA, CSV) ═══════ ║
║                                                                            ║
║  State 2B: BATCH FILE PROCESSOR (Single Lambda — ALL items at once)        ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ Receives: { jobId, clientType, itemIds, configS3Key }             │    ║
║  │                                                                    │    ║
║  │ • Load config from S3 cache (credentials already resolved)        │    ║
║  │ • Query DB directly for full header+detail DataSet (ONE query)    │    ║
║  │   → Same SP as ECS: WFGeneric_Post_GetHeaderDataByJobID           │    ║
║  │   → 20K items: ~500ms. No S3 serialization overhead.             │    ║
║  │ • Run validations (duplicate detection, ChargeAccount, etc.)      │    ║
║  │ • Group items by key (CompanyCode, Month, InvoiceType)            │    ║
║  │ • Generate file(s) per group (CSV, pipe-delimited, H/L/S, Excel) │    ║
║  │ • Write files to S3: s3://ips-output-files/{clientType}/          │    ║
║  │ • Route ALL items via parallel DB calls (SemaphoreSlim=20):       │    ║
║  │   → WORKITEM_ROUTE + GENERALLOG_INSERT + history per item         │    ║
║  │ • Returns: { total, success, failed, filesGenerated[] }           │    ║
║  │                                                                    │    ║
║  │ Time: 10K→20s, 20K→30s, 50K→60s, 100K→3.5min                    │    ║
║  │ Memory: 1024MB | Timeout: 5 minutes                               │    ║
║  │ NO external API calls. NO PostInProcess flag needed.              │    ║
║  │                                                                    │    ║
║  │ NOTE: For clients with 500K+ items, set execution_target='ECS'.   │    ║
║  │ ECS PostWorker has no timeout limit.                              │    ║
║  └───────────────────────────────────┬───────────────────────────────┘    ║
║                                      │                                     ║
║  ═══ PATH C: HYBRID (GitiSGP, Canon Wire, Worldwide) ════════════════════ ║
║                                                                            ║
║  State 2C-1: BATCH FILE GENERATION (Single Lambda)                         ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ • Generate file (same as Path B)                                   │    ║
║  │ • Copy images to output folder (Giti jobs)                        │    ║
║  │ • Route file-only items to success queue                          │    ║
║  │ • Returns: { apiItemIds: [...items needing API call...] }         │    ║
║  └───────────────────────────────────┬───────────────────────────────┘    ║
║                                      │                                     ║
║  State 2C-2: POST-FILE API CALLS (Map State — per-item)                    ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ MaxConcurrency: 10 (these APIs are typically rate-limited)         │    ║
║  │ • GitiSGP: Call ApprovalAuditMergeAPI (Login→POST→Logout)        │    ║
║  │ • Canon: Call WireRequestMergeAPI (Login→POST→Logout)             │    ║
║  │ • Worldwide: Call CoverPage merge via Asset API                   │    ║
║  │ • Route item to success/fail queue                                │    ║
║  │ • Returns: { itemId, success, errorMsg }                          │    ║
║  └───────────────────────────────────┬───────────────────────────────┘    ║
║                                      │                                     ║
║  ═══ SHARED STATES (all paths converge here) ═════════════════════════════ ║
║                                                                            ║
║  State 3: AGGREGATE RESULTS (.NET Lambda — same for all modes)             ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ • Collect results from whichever path ran (2A, 2B, or 2C)         │    ║
║  │ • Count success/failed                                            │    ║
║  │ • Write generic_execution_history                                 │    ║
║  │ • Update LastPostTime                                             │    ║
║  │ • Publish CloudWatch metrics                                      │    ║
║  │ • Send batch-level emails (plugin-specific)                       │    ║
║  │ • Clean up S3 temp: s3://ips-temp/{executionId}/* (config only)    │    ║
║  │ • Return: { total: 5000, success: 4800, failed: 200 }            │    ║
║  └───────────────────────────────────┬───────────────────────────────┘    ║
║                                      │                                     ║
║  State 4: NOTIFY (.NET Lambda)                                             ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ • Publish ONE email via SNS (entire execution summary)            │    ║
║  │ • ONE notification per execution — NOT per item                   │    ║
║  └───────────────────────────────────────────────────────────────────┘    ║
║                                                                            ║
║  CATCH: ERROR HANDLER (.NET Lambda)                                        ║
║  ┌───────────────────────────────────────────────────────────────────┐    ║
║  │ • Log failure, mark execution as FAILED in DB                     │    ║
║  │ • Send failure email via SNS                                      │    ║
║  │ • Clean up S3 temp files                                          │    ║
║  └───────────────────────────────────────────────────────────────────┘    ║
║                                                                            ║
╚═════════════════════════════════════════════════════════════════════════════╝
```

### RDS Proxy — Connection Pooling for Lambda-at-Scale

**The Problem Without RDS Proxy:**

Each Lambda invocation opens its own database connection. With `MaxConcurrency=50`:
- 50 concurrent Lambdas = 50 simultaneous DB connections (just for one client)
- 40 clients × 50 concurrent = **2,000 simultaneous connections** at peak
- SQL Server max connections = depends on instance size (typically 500-32,000)
- Lambda connections are SHORT-LIVED — opened, used for 5 seconds, closed. This creates rapid open/close churn that stresses the DB.

```
WITHOUT RDS Proxy:
  Lambda 1 → OPEN connection → query → INSERT → CLOSE (7 sec)
  Lambda 2 → OPEN connection → query → INSERT → CLOSE (7 sec)
  Lambda 3 → OPEN connection → query → INSERT → CLOSE (7 sec)
  ... × 50 concurrent
  
  SQL Server sees: 50 connections open → 50 connections close → 50 new connections open
  Every 7 seconds = constant connection churn
  Each OPEN costs ~30-50ms (TCP handshake + TLS + auth)
```

**The Solution — RDS Proxy:**

RDS Proxy sits between Lambda and RDS. It maintains a **persistent pool** of connections to the database. Lambdas connect to the proxy (fast — already established), and the proxy reuses its existing connections to RDS.

```
WITH RDS Proxy:
  Lambda 1 → connect to Proxy (< 5ms, reuses pooled connection) → query → done
  Lambda 2 → connect to Proxy (< 5ms, reuses pooled connection) → query → done
  Lambda 3 → connect to Proxy (< 5ms, reuses pooled connection) → query → done
  ... × 50 concurrent

  RDS Proxy maintains: 20 persistent connections to SQL Server (never closes them)
  50 Lambdas share those 20 connections via multiplexing
  SQL Server sees: 20 stable, long-lived connections. No churn.
```

**Layman analogy:** Without proxy = 50 people each calling the restaurant to place an order (50 phone calls, phone lines overwhelmed). With proxy = 1 secretary takes all 50 orders and calls the restaurant once with a list (1 phone call, restaurant not overwhelmed).

**What RDS Proxy does:**

| Feature | Description |
|---|---|
| **Connection pooling** | Maintains a pool of open DB connections. Lambdas borrow a connection, use it, return it. No open/close overhead. |
| **Connection multiplexing** | 50 Lambda requests can share 20 actual DB connections by taking turns (each Lambda only needs it for milliseconds per query). |
| **Connection reuse** | When Lambda finishes and the next Lambda starts, it reuses the SAME underlying DB connection. No new TCP/TLS handshake. |
| **Failover handling** | If RDS fails over to a standby, the proxy automatically reconnects. Lambdas don't notice. |
| **Credential management** | Proxy reads DB credentials from Secrets Manager. Lambdas don't need DB passwords — they authenticate to the proxy via IAM. |

**How it fits in our architecture:**

```
Lambda (Batch Processor)
    │
    │ "I need to query workitem data and write history"
    │
    ▼
RDS Proxy (ips-autopost-proxy-prod)
    │
    │ Borrows a connection from its pool (< 5ms)
    │ Executes the query on the Lambda's behalf
    │ Returns the connection to the pool
    │
    ▼
RDS SQL Server (ips-rds-database-1)
    │
    │ Sees only 20-50 persistent connections (not 2,000 churning ones)
```

**Connection math at scale:**

| Scenario | Without Proxy | With Proxy |
|---|---|---|
| 1 client, 50 concurrent Lambdas | 50 connections | ~10 pooled connections |
| 10 clients, 50 concurrent each | 500 connections | ~50 pooled connections |
| 40 clients, 50 concurrent each | 2,000 connections | ~100-200 pooled connections |
| Peak burst (all 40 fire simultaneously) | 2,000+ connections (may exceed DB limit!) | Still 100-200 (proxy queues excess) |

**Cost:**

```
RDS Proxy pricing: $0.015 per vCPU-hour of the underlying DB instance

Example: RDS instance with 4 vCPUs
  Cost: 4 × $0.015 × 24 hours × 30 days = $43.20/month

This is a FIXED cost regardless of how many Lambdas connect.
```

**Why it's essential for our Lambda-per-item architecture:**

| Without RDS Proxy | With RDS Proxy |
|---|---|
| Each Lambda opens/closes a connection (50ms overhead × 2 per item) | Connection borrowed in < 5ms |
| 40 clients × 50 concurrent = 2,000 connections → DB overwhelmed | Proxy pools to 100-200 connections → DB relaxed |
| DB sees constant open/close churn → CPU wasted on connection management | DB sees stable long-lived connections → CPU used for queries |
| Lambda cold start + new connection = 3+ seconds | Lambda connects to proxy in < 5ms (even on cold start) |
| If DB fails over, all 2,000 connections drop → mass Lambda failures | Proxy handles failover transparently → Lambdas don't notice |

**Configuration in our architecture:**

```yaml
# CloudFormation — RDS Proxy
RdsProxy:
  Type: AWS::RDS::DBProxy
  Properties:
    DBProxyName: ips-autopost-proxy-{env}
    EngineFamily: SQLSERVER
    Auth:
      - AuthScheme: SECRETS
        SecretArn: !Ref WorkflowDbSecret
        IAMAuth: REQUIRED
    RoleArn: !GetAtt RdsProxyRole.Arn
    VpcSubnetIds: [!Ref PrivateSubnetA, !Ref PrivateSubnetB]
    VpcSecurityGroupIds: [!Ref EcsSecurityGroup]
```

**Code change in Lambda:** Replace the direct RDS connection string with the proxy endpoint:

```csharp
// Instead of:
var connectionString = "Server=ips-rds-database-1.cmrmduasa2gk.us-east-1.rds.amazonaws.com;Database=Workflow;..."

// Use:
var connectionString = "Server=ips-autopost-proxy-prod.proxy-cmrmduasa2gk.us-east-1.rds.amazonaws.com;Database=Workflow;..."
```

Same connection string format — just a different hostname. No other code changes.

### Why .NET Lambda Instead of ECS (Per Execution Mode)

| Aspect | PER_ITEM Lambda | BATCH_FILE Lambda | ECS (fallback) |
|---|---|---|---|
| **Use case** | External API per item | File gen (no API) | 500K+ items or network share |
| **Timeout risk** | None — 7s << 15 min | None — 100K items in 3.5 min | None (no limit) |
| **Infrastructure** | Zero — fully serverless | Zero — fully serverless | Docker, ECR, task defs |
| **Cost (5K items)** | ~$0.86 | ~$0.001 (1 Lambda call!) | $35/month fixed |
| **Parallelism** | 50 concurrent items | N/A (1 Lambda, all items) | SemaphoreSlim in-process |
| **Per-item retry** | Step Functions auto-retry | N/A (re-run entire batch) | Manual retry logic |
| **Per-item visibility** | SF console shows each item | 1 execution entry | CloudWatch logs only |
| **Scale to zero** | Yes — no cost when idle | Yes | No — MinCapacity=1 |
| **Network shares** | ❌ Cannot access | ❌ Cannot access | ✅ Can mount shares |
| **Memory limit** | 512MB (1 item) | 2048MB (full DataSet) | No limit |

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
| 1A | **ECS Parallel Processing (SemaphoreSlim)** | Immediate 10-50× speedup, zero new infra | **CRITICAL — do first** |
| 1B | **Per-Client SQS Queues** | Client isolation, per-client DLQ/monitoring | **HIGH — foundation** |
| 1C | **Step Functions + Lambda (PER_ITEM)** | Serverless parallel for API clients | **HIGH — after 1A proves parallelism** |
| 1D | **Batch File Plugin Framework** | Support 20+ file-generation clients | **HIGH — before scaling to 40+ clients** |
| 1E | **Hybrid Mode + Caliber Plugin** | Complete client coverage | **MEDIUM — after 1C+1D stable** |
| 2 | **Async Manual Post (HTTP 202)** | UI responsiveness at scale | **Covered by Phase 1C** |
| 3 | **SNS Email Notifications** | Ops visibility per execution | **Part of Phase 1C (State 4)** |

---

## Suggested Implementation Order

### Phase 1A — ECS Parallel Processing (1-2 weeks, immediate value)
1. Add `SemaphoreSlim`-based parallelism to `AutoPostOrchestrator` for PER_ITEM clients
2. Add `max_concurrency` column to `generic_job_configuration` (configurable per client)
3. Immediate 10-50× speedup for InvitedClub/Sevita with zero new infrastructure
4. This solves the sequential bottleneck TODAY while Lambda architecture is built

### Phase 1B — Per-Client SQS Queues (2-3 weeks)
1. Scheduler Lambda provisions queues dynamically (CreateQueue is idempotent)
2. EventBridge targets per-client queue instead of shared `ips-post-queue`
3. PostWorker consumes from per-client queue (still ECS during transition)
4. Client isolation achieved without Lambda

### Phase 1C — Step Functions + Lambda for PER_ITEM Clients (4-6 weeks)
1. RDS Proxy deployment (mandatory for Lambda-at-scale)
2. Add `execution_mode` and `execution_target` columns to `generic_job_configuration`
3. Implement Prepare + Item Processor + Aggregate + Notify Lambdas
4. Step Functions state machine with Choice state (3 paths)
5. Feature flag: `execution_target='LAMBDA'` per client
6. Migrate ONE client (InvitedClub) first, validate for 2 weeks
7. Then migrate Sevita, then Caliber (one at a time)
8. Keep `execution_target='ECS'` as instant rollback

### Phase 1D — Batch File Plugin Framework (3-4 weeks)
1. Implement `IFileGenerationService` (CSV, pipe-delimited, Excel output adapters)
2. Implement `GreenthalPlugin` with internal strategy routing by `JobType`
3. Implement Batch File Processor Lambda (Path B in state machine)
4. Implement file strategies: Northstar, Giti, GitiSGP, TMK, Jonas, generic CSV
5. Add `suspected_duplicates_queue_id` to `generic_job_configuration`
6. Test with Northstar (simplest batch file client) first
7. Network share clients remain on ECS; S3-output clients go to Lambda

### Phase 1E — Hybrid Mode + Remaining Clients (2-3 weeks)
1. Implement Hybrid path (State 2C-1 + 2C-2) for GitiSGP, Canon, Worldwide
2. Implement `CalibarPlugin` (Invoice + Payment + Document + Feed download)
3. SNS email notifications after every execution (State 4)
4. API change: return 202 + progress tracking via Step Functions execution status

### Phase 2 — Decommission ECS PostWorker for Lambda Clients (after 30 days stable)
1. Only after all Lambda-target clients are stable for 30+ days
2. ECS PostWorker remains for: feed downloads, network-share file clients, 500K+ batch clients
3. Reduce ECS PostWorker capacity (fewer tasks needed — only handles remaining clients)

### Rollback Plan (per phase)
- Phase 1A: Revert `max_concurrency` to 1 (sequential processing)
- Phase 1B: Point EventBridge back to shared `ips-post-queue`
- Phase 1C: Set `execution_target='ECS'` per client (instant, no deployment)
- Phase 1D: Set `execution_target='ECS'` for batch file clients
- Phase 1E: Same as 1D — feature flag rollback

---

## Out of Scope (Future — When New Clients Are Onboarded)

- **Additional ERP Clients** (Media, Greenthal, Akron, MDS, RapidPay, etc.) — will be added one at a time via plugin architecture when those clients are ready. Not needed for InvitedClub + Sevita.
- **External Feed Sources (SOAP, FTP/SFTP)** — only REST feeds are needed for InvitedClub. Sevita has no feed download. SOAP/FTP/SFTP services will be built when a client that requires them is onboarded.
