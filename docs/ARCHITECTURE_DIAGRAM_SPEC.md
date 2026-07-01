# Architecture Diagram Specification
## IPS.AutoPost Platform — AWS Native Architecture (Updated)

> Use this spec to recreate the architecture diagram in draw.io, Lucidchart, or similar tool.
> Based on: IMPLEMENTATION_ROADMAP_MISSING_FEATURES.md (final version with multi-mode execution)

---

## Diagram Layout (Same structure as the original, updated content)

### HEADER BAR (full width, dark blue background)

```
GENERIC ERP INTEGRATION SOLUTION — AWS NATIVE ARCHITECTURE
Scalable, Secure, and Cost-Optimized Integration Platform for Multiple ERP Systems
Multi-Mode Execution: PER_ITEM | BATCH_FILE | HYBRID
```

---

### TOP ROW — 4 boxes across (light gray background)

```
┌─────────────────────┐  ┌─────────────────────┐  ┌─────────────────────┐  ┌─────────────────────┐
│ TRIGGERS            │  │ WORKFLOW UI          │  │ NOTIFICATIONS        │  │ ALERTS & ALARMS     │
│ (Event Sources)     │  │                     │  │                      │  │                     │
│ [EventBridge icon]  │  │ [Browser icon]      │  │ [SNS icon]           │  │ [CloudWatch icon]   │
│                     │  │                     │  │                      │  │                     │
│ • Hourly Jobs       │  │ • Monitor Jobs      │  │ • Email (SNS)        │  │ • Job Failures      │
│ • Daily Jobs        │  │ • Manual Triggers   │  │ • Per-Execution      │  │ • Performance       │
│ • Per-Client Sched  │  │ • View Status       │  │   Summary            │  │ • DLQ Alerts        │
│ • Feed Downloads    │  │ • Poll Progress     │  │ • Success/Failure    │  │ • System Health     │
│ • Cron Expressions  │  │ • Dashboard         │  │   Alerts             │  │ • SLA Violations    │
└─────────────────────┘  └─────────────────────┘  └─────────────────────┘  └─────────────────────┘
```

**Arrows from top row:**
- EventBridge → "DIRECT TO SQS (Scheduled)" → Per-Client SQS Queues
- Workflow UI → "VIA API (Manual)" → .NET API

---

### SECTION 1 — INVOICE POSTING FLOW (CONFIG DRIVEN) — Left 60% of page

