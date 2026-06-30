# IPS AutoPost Platform — Actual Architecture Diagram

## System: Generic-Solution (IPS.AutoPost.Platform)
## Runtime: .NET 10 | AWS ECS Fargate | SQL Server RDS

---

```
╔══════════════════════════════════════════════════════════════════════════════════╗
║                    IPS AUTOPOST PLATFORM — AWS ARCHITECTURE                      ║
║              Multi-Client | Scalable | Secure | Observable                       ║
╚══════════════════════════════════════════════════════════════════════════════════╝


┌─────────────┐         ┌──────────────────┐              ┌───────────────────┐
│  TRIGGERS   │         │   WORKFLOW UI    │              │ ALERTS & ALARMS   │
├─────────────┤         ├──────────────────┤              ├───────────────────┤
│ EventBridge │         │• View Jobs       │              │ Amazon SNS        │
│ Scheduler   │         │• Trigger Manual  │              │   ↓               │
│ (Scheduled) │         │  Post            │              │ Email Alerts      │
│             │         │• View History    │              │                   │
│ ASP.NET     │         │• View Logs       │              │ CloudWatch Alarms │
│ Core API    │         │• Check Status    │              │ • DLQ depth > 0   │
│ (Manual)    │         │                  │              │ • PostFailed > 5  │
└──────┬──────┘         └────────┬─────────┘              │ • Task crash      │
       │                         │                        └───────────────────┘
       │                         │
       ▼                         ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│                    MANUAL POST FLOW (SYNCHRONOUS)                              │
│                                                                               │
│  1. User clicks "Post Now" or selects items in Workflow UI                    │
│  2. HTTP POST to ASP.NET Core API: /api/post/{jobId}/items/{itemIds}          │
│  3. API validates x-api-key header (from Secrets Manager)                     │
│  4. AutoPostOrchestrator processes invoices synchronously                     │
│  5. Full PostBatchResult returned in HTTP response (success/fail per item)    │
│                                                                               │
│  Response time: 2-30 seconds (depends on batch size)                          │
│  No queue involved. No async. Immediate result.                               │
└──────────────────────────────────────────────────────────────────────────────┘


═══════════════════════════════════════════════════════════════════════════════════
║  1. INVOICE POSTING FLOW (ECS Fargate — No Step Functions, No Lambda)          ║
═══════════════════════════════════════════════════════════════════════════════════

┌───────────────────┐        ┌────────────────────────────────────────────┐
│  SHARED POST      │        │  ECS FARGATE — PostWorker                  │
│  QUEUE            │        │  (BackgroundService, .NET 10)              │
│                   │        │                                            │
│  ips-post-queue   │───────→│  Polls SQS (MaxNumberOfMessages=10)       │
│  (ALL clients)    │        │  Creates DI scope per message              │
│                   │        │  Sets CorrelationId from MessageId         │
│  Message format:  │        │  Sends ExecutePostCommand via MediatR      │
│  {                │        │                                            │
│   "JobId": 1001,  │        │  MediatR Pipeline:                        │
│   "ClientType":   │        │    LoggingBehavior (logs start/end)       │
│     "INVITEDCLUB",│        │    ValidationBehavior (FluentValidation)   │
│   "Pipeline":     │        │    ExecutePostHandler                      │
│     "Post",       │        │      └→ AutoPostOrchestrator              │
│   "TriggerType":  │        │           ├→ Load config from DB          │
│     "Scheduled"   │        │           ├→ Check schedule window        │
│  }                │        │           ├→ OnBeforePostAsync (plugin)    │
│                   │        │           ├→ Fetch workitems              │
│  DLQ:             │        │           ├→ ExecutePostAsync (plugin)    │
│  ips-post-dlq     │        │           ├→ Write execution history      │
│  (after 3 fails)  │        │           └→ Publish CloudWatch metrics   │
└───────────────────┘        └────────────────────────────────────────────┘
                                              │
                                              ▼
                             ┌─────────────────────────────────┐
                             │  ERP / External APIs             │
                             │                                  │
                             │  • Oracle Fusion (InvitedClub)   │
                             │    - POST Invoice (HTTP 201)     │
                             │    - POST Attachment (HTTP 201)  │
                             │    - POST CalculateTax (HTTP 200)│
                             │                                  │
                             │  • Sevita AP System              │
                             │    - OAuth2 token request        │
                             │    - POST Invoice (HTTP 201)     │
                             └─────────────────────────────────┘


┌────────────────────────────────────────────────────────────────┐
│  Plugin Architecture                                            │
│                                                                 │
│  IClientPlugin interface:                                       │
│    • ExecutePostAsync()        — post invoices                  │
│    • ExecuteFeedDownloadAsync() — download feed data            │
│    • OnBeforePostAsync()       — batch pre-loading              │
│    • ClearPostInProcessAsync() — clear processing flag          │
│                                                                 │
│  Registered Plugins:                                            │
│    ┌──────────────────────────────────────┐                     │
│    │ InvitedClubPlugin (client_type=      │                     │
│    │   "INVITEDCLUB")                     │                     │
│    │   • 3-step Oracle Fusion posting     │                     │
│    │   • Supplier/COA feed download       │                     │
│    │   • Image retry service              │                     │
│    └──────────────────────────────────────┘                     │
│    ┌──────────────────────────────────────┐                     │
│    │ SevitaPlugin (client_type="SEVITA")  │                     │
│    │   • OAuth2 token caching             │                     │
│    │   • PO/Non-PO validation             │                     │
│    │   • Line item grouping               │                     │
│    └──────────────────────────────────────┘                     │
│                                                                 │
│  Adding a new client:                                           │
│    1. Write one plugin class                                    │
│    2. Register in PluginRegistration.cs                         │
│    3. INSERT row into generic_job_configuration                 │
│    → No core engine changes. No new deployments.                │
└────────────────────────────────────────────────────────────────┘


═══════════════════════════════════════════════════════════════════════════════════
║  2. FEED DOWNLOAD / LONG RUNNING FLOW (ECS Fargate)                            ║
═══════════════════════════════════════════════════════════════════════════════════

┌───────────────────┐        ┌────────────────────────────────────────────┐
│  SHARED FEED      │        │  ECS FARGATE — FeedWorker                  │
│  QUEUE            │        │  (BackgroundService, .NET 10)              │
│                   │        │                                            │
│  ips-feed-queue   │───────→│  Same pattern as PostWorker:               │
│  (ALL clients)    │        │  • SQS long polling (20s)                  │
│                   │        │  • MaxNumberOfMessages=10                  │
│  DLQ:             │        │  • DI scope per message                    │
│  ips-feed-dlq     │        │  • MediatR ExecuteFeedCommand              │
│                   │        │  • No timeout (ECS = unlimited runtime)    │
└───────────────────┘        └─────────────────────┬──────────────────────┘
                                                    │
                                                    ▼
                             ┌──────────────────────────────────────────────┐
                             │  External Feed Sources                        │
                             │                                               │
                             │  • Oracle Fusion REST API (Suppliers, COA)    │
                             │  • Future: SOAP APIs, FTP/SFTP, S3 sources   │
                             └──────────────────────────────────────────────┘
                                                    │
                                                    ▼
                             ┌──────────────────────────────────────────────┐
                             │  S3 Feed Archive                              │
                             │  ips-feed-archive/{client_type}/{date}/       │
                             └──────────────────────────────────────────────┘


═══════════════════════════════════════════════════════════════════════════════════
║  3. SCHEDULER LAMBDA                                                           ║
═══════════════════════════════════════════════════════════════════════════════════

┌────────────────────────────────────────────────────────────────┐
│  IPS.AutoPost.Scheduler (AWS Lambda, .NET 10)                   │
│                                                                 │
│  Triggered: EventBridge rate(10 minutes)                        │
│                                                                 │
│  Reads: generic_execution_schedule JOIN generic_job_config      │
│                                                                 │
│  For each active schedule:                                      │
│    • Create EventBridge rule if not exists                      │
│    • Update rule if cron_expression changed                     │
│    • Disable rule if is_active=0                                │
│                                                                 │
│  Target mapping:                                                │
│    schedule_type='POST'     → ips-post-queue                    │
│    schedule_type='DOWNLOAD' → ips-feed-queue                    │
│                                                                 │
│  Never touches invoices. Never calls ERP APIs.                  │
└────────────────────────────────────────────────────────────────┘


═══════════════════════════════════════════════════════════════════════════════════
║  SHARED DATA & SERVICES LAYER                                                  ║
═══════════════════════════════════════════════════════════════════════════════════

┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐
│ Amazon RDS       │  │ Amazon S3        │  │ AWS Secrets Mgr  │
│ SQL Server       │  │                  │  │                  │
│                  │  │ • Invoice images │  │ • DB credentials │
│ • Workflow DB    │  │   (ips-invoice-  │  │ • API credentials│
│   (existing)     │  │    images/)      │  │   (per client)   │
│ • 10 new generic │  │ • Feed archives  │  │ • SMTP password  │
│   tables (EF     │  │   (ips-feed-     │  │ • Encrypted KMS  │
│   Core managed)  │  │    archive/)     │  │                  │
│ • Work Items     │  │ • Output files   │  │ Path pattern:    │
│ • Post History   │  │   (ips-output-   │  │ /IPS/{client}/   │
│ • Execution Hist │  │    files/)       │  │   {env}/{purpose}│
│                  │  │                  │  │                  │
│ Max Pool: 2000   │  │ Lifecycle: 90d   │  │ Runtime fetch    │
└──────────────────┘  └──────────────────┘  └──────────────────┘

┌──────────────────┐  ┌──────────────────┐
│ Amazon SQS       │  │ Amazon ECR       │
│                  │  │                  │
│ • ips-post-queue │  │ • ecr-ips-       │
│   (shared)       │  │   autopost-{env} │
│ • ips-feed-queue │  │ • ScanOnPush     │
│   (shared)       │  │ • Lifecycle:     │
│ • ips-post-dlq   │  │   untagged 7d    │
│ • ips-feed-dlq   │  │                  │
│                  │  │                  │
│ VisibilityTimeout│  │                  │
│   = 7200s (2hr)  │  │                  │
│ Retention: 14d   │  │                  │
│ maxReceive: 3    │  │                  │
└──────────────────┘  └──────────────────┘


═══════════════════════════════════════════════════════════════════════════════════
║  OBSERVABILITY & MONITORING                                                    ║
═══════════════════════════════════════════════════════════════════════════════════

┌────────────────────────────────────────────────────────────────┐
│  Amazon CloudWatch                                              │
│                                                                 │
│  Logs:                                                         │
│    /ips/autopost/feed/{env}                                    │
│    /ips/autopost/post/{env}                                    │
│    Retention: 90 days                                          │
│    Format: [{Timestamp}] [{CorrelationId}] [{ClientType}]      │
│            [{JobId}] {Message}                                  │
│                                                                 │
│  Custom Metrics (namespace: IPS/AutoPost/{env}):               │
│    Dimensions: ClientType + JobId                              │
│    PostStarted, PostCompleted, PostFailed                      │
│    PostSuccessCount, PostFailedCount, PostDurationSeconds       │
│    FeedStarted, FeedCompleted, FeedRecordsDownloaded           │
│    FeedDurationSeconds                                         │
│    ImageRetryAttempted, ImageRetrySucceeded                    │
│                                                                 │
│  Alarms → SNS → Email:                                         │
│    • DLQ messages > 0                                          │
│    • PostFailedCount > 5 in 5 minutes                          │
│    • ECS task exit code ≠ 0                                    │
└────────────────────────────────────────────────────────────────┘


═══════════════════════════════════════════════════════════════════════════════════
║  SECURITY & NETWORK FOUNDATION                                                 ║
═══════════════════════════════════════════════════════════════════════════════════

┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐
│ Amazon   │ │ Private  │ │ NAT      │ │ Security │ │ IAM      │ │ TLS 1.2+ │
│ VPC      │ │ Subnets  │ │ Gateway  │ │ Groups   │ │ Roles    │ │ AES-256  │
│ 10.10.   │ │ Multi-AZ │ │ Outbound │ │ Least    │ │ Least    │ │ All data │
│ 0.0/16   │ │ (2 AZs)  │ │ Internet │ │ Privilege│ │ Privilege│ │ encrypted│
└──────────┘ └──────────┘ └──────────┘ └──────────┘ └──────────┘ └──────────┘


═══════════════════════════════════════════════════════════════════════════════════
║  INFRASTRUCTURE AS CODE & CI/CD                                                ║
═══════════════════════════════════════════════════════════════════════════════════

┌────────────────────────────────────────────────────────────────┐
│  CloudFormation Stacks:                                         │
│    • infrastructure.yaml (VPC, ECS Cluster, SQS, ECR, S3)      │
│    • application.yaml (Task Definitions, Services, Scaling)    │
│    • monitoring.yaml (Alarms, Dashboards, SNS)                 │
│                                                                 │
│  Docker:                                                        │
│    • Dockerfile.FeedWorker (multi-stage, .NET 10)              │
│    • Dockerfile.PostWorker (multi-stage, .NET 10)              │
│                                                                 │
│  CI/CD:                                                         │
│    • .github/workflows/deploy.yml                              │
│    • Build → Test → Docker → ECR → ECS Deploy                  │
│                                                                 │
│  ECS Scaling:                                                   │
│    • MinCapacity: 1 (always-on, no cold start)                 │
│    • MaxCapacity: 5                                             │
│    • Scale out: SQS depth > 10 messages                        │
│    • Scale in: SQS depth = 0 for 10 minutes                   │
└────────────────────────────────────────────────────────────────┘


═══════════════════════════════════════════════════════════════════════════════════
║  EXECUTION FLOW (Bottom Summary)                                               ║
═══════════════════════════════════════════════════════════════════════════════════

  ┌──────────┐    ┌──────────────┐    ┌──────────────┐    ┌──────────────┐
  │1. TRIGGER│───→│2. SQS MESSAGE│───→│3. PROCESSING │───→│4. WRITE      │
  │          │    │              │    │              │    │   RESULTS    │
  │EventBridge    │Message on    │    │ECS Fargate   │    │DB + CW      │
  │fires on  │    │ips-post-queue│    │PostWorker    │    │Metrics      │
  │schedule  │    │or            │    │processes via │    │              │
  │  OR      │    │ips-feed-queue│    │plugin        │    │Execution    │
  │API call  │    │              │    │              │    │History      │
  │(manual)  │    │              │    │              │    │              │
  └──────────┘    └──────────────┘    └──────────────┘    └──────────────┘
```

---

## Comparison Legend

```
✅ = Matches our system exactly
⚠️ = We have it but implemented differently
❌ = Not in our system (remove from diagram)
⭐ = Valid future enhancement (keep but mark as "planned")
```