```
┌─────────────────────────────────────────────────────────────────────────────────────┐
│  1   INVOICE POSTING FLOW (CONFIG DRIVEN) — Multi-Mode Execution                    │
│                                                                                      │
│  ┌──── MANUAL (On Demand) ────┐     ┌──── SCHEDULED (Automated) ────┐               │
│  │                            │     │                               │               │
│  │  [Workflow UI]             │     │  [Amazon EventBridge]         │               │
│  │       │                    │     │       │                       │               │
│  │       ▼                    │     │       │                       │               │
│  │  [.NET API]               │     │       │                       │               │
│  │  • Auth (OAuth/AD)         │     │       │                       │               │
│  │  • Returns HTTP 202        │     │       │                       │               │
│  │  • Request Validation      │     │       │                       │               │
│  │  • Derives client queue    │     │       │                       │               │
│  └────────┬───────────────────┘     └───────┤                       │               │
│           │                                  │                       │               │
│           ▼                                  ▼                       │               │
│  ┌────────────────────────────────────────────────────────────────┐ │               │
│  │  PER-CLIENT SQS QUEUES (Dynamically Provisioned)               │ │               │
│  │  [Amazon SQS icon]                                             │ │               │
│  │                                                                 │ │               │
│  │  ips-post-invitedclub-{env}    • Reliable Delivery             │ │               │
│  │  ips-post-sevita-{env}         • Client Isolation              │ │               │
│  │  ips-post-caliber-{env}        • Per-Client DLQ               │ │               │
│  │  ips-post-northstar-{env}      • Independent Retry            │ │               │
│  │  ips-post-giti-{env}           • Backpressure Handling        │ │               │
│  │  ... (one per active job)                                      │ │               │
│  └────────┬───────────────────────────────────────────────────────┘ │               │
│           │                                                                          │
│           │         ┌─────────────────────────────────┐                             │
│           │         │  Per-Client DLQ                  │                             │
│           ├────────►│  ips-post-{client}-dlq-{env}    │ Failed messages              │
│           │         │  • CloudWatch Alarm             │ for troubleshooting          │
│           │         │  • SNS Alert on arrival         │                             │
│           │         └─────────────────────────────────┘                             │
│           ▼                                                                          │
│  ┌────────────────────────────────────────────────────────────────┐                 │
│  │  START WORKFLOW LAMBDA (SQS Consumer)                           │                 │
│  │  [Lambda icon]                                                  │                 │
│  │                                                                 │                 │
│  │  • Validate & Enrich Message                                   │                 │
│  │  • Load Configuration (execution_mode, execution_target)       │                 │
│  │  • If target='ECS' → forward to ECS queue (rollback path)     │                 │
│  │  • If target='LAMBDA' → Start Step Functions Execution         │                 │
│  └────────┬───────────────────────────────────────────────────────┘                 │
│           │                                                                          │
│           ▼                                                                          │
│  ╔════════════════════════════════════════════════════════════════════╗               │
│  ║  AWS STEP FUNCTIONS — UNIFIED STATE MACHINE                       ║               │
│  ║                                                                    ║               │
│  ║  ┌──────────┐    ┌──────────┐    ┌──────────────┐    ┌────────┐  ║               │
│  ║  │ Prepare  │───►│  Choice  │───►│  Process     │───►│Aggreg- │  ║               │
│  ║  │ Execution│    │  State   │    │  (3 paths)   │    │  ate   │  ║               │
│  ║  └──────────┘    └──────────┘    └──────────────┘    └────────┘  ║               │
│  ║       │               │                │                  │       ║               │
│  ║       │               │                │                  │       ║               │
│  ║  Load config     Branch by         Path A/B/C         Write      ║               │
│  ║  Fetch ItemIds   execution_mode    (see below)        history    ║               │
│  ║  Cache to S3                                          Metrics    ║               │
│  ║                                                       Email      ║               │
│  ║                                                       Notify     ║               │
│  ╚════════════════════════════════════════════════════════════════════╝               │
│                                                                                      │
│  ┌─────────────────────────────────────────────────────────────────────────────────┐│
│  │  THREE EXECUTION PATHS (Config-Driven per Job)                                   ││
│  │                                                                                   ││
│  │  PATH A: PER_ITEM          PATH B: BATCH_FILE        PATH C: HYBRID              ││
│  │  ┌─────────────────┐      ┌─────────────────┐      ┌─────────────────┐          ││
│  │  │ [Map State]     │      │ [Single Lambda] │      │ [Lambda + Map]  │          ││
│  │  │                 │      │                 │      │                 │          ││
│  │  │ 1 Lambda per    │      │ 1 Lambda for    │      │ File gen first  │          ││
│  │  │ item (parallel) │      │ ALL items       │      │ then per-item   │          ││
│  │  │                 │      │                 │      │ API calls       │          ││
│  │  │ MaxConcurrency  │      │ Query DB once   │      │                 │          ││
│  │  │ = 50 (per-client│      │ Generate files  │      │ MaxConcurrency  │          ││
│  │  │  configurable)  │      │ Route all items │      │ = 10            │          ││
│  │  │                 │      │ (SemaphoreSlim  │      │                 │          ││
│  │  │ Calls ERP API   │      │  = 20 parallel) │      │                 │          ││
│  │  │ per item        │      │                 │      │                 │          ││
│  │  ├─────────────────┤      ├─────────────────┤      ├─────────────────┤          ││
│  │  │ InvitedClub     │      │ Northstar       │      │ GitiSGP         │          ││
│  │  │ Sevita          │      │ TMK             │      │ Canon Wire      │          ││
│  │  │ Caliber         │      │ Jonas/ClubJonas │      │ Worldwide       │          ││
│  │  │                 │      │ Giti            │      │                 │          ││
│  │  │                 │      │ SCSPA, Diray    │      │                 │          ││
│  │  │                 │      │ Cobalt, Sharp   │      │                 │          ││
│  │  │                 │      │ Generic CSV     │      │                 │          ││
│  │  └─────────────────┘      └─────────────────┘      └─────────────────┘          ││
│  └─────────────────────────────────────────────────────────────────────────────────┘│
│                                                                                      │
│  ┌─────────────────────────────────────────────────────────────────────────────────┐│
│  │  ERP INTEGRATIONS (CONFIG DRIVEN)                                                ││
│  │                                                                                   ││
│  │  [Oracle Fusion]  [Caliber]  [Sevita AP]  [NetSuite]  [Other ERPs/APIs]          ││
│  │                                                                                   ││
│  │  File Outputs: CSV | Pipe-Delimited | Excel | H/L/S Format | TMK Custom          ││
│  └─────────────────────────────────────────────────────────────────────────────────┘│
│                                                                                      │
│  ┌─────────────────────────────────────────────────────────────────────────────────┐│
│  │  Amazon SNS Notifications                                                        ││
│  │  [SNS icon]  • ONE email per execution (not per item)                            ││
│  │              • Success/Failure summary • Files generated list                    ││
│  └─────────────────────────────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────────────────────────────┘
```

---

### SECTION 2 — FEED DOWNLOAD / LONG RUNNING PROCESSING FLOW — Top Right

```
┌─────────────────────────────────────────────────────────────────────────┐
│  2   FEED DOWNLOAD / LONG RUNNING PROCESSING FLOW                       │
│                                                                          │
│  [Amazon EventBridge] ──► [feed-download-queue]  • Reliable Delivery    │
│  (Scheduled Jobs)         [Amazon SQS]           • Backpressure         │
│                                                   • Retry Support        │
│           │                                                              │
│           ▼                                                              │
│  ┌────────────────────────────────────────┐                             │
│  │  ECS Fargate Workers                   │                             │
│  │  [ECS icon]                            │                             │
│  │                                        │                             │
│  │  • Download & Processing               │                             │
│  │  • Parallel Workers (SemaphoreSlim)    │                             │
│  │  • Auto Scaling (1-5 tasks)           │                             │
│  │  • No execution time limit            │                             │
│  │  • Also handles ECS-target post jobs  │                             │
│  └────────────────────────────────────────┘                             │
│                                                                          │
│  SUPPORTED SOURCES:                                                      │
│  [FTP/SFTP]  [SOAP APIs]  [HTTP/REST APIs]  [File Shares]  [S3]        │
│                                                                          │
│  ┌────────────────────────────────────────┐                             │
│  │  Amazon S3 Feed Archive                │                             │
│  │  • Raw Feeds                           │                             │
│  │  • Processed Files                     │                             │
│  │  • Archive & History                   │                             │
│  └────────────────────────────────────────┘                             │
│                                                                          │
│  ┌────────────────────────────────────────┐                             │
│  │  Amazon RDS (SQL Server)              │                             │
│  │  • Feed Metadata                      │                             │
│  │  • Processing Status                  │                             │
│  │  • Job History                        │                             │
│  └────────────────────────────────────────┘                             │
│                                                                          │
│  [Amazon SNS Notifications]                                              │
│  • Feed Download Status                                                  │
│  • Success / Failure Alerts                                              │
└─────────────────────────────────────────────────────────────────────────┘
```

---

### SECTION 3 — SHARED DATA & SERVICES — Right sidebar

```
┌───────────────────────────────────────────┐
│  SHARED DATA & SERVICES                    │
│                                            │
│  [RDS icon] Amazon RDS (SQL Server)        │
│     Job metadata, Configurations,          │
│     Processing history, Workitems          │
│                                            │
│  [RDS Proxy icon] Amazon RDS Proxy         │
│     Connection pooling for Lambda,         │
│     Multiplexing 50 concurrent to ~20 DB   │
│                                            │
│  [S3 icon] Amazon S3                       │
│     Invoice images, Feed archives,         │
│     Output files, Config cache             │
│                                            │
│  [Secrets icon] AWS Secrets Manager        │
│     ERP credentials, DB passwords,         │
│     API keys (resolved at startup)         │
│                                            │
│  [SQS icon] Amazon SQS                    │
│     Per-client post queues,                │
│     Feed queue, DLQs                       │
│                                            │
│  [ECR icon] Amazon ECR                     │
│     Container images for ECS workers       │
│     and Lambda containers                  │
│                                            │
│  [SF icon] AWS Step Functions              │
│     Orchestration, retry logic,            │
│     per-item visibility, multi-mode        │
│                                            │
│  [Lambda icon] AWS Lambda                  │
│     Serverless compute for posting,        │
│     Start Workflow, Aggregate, Notify      │
│                                            │
├───────────────────────────────────────────┤
│  OBSERVABILITY & MONITORING                │
│                                            │
│  [CW icon] Amazon CloudWatch              │
│     Logs, Metrics, Alarms, Dashboards,    │
│     Per-client PostSuccess/PostFailed     │
│                                            │
│  [SNS icon] Amazon SNS                    │
│     Post/Feed execution notifications,    │
│     DLQ alerts, Failure alerts            │
└───────────────────────────────────────────┘
```

---

### SECTION 4 — SECURITY & NETWORK FOUNDATION — Bottom bar

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│  SECURITY & NETWORK FOUNDATION                                                        │
│                                                                                       │
│  [VPC]       [Subnets]      [NAT GW]       [SG]            [IAM]                     │
│  Amazon VPC  Private        NAT Gateway    Security        AWS IAM                    │
│  Isolated &  Subnets        Outbound       Groups          Roles & Policies           │
│  Secure      (2 AZs)        Internet       Traffic         ECS Task Role,             │
│  Network     Compute        Access         Control         Scheduler Role,            │
│              Isolation      (no inbound)   (1433 to RDS)   Lambda Exec Role           │
│                                                                                       │
│  [Secrets Manager]          [S3 Block Public Access]       [TLS in Transit]           │
│  All credentials            No public buckets              HTTPS for all API calls    │
│  in Secrets Manager         BlockPublicAcls=true           TLS 1.2+ enforced          │
│  (never in config files)                                                              │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

---

### LEGEND — Bottom of page

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│  LEGEND                                                                               │
│                                                                                       │
│  ──────►  Invoice Posting Flow (PER_ITEM)                                            │
│  ══════►  Batch File Generation Flow (BATCH_FILE)                                    │
│  ─ ─ ─►  Feed Download Flow                                                         │
│  ·····►  Notifications / Alerts Flow                                                 │
│  ──▶──   Scheduled (Direct to SQS)                                                   │
│  ──▷──   Manual (Via API → HTTP 202 → Poll)                                          │
│                                                                                       │
│  [AWS Region]  |  [Multi-AZ Deployment]  |  [Scalable & Highly Available]            │
│                                                                                       │
│  EXECUTION MODES:                                                                    │
│  PER_ITEM    = 1 Lambda per item, 50 concurrent (API clients)                        │
│  BATCH_FILE  = 1 Lambda for all items (file generation clients)                      │
│  HYBRID      = Batch file + per-item API calls (file + API clients)                  │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

---

## Key Differences from Original Diagram

| Original Diagram | Updated Diagram |
|---|---|
| Single "invoice-post-queue" | Per-client SQS queues (dynamically provisioned) |
| One Step Functions flow (sequential states) | Choice state → 3 execution paths (A/B/C) |
| "Batch Processor" (vague) | Explicit: Item Processor (PER_ITEM) vs Batch File Processor (BATCH_FILE) |
| No execution mode concept | Three modes: PER_ITEM, BATCH_FILE, HYBRID |
| No ECS fallback shown | ECS PostWorker shown as fallback path (execution_target='ECS') |
| No RDS Proxy | RDS Proxy shown in shared services |
| "Config Loader → Work Item Fetcher → Batch Processor → Result Aggregator" | Prepare → Choice → [Path A/B/C] → Aggregate → Notify |
| No file output shown | File outputs shown: CSV, Pipe-delimited, Excel, H/L/S, TMK |
| Only Oracle Fusion + SAP + NetSuite | Oracle Fusion + Caliber + Sevita + File outputs (20+ formats) |
| Slack/Teams notifications | Removed (email via SNS only, as decided) |
| AWS CloudTrail shown | Removed (default account trail sufficient) |
| AWS KMS shown | Removed (not used — Secrets Manager handles credential security) |
| No per-client DLQ | Per-client DLQ with CloudWatch alarm |
| Cost Optimization section | Removed (not part of architecture diagram) |

---

## Color Scheme (same as original)

- **Dark blue header bar** — title
- **White/light gray** — section backgrounds
- **Orange icons** — AWS services (EventBridge, SQS, Lambda, etc.)
- **Blue icons** — .NET components (API, Workflow UI)
- **Green arrows** — scheduled flow
- **Blue arrows** — manual flow
- **Red arrows** — error/DLQ flow
- **Purple section** — security & network foundation
- **Gray section** — cost optimization

---

## To Generate This Image

Use one of:
1. **draw.io (diagrams.net)** — free, use AWS icon library
2. **Lucidchart** — has AWS architecture templates
3. **Figma** — with AWS icon pack plugin
4. **AWS Architecture Icons** — download from https://aws.amazon.com/architecture/icons/
5. **Mermaid.js** — for a simpler version in code

The layout follows the same grid as the original image: top trigger bar, left posting flow, right feed flow, far-right shared services, bottom security + cost bars.
