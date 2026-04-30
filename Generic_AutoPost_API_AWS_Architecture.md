# Generic AutoPost API Platform - AWS Cloud Architecture
### Deep-Dive Reference | Beginner-Friendly | .NET Core 10 | 40+ Clients

> **Based on**: Deep analysis of Generic_AutoPost_API_Implementation.md  
> **Date**: April 24, 2026  
> **Audience**: Developers, Architects, Beginners welcome

---

## Table of Contents

1. [What Is This Document?](#1-what-is-this-document)
2. [The Big Picture - Why AWS?](#2-the-big-picture---why-aws)
3. [Complete AWS Architecture Diagram](#3-complete-aws-architecture-diagram)
4. [Every AWS Service Used - Purpose, Why, Benefit](#4-every-aws-service-used---purpose-why-benefit)
5. [How Data Flows - Step by Step](#5-how-data-flows---step-by-step)
6. [Per-Client Service Mapping](#6-per-client-service-mapping)
7. [Security Architecture](#7-security-architecture)
8. [Observability - Logs, Metrics, Alerts](#8-observability---logs-metrics-alerts)
9. [Cost Architecture](#9-cost-architecture)
10. [Alternatives Considered](#10-alternatives-considered)
11. [Infrastructure as Code - CloudFormation](#11-infrastructure-as-code---cloudformation)
12. [Deployment Pipeline](#12-deployment-pipeline)
13. [Beginner Glossary](#13-beginner-glossary)

---

## 1. What Is This Document?

### Plain English First

Right now, the IPS system has **40+ separate Windows Services**, running on local windows server, each doing the same job: pick up invoices, post them to an external system, download reference data. Every new client means a new server, a new service, a new deployment.

This document describes the **AWS Cloud Architecture** for the new **Generic AutoPost API Platform** - one unified system that replaces all 40+ services. It explains:

- **What** each AWS service is
- **Why** we chose it
- **What benefit** it gives us
- **How** everything connects
- **What the alternatives were** and why we did not pick them

### Who Should Read This

| Reader | What You Will Get |
|---|---|
| **Beginner developer** | A clear map of what AWS services exist and why each one is here |
| **Mid-level developer** | Enough detail to implement and debug each layer |
| **Architect** | Full justification for every service choice with alternatives |
| **DevOps / Cloud engineer** | Deployment topology, IAM, VPC, scaling config |

---

## 2. The Big Picture - Why AWS?

### What We Had Before (The Problem)

```
BEFORE (Today):
+------------------+     +------------------+     +------------------+
|  Windows Server  |     |  Windows Server  |     |  Windows Server  |
|  Media_API_Lib   |     | InvitedClub_Lib  |     |  Caliber_Lib     |
|  Windows Service |     |  Windows Service |     |  Windows Service |
|  Always running  |     |  Always running  |     |  Always running  |
+------------------+     +------------------+     +------------------+
```

**Problems with the old approach:**
- No central logging - each service writes to its own log file on its own server
- No auto-scaling - if a job takes longer, it just runs longer
- No retry logic - if a post fails, someone has to manually re-run it
- Windows-only - tied to .NET Framework, cannot modernize

### What We Are Building (The Solution)

```
AFTER (New Platform):
+------------------------------------------------------------------+
|                    AWS Cloud                                      |
|                                                                   |
|  Lambda (runs only when triggered - $0 when idle)                |
|  ECS Fargate (runs only for long jobs - $0 when idle)            |
|  EventBridge (replaces Windows Service timer - $1/month)         |
|  SQS (replaces manual retry - built-in retry)                    |
|  RDS (existing database - unchanged)                             |
|  S3 (file storage - pennies per GB)                              |
|  Secrets Manager (replaces config files with credentials)        |
|  CloudWatch (central logging for ALL clients in one place)       |
+------------------------------------------------------------------+
  All 40+ clients handled by ONE platform
  New client = database rows + one plugin class
  Cost: pay only for what runs
```

### The Three Core Principles

1. **Pay per use** - Lambda and Fargate charge only when running. 15 idle Windows Services cost money 24/7. Lambda costs $0 when idle.
2. **One platform, many clients** - The same Lambda function handles Media, InvitedClub, Greenthal, and every future client. No new infrastructure per client.
3. **Managed services** - AWS manages the servers, OS patches, scaling, and availability. We write business logic, not infrastructure code.

---

## 3. Complete AWS Architecture Diagram

### 3.1 Full System Diagram

> Lambda is a schedule manager only.
> All actual work — feed downloads AND invoice posting for ALL 40+ clients — goes through
> ECS Fargate. This removes the 15-minute timeout risk entirely, handles 10,000+ invoices
> without chunking, and gives every client the same reliable execution path.

```
+===========================================================================+
|           IPS AUTOPOST PLATFORM - IMPROVED AWS ARCHITECTURE              |
|              .NET Core 10 | 40+ Clients | No Time Limit | Production      |
+===========================================================================+

+-------------------------------- LAYER 0: ADMIN --------------------------+
|                                                                           |
|  +-----------------------------------------------------------------------+|
|  | Admin UI / Config API  (.NET 10 Web API)                             ||
|  |                                                                       ||
|  |  Add new client | Change schedule | Enable/Disable job               ||
|  |  View execution history | Trigger manual post                        ||
|  +-------------------------------------+---------------------------------+|
|                                        |                                  |
|                                        v                                  |
|  +-----------------------------------------------------------------------+|
|  | Amazon RDS SQL Server  (Single source of truth for ALL config)       ||
|  |                                                                       ||
|  |  generic_job_configuration      generic_execution_schedule           ||
|  |  generic_feed_configuration     generic_auth_configuration           ||
|  |  generic_post_history           generic_execution_history            ||
|  |  Workitems (existing)           WFxxxIndexHeader (existing)          ||
|  +-----------------------------------------------------------------------+|
+===========================================================================+
                                        |
                                        v
+-------------------------------- LAYER 1: SCHEDULE SYNC ------------------+
|                                                                           |
|  +-----------------------------------------------------------------------+|
|  | Scheduler Lambda  (runs every 10 minutes via EventBridge rate rule)  ||
|  |                                                                       ||
|  |  1. Reads generic_execution_schedule from RDS                        ||
|  |  2. Compares with existing EventBridge rules                         ||
|  |  3. Creates rules for new clients                                    ||
|  |  4. Updates rules for changed schedules                              ||
|  |  5. Disables rules for inactive jobs                                 ||
|  |                                                                       ||
|  |  NEVER touches invoices. NEVER posts data. Schedule sync ONLY.       ||
|  |  Completes in < 5 seconds. Costs almost nothing.                     ||
|  +-------------------------------------+---------------------------------+|
|                                        |                                  |
|                                        v                                  |
|  +-----------------------------------------------------------------------+|
|  | Amazon EventBridge Scheduler  (one rule per job per type)            ||
|  |                                                                       ||
|  |  ips-feed-invitedclub   cron(0 3 * * ? *)   -> ips-feed-queue       ||
|  |  ips-post-invitedclub   rate(15 minutes)    -> ips-post-queue       ||
|  |  ips-feed-media         cron(0 7 * * ? *)   -> ips-feed-queue       ||
|  |  ips-post-media         cron(0 8 * * ? *)   -> ips-post-queue       ||
|  |  ips-post-akron         cron(0 9 * * ? *)   -> ips-post-queue       ||
|  |  ips-post-vantaca       rate(30 minutes)    -> ips-post-queue       ||
|  |  ips-post-greenthal     cron(0 8 * * ? *)   -> ips-post-queue       ||
|  |  ips-post-mds           cron(0 10 * * ? *)  -> ips-post-queue       ||
|  |  ... one rule per job per type for all 40+ clients ...              ||
|  |                                                                       ||
|  |  EventBridge NEVER calls Fargate directly.                           ||
|  |  It ONLY drops a message into SQS. That is its entire job.          ||
|  +-------------------------------------+---------------------------------+|
+===========================================================================+
                    |                                   |
                    v                                   v
+-------------------------------- LAYER 2: QUEUES -------------------------+
|                                                                           |
|  +----------------------------------+  +--------------------------------+ |
|  | SQS: ips-feed-queue              |  | SQS: ips-post-queue            | |
|  |                                  |  |                                | |
|  | Holds: feed download jobs        |  | Holds: invoice post jobs       | |
|  | Visibility timeout: 60 min       |  | Visibility timeout: 60 min     | |
|  | Message retention:  4 days       |  | Message retention:  4 days     | |
|  | Max receive count:  3            |  | Max receive count:  3          | |
|  | On 3 failures -> ips-feed-dlq    |  | On 3 failures -> ips-post-dlq  | |
|  |                                  |  |                                | |
|  | Example message:                 |  | Example message:               | |
|  | {                                |  | {                              | |
|  |   "JobId": 100,                  |  |   "JobId": 371,                | |
|  |   "ClientType": "MEDIA",         |  |   "ClientType": "INVITEDCLUB", | |
|  |   "Pipeline": "Feed",            |  |   "Pipeline": "Post",          | |
|  |   "TriggerType": "Scheduled"     |  |   "TriggerType": "Scheduled"   | |
|  | }                                |  | }                              | |
|  +----------------+-----------------+  +---------------+----------------+ |
|                   |                                    |                  |
+===========================================================================+
                    |                                    |
                    v                                    v
+-------------------------------- LAYER 3: COMPUTE ------------------------+
|                                                                           |
|  +----------------------------------+  +--------------------------------+ |
|  | ECS Fargate: FEED WORKER         |  | ECS Fargate: POST WORKER       | |
|  | IPS.AutoPost.Host.FeedWorker     |  | IPS.AutoPost.Host.PostWorker   | |
|  |                                  |  |                                | |
|  | Polls ips-feed-queue             |  | Polls ips-post-queue           | |
|  |                                  |  |                                | |
|  | For each message:                |  | For each message:              | |
|  |  1. Read job config from RDS     |  |  1. Read job config from RDS   | |
|  |  2. Get credentials (Secrets Mgr)|  |  2. Get credentials            | |
|  |  3. Resolve feed plugin          |  |  3. Resolve post plugin        | |
|  |  4. Download feed from ERP/FTP   |  |  4. Fetch ALL pending items    | |
|  |  5. Parse + transform data       |  |  5. Post to ERP (no time limit)| |
|  |  6. Bulk insert into RDS         |  |  6. Route each result in RDS   | |
|  |  7. Archive raw file to S3       |  |  7. Write generic_post_history | |
|  |  8. Write FeedExecutionHistory   |  |  8. Write ExecHistory + metrics| |
|  |                                  |  |                                | |
|  | NO time limit                    |  | NO time limit                  | |
|  | Handles ALL feed clients:        |  | Handles ALL post clients:      | |
|  |  Media SOAP (38 min) - fine      |  |  InvitedClub 10,000+ - fine    | |
|  |  InvitedClub REST - fine         |  |  MDS TIFF processing - fine    | |
|  |  Caliber REST - fine             |  |  Greenthal 20 types - fine     | |
|  |  FTP/SFTP feeds - fine           |  |  Vantaca, Akron, etc. - fine   | |
|  |                                  |  |                                | |
|  | Min tasks: 1 (always running)    |  | Min tasks: 1 (always running)  | |
|  | Max tasks: 5                     |  | Max tasks: 10                  | |
|  | Scale trigger: queue depth > 0   |  | Scale trigger: queue depth > 0 | |
|  +----------------------------------+  +--------------------------------+ |
|                                                                           |
|  Both workers use the SAME assemblies:                                    |
|  +-- IPS.AutoPost.Core.dll      (generic engine - AutoPostOrchestrator)  |
|  +-- IPS.AutoPost.Plugins.dll   (all 40+ client plugins)                 |
|                                                                           |
+===========================================================================+
                                        |
                                        v
+-------------------------------- LAYER 4: DATA ---------------------------+
|                                                                           |
|  +------------------------+  +------------------+  +------------------+  |
|  | Amazon RDS SQL Server  |  | Amazon S3        |  | AWS Secrets Mgr  |  |
|  |                        |  |                  |  |                  |  |
|  | Existing tables:       |  | ips-invoice-     |  | /IPS/Common/DB   |  |
|  |  Workitems             |  | images/          |  | /IPS/{client}/   |  |
|  |  WFxxxIndexHeader      |  | (PDFs + TIFFs)   |  |   PostAuth       |  |
|  |  InvitedClubSupplier   |  |                  |  | /IPS/{client}/   |  |
|  |  etc. (all unchanged)  |  | ips-feed-        |  |   FeedAuth       |  |
|  |                        |  | archive/         |  |                  |  |
|  | New generic tables:    |  | (raw feed files) |  | Auto-rotates     |  |
|  |  generic_job_config    |  |                  |  | credentials      |  |
|  |  generic_exec_schedule |  | ips-output-      |  |                  |  |
|  |  generic_post_history  |  | files/           |  |                  |  |
|  |  generic_feed_config   |  | (Greenthal CSV,  |  |                  |  |
|  |  generic_auth_config   |  |  MDS ZIP output) |  |                  |  |
|  |  generic_field_mapping |  |                  |  |                  |  |
|  +------------------------+  +------------------+  +------------------+  |
|                                                                           |
+===========================================================================+
                                        |
                                        v
+-------------------------------- LAYER 5: MANUAL TRIGGER -----------------+
|                                                                           |
|  +-----------------------------------------------------------------------+|
|  | API Gateway + .NET Web API  (manual post trigger from Workflow UI)  ||
|  |                                                                       ||
|  |  POST /api/post/{jobId}             -> calls Post Worker DIRECTLY   ||
|  |  POST /api/post/{jobId}/items/{ids} -> calls Post Worker DIRECTLY   ||
|  |  POST /api/feed/{jobId}             -> calls Feed Worker DIRECTLY   ||
|  |  GET  /api/status/{executionId}     -> reads generic_exec_history   ||
|  |                                                                       ||
|  |  Manual trigger path is DIRECT                                       ||
|  |  API Gateway -> .NET Web API -> Fargate internal endpoint            ||
|  |  User gets an immediate response. No queue wait. No polling needed. ||
|  |                                                                       ||
|  |  Scheduled jobs  -> EventBridge -> SQS -> Fargate (async)           ||
|  |  Manual jobs     -> API Gateway -> Web API -> Fargate (sync/direct) ||
|  +-----------------------------------------------------------------------+|
+===========================================================================+
                                        |
                                        v
+-------------------------------- LAYER 6: OBSERVABILITY ------------------+
|                                                                           |
|  +------------------+  +-------------------+  +----------------------+   |
|  | CloudWatch Logs  |  | CloudWatch Metrics |  | Amazon SNS           |   |
|  |                  |  |                    |  |                      |   |
|  | /ips/feed/{client}  | FeedSuccessCount   |  | ips-alerts-critical  |   |
|  | /ips/post/{client}  | FeedFailedCount    |  |  -> email + Slack    |   |
|  | /ips/scheduler   |  | PostSuccessCount   |  |                      |   |
|  |                  |  | PostFailedCount    |  | ips-alerts-warning   |   |
|  | 90 day retention |  | PostDurationSec    |  |  -> email            |   |
|  |                  |  | QueueDepth         |  |                      |   |
|  |                  |  |                    |  | Alarms:              |   |
|  |                  |  |                    |  | PostFailed > 10      |   |
|  |                  |  |                    |  | DLQ msg count > 0    |   |
|  |                  |  |                    |  | Fargate task stopped |   |
|  +------------------+  +-------------------+  +----------------------+   |
|                                                                           |
|  +------------------+  +-------------------------------------------+    |
|  | AWS X-Ray        |  | CloudWatch Dashboard                       |    |
|  | Traces each job  |  | "IPS AutoPost Operations"                  |    |
|  | end-to-end:      |  |  - Jobs run today (all clients)            |    |
|  | RDS query time + |  |  - Success/fail rate per client            |    |
|  | ERP API time +   |  |  - Average post duration per client        |    |
|  | S3 read time     |  |  - Queue depth (backlog indicator)         |    |
|  +------------------+  +-------------------------------------------+    |
|                                                                           |
+===========================================================================+
```

---

### 3.1.1 How Each Layer Connects — The Complete Flow

```
ADDING A NEW CLIENT (e.g. Akron at 2 PM today):

  Admin UI saves Akron config to RDS
       |
       | (up to 10 min wait)
       v
  Scheduler Lambda wakes up, reads RDS, sees new Akron job
  Creates EventBridge rule: ips-post-akron -> cron(0 9 * * ? *)
  Target: ips-post-queue
       |
  Next day 9:00 AM
       v
  EventBridge fires -> drops message in ips-post-queue
       |
       v
  Post Worker (already running) picks up message within seconds
  Processes all Akron invoices -> done
```

```
SCHEDULED POST (InvitedClub 10,000 invoices at 8 AM):

  8:00 AM  EventBridge ips-post-invitedclub fires
           Drops in ips-post-queue: {"JobId":371,"ClientType":"INVITEDCLUB"}
                |
                v
  Post Worker picks up (already running, no cold start wait)
  Reads config from RDS
  Gets Oracle Fusion credentials from Secrets Manager
  Fetches ALL 10,000 pending workitems from Workitems table
           |
           | For each of 10,000 invoices (no time limit):
           |   Get PDF from S3
           |   POST invoice to Oracle Fusion  (Step 1)
           |   POST attachment                (Step 2)
           |   POST calculate tax             (Step 3)
           |   WORKITEM_ROUTE -> success/fail queue in RDS
           v
  10:45 AM  All 10,000 done
  Writes generic_post_history (10,000 rows)
  Writes generic_execution_history: 9,987 success / 13 failed / 105 min
  CloudWatch metrics published
  If failed > 10: SNS alert -> email to team
```

```
SCHEDULED FEED DOWNLOAD (Media at 7 AM — SOAP, 6 months of data):

  7:00 AM  EventBridge ips-feed-media fires
           Drops in ips-feed-queue: {"JobId":100,"ClientType":"MEDIA"}
                |
                v
  Feed Worker picks up message
  Resolves MediaPlugin (SOAP feed strategy)
  Calls Advantage SOAP API in 10-day chunks going back 6 months
  Downloads: Vendor, MediaOrder, GL, PO, Jobs feeds
  Bulk inserts all data into RDS feed tables
  Archives raw files to S3 ips-feed-archive/media/
           |
  7:38 AM  Done. Took 38 minutes. No timeout. No problem.
  Writes FeedExecutionHistory: success / 38 min / 24,500 records
```

```
MANUAL POST from Workflow UI (someone clicks "Post Now"):

  User clicks Post Now for InvitedClub invoice #4521
  Workflow UI -> API Gateway POST /api/post/371/items/4521
           |
           v
  API Gateway validates API key
           |
           v
  .NET Web API (PostController) receives the request
  Calls Post Worker Fargate service DIRECTLY via internal HTTP endpoint
  (NOT through SQS — this is a direct synchronous call)
           |
           v
  Post Worker receives the request immediately
  Reads config from RDS for JobId 371
  Gets Oracle Fusion credentials from Secrets Manager
  Processes ONLY invoice 4521 (ItemIds filter applied)
  POST invoice to Oracle Fusion  (Step 1)
  POST attachment                (Step 2)
  POST calculate tax             (Step 3)
  WORKITEM_ROUTE -> success/fail queue in RDS
  Writes generic_post_history
           |
           v
  Done in ~8 seconds
  Web API returns result immediately to Workflow UI:
  {"ExecutionId":9876, "Status":"Success", "ItemId":4521, "Duration":"8s"}
  User sees the result instantly. No polling needed.

  KEY DIFFERENCE vs scheduled jobs:
  Scheduled -> EventBridge -> SQS -> Fargate (async, user does not wait)
  Manual    -> API Gateway -> Web API -> Fargate direct (sync, user sees result now)
```

```
FAILURE AND RETRY (Post Worker crashes mid-job):

  Post Worker processing InvitedClub 10,000 invoices
  At invoice 6,432 -> Fargate task crashes (any reason)
           |
           | SQS visibility timeout expires (60 min)
           v
  Message becomes visible again in ips-post-queue
  ECS auto-starts a new Post Worker task
  New task picks up the same message
  Queries RDS: WHERE StatusId IN (100) AND PostInProcess = 0
  Invoices 1-6,431 already routed to success queue -> NOT returned
  Only remaining ~3,569 invoices returned
  Processing resumes naturally from where it left off
           |
  After 3 total failures on same message:
           v
  Message moves to ips-post-dlq (Dead Letter Queue)
  SNS alarm fires -> email alert to team
  Team investigates only those specific invoices
  Other 9,431 invoices completely unaffected
```

---

### 3.1.2 ECS Fargate Cold Start — The 30-60 Second Problem and How We Solve It

> **The Problem**
>
> When ECS Fargate scales from 0 tasks to 1 task (because a new message arrived in SQS),
> it takes 30-60 seconds to:
> - Allocate compute resources
> - Pull the Docker image from ECR
> - Start the .NET application
> - Begin polling SQS
>
> For a scheduled job that runs at 8 AM, a 60-second delay is acceptable.
> For a manual "Post Now" click from the Workflow UI, a 60-second wait before
> anything happens feels broken to the user.

> **The Solution: Always Keep Minimum 1 Task Running**
>
> Both the Feed Worker and Post Worker are configured with `minimumHealthyPercent = 1`
> and `desiredCount = 1`. This means ECS always keeps at least 1 task running,
> even when the SQS queue is empty.
>
> The always-on task costs ~$15-20/month per worker (2 workers = ~$35/month).
> This is the price of instant responsiveness. Worth it.

```
WITHOUT minimum task (scale to zero):

  8:00:00 AM  EventBridge fires -> message in SQS
  8:00:00 AM  ECS detects message, starts scaling up
  8:00:30 AM  Fargate allocates resources
  8:00:45 AM  Docker image pulled from ECR
  8:01:00 AM  .NET app starts, DI container built
  8:01:05 AM  Worker begins polling SQS
  8:01:05 AM  Message picked up -> processing starts
  DELAY: ~65 seconds before first invoice is touched

WITH minimum task (always 1 running):

  8:00:00 AM  EventBridge fires -> message in SQS
  8:00:02 AM  Already-running Post Worker picks up message
  8:00:02 AM  Processing starts immediately
  DELAY: ~2 seconds
```

> **ECS Service Configuration for Always-On:**
>
> ```json
> {
>   "serviceName": "ips-post-worker",
>   "desiredCount": 1,
>   "deploymentConfiguration": {
>     "minimumHealthyPercent": 100,
>     "maximumPercent": 200
>   },
>   "capacityProviderStrategy": [
>     {
>       "capacityProvider": "FARGATE",
>       "weight": 1,
>       "base": 1
>     }
>   ]
> }
> ```
>
> **Auto Scaling on top of the always-on task:**
> When multiple clients have jobs queued at the same time (e.g. 5 clients all
> fire at 8 AM), ECS scales UP from 1 task to N tasks automatically based on
> SQS queue depth. When the queue drains, it scales back DOWN to 1 (not 0).
>
> ```
> Queue depth = 0  -> 1 task running  (always-on, ready to go)
> Queue depth = 1  -> 1 task running  (the always-on task handles it)
> Queue depth = 3  -> 3 tasks running (scaled up, one per message)
> Queue depth = 0  -> scales back to 1 (never goes to 0)
> ```
>
> **Cost breakdown:**
> - 1 always-on Fargate task (0.5 vCPU, 1GB): ~$15/month
> - 2 workers (Feed + Post): ~$30/month total
> - Scale-up tasks: charged only for the minutes they run
> - Total always-on cost: ~$30/month for instant responsiveness across all 40+ clients
### 3.2 Network / VPC Diagram

```
+====================== AWS VPC (10.0.0.0/16) ==========================+
|                                                                        |
|  +-- Private Subnet A (10.0.1.0/24) -- AZ us-east-1a ---------------+|
|  |  Lambda ENI (when inside VPC)                                      ||
|  |  ECS Fargate Tasks                                                 ||
|  +--------------------------------------------------------------------+|
|                                                                        |
|  +-- Private Subnet B (10.0.2.0/24) -- AZ us-east-1b ---------------+|
|  |  Lambda ENI (multi-AZ failover)                                    ||
|  |  ECS Fargate Tasks (multi-AZ)                                      ||
|  +--------------------------------------------------------------------+|
|                                                                        |
|  +-- Private Subnet C (10.0.3.0/24) -- AZ us-east-1c ---------------+|
|  |  Amazon RDS SQL Server (Multi-AZ standby)                          ||
|  +--------------------------------------------------------------------+|
|                                                                        |
|  +-- NAT Gateway (for outbound internet - posting to external APIs) -+|
|  |  Lambda/Fargate -> NAT GW -> Internet Gateway -> External ERP APIs ||
|  +--------------------------------------------------------------------+|
|                                                                        |
|  +-- VPC Endpoints (private AWS service access, no internet needed) -+|
|  |  com.amazonaws.us-east-1.s3          (S3 access)                  ||
|  |  com.amazonaws.us-east-1.secretsmanager (Secrets Manager)         ||
|  |  com.amazonaws.us-east-1.sqs         (SQS access)                 ||
|  |  com.amazonaws.us-east-1.logs        (CloudWatch Logs)            ||
|  +--------------------------------------------------------------------+|
|                                                                        |
+========================================================================+
```

---

## 4. Every AWS Service Used - Purpose, Why, Benefit

This section explains every single AWS service in the architecture. Each one is explained from scratch so a beginner can understand it.

---

### 4.1 Amazon EventBridge Scheduler

#### What Is It? (Beginner Explanation)
Think of EventBridge Scheduler as a **cloud alarm clock**. You tell it "run this job every day at 8 AM" and it fires a trigger at exactly that time. It replaces the Windows Service timer that was running on local windows server.

#### What It Does in Our System
- Holds one schedule rule per job in `generic_job_configuration`
- Fires at the configured cron time (e.g. `cron(0 8 * * ? *)` = 8 AM daily)
- Sends a JSON message to Lambda or SQS when it fires
- Supports both cron expressions and rate expressions (`rate(30 minutes)`)

#### Why We Use It (Not a Simple Timer)
| Option | Problem |
|---|---|
| Windows Service timer | Requires an windows server running 24/7 just to check the time |
| Windows server -Task sceduler cron job | manual setup per client |
| EventBridge Scheduler | Fully managed, $1/million invocations, zero servers |

#### Benefit
- **Zero infrastructure** - no server needed just to trigger a job
- **Per-job schedules** - each of the 40+ clients can have its own schedule
- **Timezone support** - schedule in America/New_York, not UTC
- **Automatic retry** - if Lambda is throttled, EventBridge retries automatically
- **Cost**: ~$0.01/month for 40+ jobs running twice daily

#### Configuration Example
```json
{
  "ScheduleName": "ips-autopost-invitedclub-job371",
  "ScheduleExpression": "cron(0 8 * * ? *)",
  "ScheduleExpressionTimezone": "America/New_York",
  "Target": {
    "Arn": "arn:aws:lambda:us-east-1:123456789:function:ips-autopost",
    "Input": "{\"JobId\": 371, \"ClientType\": \"INVITEDCLUB\", \"TriggerType\": \"Scheduled\"}"
  }
}
```

---

### 4.2 AWS Lambda

#### What Is It? (Beginner Explanation)
Lambda is **code that runs without a server**. You upload your .NET application, and AWS runs it only when triggered. When nothing is happening, it costs $0. When triggered, AWS starts it in ~1-2 seconds, runs it, and shuts it down. You pay only for the milliseconds it runs.

#### What It Does in Our System
- Hosts `IPS.AutoPost.Host.Lambda` - the main entry point for all short-running jobs
- Receives the trigger from EventBridge (scheduled) or API Gateway (manual)
- Loads the `AutoPostOrchestrator`, resolves the correct client plugin, runs the post
- Handles all clients whose jobs complete in under 15 minutes

#### Why We Use It (Not Windows local server)
| Option | Problem |
|---|---|
| ECS always-on | Still costs money when idle |
| Lambda | $0 when idle, ~$0.20 per million invocations |

#### Benefit
- **Cost**: 40+ jobs running 2x/day for 2 minutes each = ~$0.50/month total
- **No server management** - no OS patches, no capacity planning
- **Auto-scales** - if 40 jobs trigger at the same time, AWS runs 40 Lambda instances in parallel automatically
- **Built-in retry** - Lambda retries on failure automatically
- **Deployment** - deploy a new version in seconds with zero downtime

#### Lambda Configuration for Our Platform
```
Function name:    ips-autopost-platform
Runtime:          .NET 8 (Lambda supports .NET 8; upgrade to .NET 10 when available)
Memory:           1024 MB (configurable per job via environment variable)
Timeout:          15 minutes (maximum Lambda allows)
Architecture:     x86_64
VPC:              Yes (needs access to RDS in private subnet)
Concurrency:      Reserved = 20 (prevents runaway parallel executions)
```

#### When Lambda Is NOT Used
Lambda has a hard 15-minute timeout. These jobs use ECS Fargate instead:
- Media MediaOrders feed download (can take 20-40 minutes for months of SOAP data)
- MDS TIFF image processing (GDI+ image manipulation on large batches)

---

### 4.3 Amazon ECS Fargate

#### What Is It? (Beginner Explanation)
ECS Fargate is **containers without managing servers**. You package your application in a Docker container, tell AWS how much CPU and memory it needs, and AWS runs it. Unlike Lambda, there is no 15-minute limit. Unlike Windows local server, you do not manage the underlying server.

#### What It Does in Our System
- Hosts `IPS.AutoPost.Host.Worker` - the .NET Worker Service for long-running jobs
- Runs as a background service that polls SQS for long-job messages
- Handles Media feed downloads and MDS TIFF processing
- Scales from 0 tasks to N tasks based on SQS queue depth

#### Why We Use It (Not Lambda for Long Jobs)
| Option | Problem |
|---|---|
| Lambda | Hard 15-minute timeout - Media feed can take 30-40 minutes |
| Windows local server always-on | Costs money 24/7 even when Media only runs once a day |
| ECS Fargate | Scales to 0 when idle ($0), scales up when needed |

#### Benefit
- **No timeout limit** - can run for hours if needed
- **Scale to zero** - when no long jobs are queued, 0 Fargate tasks run = $0 cost
- **Same code** - uses the same `IPS.AutoPost.Core` and `IPS.AutoPost.Plugins` assemblies as Lambda
- **GDI+ support** - MDS TIFF processing uses GDI+ which needs a full OS environment (Lambda has restrictions)

#### ECS Configuration
```
Cluster:          ips-autopost-cluster
Service:          ips-autopost-worker
Task Definition:
  CPU:            1024 (1 vCPU)
  Memory:         4096 MB (4 GB - needed for TIFF processing)
  Container:      ips-autopost-worker:latest
  Image:          ECR repository
Auto Scaling:
  Min tasks:      0
  Max tasks:      5
  Scale-out:      SQS ApproximateNumberOfMessages > 0
  Scale-in:       SQS ApproximateNumberOfMessages = 0 for 5 minutes
```

---

### 4.4 Amazon API Gateway

#### What Is It? (Beginner Explanation)
API Gateway is a **front door for your application on the internet**. It receives HTTP requests (like clicking "Post Now" in the Workflow UI) and routes them to Lambda. It handles authentication, rate limiting, and HTTPS automatically.

#### What It Does in Our System
- Exposes `POST /api/post/{jobId}` - manual post trigger from Workflow UI
- Exposes `POST /api/feed/{jobId}` - manual feed refresh
- Exposes `POST /api/post/{jobId}/items/{itemIds}` - post specific work items
- Validates the API key before passing the request to Lambda
- Returns the execution result back to the caller

#### Why We Use It
- The Workflow UI needs a way to trigger posts manually (not just on schedule)
- API Gateway provides HTTPS, authentication, and rate limiting out of the box
- No server needed - it is fully managed

#### Benefit
- **Secure** - API key or IAM authentication built in
- **Rate limiting** - prevents accidental flooding (e.g. 100 requests/second max)
- **HTTPS only** - all traffic encrypted in transit
- **Cost**: $3.50 per million API calls - manual triggers are rare, cost is negligible

---

### 4.5 Amazon SQS (Simple Queue Service)

#### What Is It? (Beginner Explanation)
SQS is a **message queue** - a holding area for tasks that need to be processed. Think of it like a to-do list in the cloud. One service puts a message on the list ("process this job"), another service picks it up and does the work. If the work fails, the message goes back on the list automatically.

#### What It Does in Our System
Three queues are used:

| Queue | Purpose |
|---|---|
| `ips-long-job-queue` | Holds messages for ECS Fargate long-running jobs (Media, MDS) |
| `ips-retry-queue` | Holds failed image posts for InvitedClub retry logic |
| `ips-dlq` (Dead Letter Queue) | Holds messages that failed after max retries - for investigation |

#### Why We Use It
- **Decoupling** - EventBridge fires and puts a message in SQS. Fargate picks it up when ready. They do not need to be running at the same time.
- **Automatic retry** - if Fargate crashes mid-job, the message becomes visible again and gets retried
- **Dead letter queue** - after 3 failed attempts, the message moves to DLQ so we can investigate without losing it

#### Benefit
- **Reliability** - messages are never lost, even if the consumer crashes
- **Backpressure** - if Fargate is busy, messages wait in queue instead of being dropped
- **Visibility timeout** - message is hidden while being processed, reappears if processing fails
- **Cost**: $0.40 per million messages - essentially free for our volume

#### SQS Configuration
```
Queue:                  ips-long-job-queue
Visibility Timeout:     60 minutes (max job duration)
Message Retention:      4 days
Dead Letter Queue:      ips-dlq
Max Receive Count:      3 (move to DLQ after 3 failures)
```

---

### 4.6 Amazon RDS (Relational Database Service) - SQL Server

#### What Is It? (Beginner Explanation)
RDS is a **managed SQL Server database in the cloud**. AWS handles backups, patching, failover, and monitoring. You just connect to it like any SQL Server.

#### What It Does in Our System
- Hosts the existing Workflow database (Workitems, WFxxxIndexHeader, etc.) - **unchanged**
- Hosts the new generic configuration tables (generic_job_configuration, etc.)
- All 40+ client plugins read and write to this same database
- Multi-AZ deployment means automatic failover if one availability zone goes down

#### Why We Use It (Not Self-Managed SQL Server on Windows local server)
| Option | Problem |
|---|---|
| SQL Server on Windows local server | Manual backups, manual patching, manual failover setup |
| RDS SQL Server | Automated backups, automated patching, automatic Multi-AZ failover |

#### Benefit
- **Automated backups** - point-in-time recovery up to 35 days
- **Multi-AZ** - if the primary database fails, standby takes over in ~60 seconds automatically
- **No patching** - AWS patches the OS and SQL Server engine
- **Monitoring** - CPU, connections, storage all visible in CloudWatch automatically

---

### 4.7 Amazon S3 (Simple Storage Service)

#### What Is It? (Beginner Explanation)
S3 is **unlimited file storage in the cloud**. Think of it as a hard drive that never fills up, never fails, and costs pennies per GB. Files are stored in "buckets" (like folders).

#### What It Does in Our System

| Bucket | What Is Stored | Who Uses It |
|---|---|---|
| `ips-invoice-images` | Invoice PDFs and TIFF images (existing) | All clients that attach images |
| `ips-feed-archive` | Downloaded feed files (Vendor, Supplier, COA, etc.) | Media, InvitedClub, Caliber |
| `ips-output-files` | Generated CSV/Excel output files | Greenthal, MDS |

#### Why We Use It (Not Local File System)
- Lambda and Fargate are **stateless** - they have no persistent local disk
- S3 is accessible from Lambda, Fargate, and the Workflow UI simultaneously
- S3 has 99.999999999% (11 nines) durability - files are never lost

#### Benefit
- **Shared access** - Lambda, Fargate, and the UI all read from the same bucket
- **Lifecycle policies** - automatically delete old feed archives after 90 days
- **Versioning** - keep previous versions of output files for audit
- **Cost**: ~$0.023/GB/month - storing 100GB of feed archives costs $2.30/month

---

### 4.8 AWS Secrets Manager

#### What Is It? (Beginner Explanation)
Secrets Manager is a **secure vault for passwords and API keys**. Instead of storing database passwords in config files (which can be accidentally committed to Git), you store them in Secrets Manager and your code fetches them at runtime. The vault can also automatically rotate passwords on a schedule.

#### What It Does in Our System
Stores all credentials that the platform needs:

| Secret Path | What It Stores |
|---|---|
| `/IPS/Common/{env}/Database/Workflow` | SQL Server connection string |
| `/IPS/InvitedClub/{env}/PostAuth` | Oracle Fusion API credentials |
| `/IPS/Media/{env}/PostAuth` | Advantage/MergeWorld SOAP credentials |
| `/IPS/MDS/{env}/PostAuth` | RapidPay API credentials (per company code) |
| `/IPS/{client}/{env}/FeedAuth` | Feed download credentials per client |
| `/IPS/Common/{env}/SMTP` | Email server credentials |

#### Why We Use It (Not App.config or Environment Variables)
| Option | Problem |
|---|---|
| App.config / appsettings.json | Credentials in source code or config files - security risk |
| Environment variables | Visible in ECS task definition - still a risk |
| Secrets Manager | Encrypted at rest, access controlled by IAM, auto-rotation |

#### Benefit
- **Encrypted** - AES-256 encryption at rest
- **Access controlled** - only Lambda/Fargate IAM roles can read specific secrets
- **Audit trail** - every secret access is logged in CloudTrail
- **Auto-rotation** - can rotate database passwords automatically without code changes
- **Cost**: $0.40/secret/month - 20 secrets = $8/month

---

### 4.9 Amazon CloudWatch

#### What Is It? (Beginner Explanation)
CloudWatch is **the monitoring and logging system for everything in AWS**. Every Lambda invocation, every ECS task, every RDS query - all logs flow into CloudWatch. You can search logs, create dashboards, and set alarms.

#### What It Does in Our System

**Logs:**
- Every `WriteToFile()` call in the old code now goes to CloudWatch Logs
- Log group per client: `/ips/autopost/invitedclub/371`
- Searchable across all clients from one place
- 90-day retention

**Metrics:**
- `PostSuccessCount` - how many invoices posted successfully per job
- `PostFailCount` - how many failed
- `FeedDownloadDuration` - how long feed downloads take
- `LambdaErrors` - Lambda execution errors

**Alarms:**
- `PostFailCount > 5 in 5 minutes` → SNS alert → email/Slack
- `Lambda timeout` → SNS alert
- `RDS CPU > 80%` → SNS alert

**Dashboard:**
- "IPS AutoPost Operations" dashboard showing all jobs in one view

#### Benefit
- **Searchable** - find all errors for InvitedClub in the last hour with one query
- **Alerting** - know about failures before clients call you
- **Cost**: ~$5/month for log storage and metrics

---

### 4.10 AWS X-Ray

#### What Is It? (Beginner Explanation)
X-Ray is a **performance tracer**. It tracks a single request as it flows through Lambda → RDS → S3 → external API and shows you exactly where time is being spent. If a post is slow, X-Ray shows you whether the bottleneck is the database query, the HTTP call to the ERP, or the S3 image download.

#### What It Does in Our System
- Traces each post execution end-to-end
- Shows: Lambda init time + DB query time + HTTP post time + S3 image time
- Identifies slow queries and slow external API calls

#### Benefit
- **Performance debugging** - "InvitedClub posts are slow" → X-Ray shows it is the Oracle Fusion API taking 8 seconds per call
- **No code changes needed** - X-Ray SDK instruments automatically with one line of config

---

### 4.11 Amazon SNS (Simple Notification Service)

#### What Is It? (Beginner Explanation)
SNS is a **notification broadcaster**. When something goes wrong (CloudWatch alarm fires), SNS sends the alert to everyone who needs to know - email, Slack, PagerDuty - all at once.

#### What It Does in Our System
- Receives CloudWatch alarm notifications
- Broadcasts to email subscribers and Slack webhook
- Two topics: `ips-alerts-critical` (post failures) and `ips-alerts-warning` (slow jobs)

---

### 4.12 Amazon ECR (Elastic Container Registry)

#### What Is It? (Beginner Explanation)
ECR is a **private Docker image registry** - like Docker Hub but inside your AWS account. When you build the ECS Fargate container image, it gets pushed to ECR. ECS pulls it from there when starting tasks.

#### What It Does in Our System
- Stores the `ips-autopost-worker` Docker image
- ECS Fargate pulls the latest image when scaling up tasks
- Image scanning enabled - detects known vulnerabilities in the container

---

## 5. How Data Flows - Step by Step

### 5.1 Scheduled Auto-Post Flow (Most Common - e.g. InvitedClub)

This is what happens every day at 8 AM for InvitedClub:

```
STEP 1: EventBridge fires
+---------------------------+
| EventBridge Scheduler     |
| Rule: cron(0 8 * * ? *)   |
| Fires at 8:00 AM EST      |
| Sends JSON to Lambda:     |
| {                         |
|   "JobId": 371,           |
|   "ClientType":           |
|   "INVITEDCLUB",          |
|   "TriggerType":          |
|   "Scheduled"             |
| }                         |
+---------------------------+
            |
            v
STEP 2: Lambda starts (cold start ~1-2 sec)
+---------------------------+
| Lambda Function           |
| ips-autopost-platform     |
|                           |
| 1. Reads event JSON       |
| 2. Builds DI container    |
| 3. Calls                  |
|    AutoPostOrchestrator   |
|    .RunAsync(371)         |
+---------------------------+
            |
            v
STEP 3: Load configuration from RDS
+---------------------------+
| RDS SQL Server            |
|                           |
| SELECT * FROM             |
|  generic_job_config       |
| WHERE job_id = 371        |
|  AND is_active = 1        |
|                           |
| Returns: PostServiceUrl,  |
| HeaderTable, QueueIds,    |
| AuthType, etc.            |
+---------------------------+
            |
            v
STEP 4: Fetch credentials from Secrets Manager
+---------------------------+
| AWS Secrets Manager       |
|                           |
| GET /IPS/InvitedClub/     |
|   prod/PostAuth           |
|                           |
| Returns: username,        |
| password, token_url       |
+---------------------------+
            |
            v
STEP 5: Check schedule - should we run now?
+---------------------------+
| SchedulerService          |
|                           |
| LastPostTime = 7:55 AM    |
| Schedule = 08:00          |
| Window = 30 min           |
|                           |
| 8:00 is within 30 min     |
| of 08:00 -> YES, run      |
+---------------------------+
            |
            v
STEP 6: Fetch workitems from RDS
+---------------------------+
| WorkitemService           |
|                           |
| SELECT w.ItemId,          |
|   w.StatusId,             |
|   w.ImagePath             |
| FROM Workitems w          |
| JOIN WFInvoiceHeader h    |
|   ON w.ItemId = h.UID     |
| WHERE w.JobId = 371       |
|   AND w.StatusId IN (100) |
|   AND h.PostInProcess = 0 |
|                           |
| Returns: 47 workitems     |
+---------------------------+
            |
            v
STEP 7: Resolve InvitedClub plugin
+---------------------------+
| PluginRegistry            |
|                           |
| Resolve("INVITEDCLUB")    |
| -> InvitedClubPlugin      |
+---------------------------+
            |
            v
STEP 8: Plugin executes (InvitedClub-specific logic)
+---------------------------+
| InvitedClubPlugin         |
|                           |
| For each workitem:        |
|  1. Get image from S3     |
|  2. POST invoice to       |
|     Oracle Fusion         |
|     (Step 1: Invoice)     |
|  3. POST attachment       |
|     (Step 2: Attachment)  |
|  4. POST calculate tax    |
|     (Step 3: Tax)         |
|  5. Return PostResult     |
+---------------------------+
            |
            v
STEP 9: Core engine routes results
+---------------------------+
| RoutingService            |
|                           |
| Success -> WORKITEM_ROUTE |
|   StatusId = 200 (done)   |
| Fail -> WORKITEM_ROUTE    |
|   StatusId = 500 (failed) |
+---------------------------+
            |
            v
STEP 10: Write history and logs
+---------------------------+
| AuditService              |
|                           |
| INSERT generic_post_history|
| INSERT generic_exec_history|
| CloudWatch Logs:          |
|  "InvitedClub Job 371:    |
|   47 processed,           |
|   45 success, 2 failed"   |
+---------------------------+
            |
            v
STEP 11: Lambda completes, CloudWatch metrics updated
+---------------------------+
| CloudWatch Metrics        |
|                           |
| PostSuccessCount += 45    |
| PostFailCount += 2        |
| Duration = 4m 32s         |
|                           |
| If PostFailCount > 5:     |
|  SNS -> email alert       |
+---------------------------+
```

---

### 5.2 Manual Post Flow (From Workflow UI)

```
User clicks "Post Now" in Workflow UI
            |
            v
+---------------------------+
| Workflow UI               |
| HTTP POST to:             |
| /api/post/371/items/      |
|   456,789,1023            |
| Header: x-api-key: ***    |
+---------------------------+
            |
            v
+---------------------------+
| API Gateway               |
| Validates API key         |
| Routes to Lambda          |
| Passes: JobId=371,        |
|  ItemIds="456,789,1023"   |
+---------------------------+
            |
            v
+---------------------------+
| Lambda                    |
| Detects ItemIds != empty  |
| Calls RunManualAsync()    |
| Skips schedule check      |
| Runs only for those items |
+---------------------------+
            |
            v
(same steps 3-11 as above, but only for items 456, 789, 1023)
```

---

### 5.3 Long-Running Job Flow (Media Feed Download via ECS Fargate)

```
EventBridge fires at 7:00 AM for Media feed download
            |
            v
+---------------------------+
| EventBridge               |
| Sends message to SQS:     |
| {                         |
|   "JobId": 100,           |
|   "ClientType": "MEDIA",  |
|   "ExecutionType":        |
|   "FEED_DOWNLOAD"         |
| }                         |
+---------------------------+
            |
            v
+---------------------------+
| SQS: ips-long-job-queue   |
| Message waits in queue    |
| Visibility timeout: 60min |
+---------------------------+
            |
            v
+---------------------------+
| ECS Fargate Auto Scaling  |
| SQS depth > 0 detected    |
| Scales from 0 -> 1 task   |
| Pulls Docker image        |
| from ECR                  |
| Starts Worker container   |
+---------------------------+
            |
            v
+---------------------------+
| IPS.AutoPost.Host.Worker  |
| Polls SQS, gets message   |
| Calls AutoPostOrchestrator|
| Resolves MediaPlugin      |
| Calls ExecuteFeedDownload |
+---------------------------+
            |
            v
+---------------------------+
| MediaPlugin               |
| Calls Advantage SOAP API  |
| Downloads Vendor feed     |
| Downloads MediaOrders     |
|  (10-day chunks, 6 months)|
| Downloads GL, PO, Jobs    |
| Total time: ~25 minutes   |
| Bulk inserts to RDS       |
| Archives raw files to S3  |
+---------------------------+
            |
            v
+---------------------------+
| ECS Auto Scaling          |
| SQS depth = 0             |
| Scales back to 0 tasks    |
| Cost: $0 when idle        |
+---------------------------+
```

---

## 6. Security Architecture

### 6.1 IAM Roles and Least Privilege

Every AWS service gets its own IAM role with only the permissions it needs. Nothing has admin access.

```
Lambda Execution Role: ips-autopost-lambda-role
Permissions:
  - rds-db:connect                          (connect to RDS)
  - secretsmanager:GetSecretValue           (read secrets)
    Resource: arn:aws:secretsmanager:*:*:secret:/IPS/*
  - s3:GetObject                            (read invoice images)
    Resource: arn:aws:s3:::ips-invoice-images/*
  - s3:PutObject                            (write feed archives)
    Resource: arn:aws:s3:::ips-feed-archive/*
  - s3:PutObject                            (write output files)
    Resource: arn:aws:s3:::ips-output-files/*
  - sqs:SendMessage                         (send to retry queue)
    Resource: arn:aws:sqs:*:*:ips-retry-queue
  - logs:CreateLogGroup                     (write to CloudWatch)
  - logs:CreateLogStream
  - logs:PutLogEvents
  - xray:PutTraceSegments                   (X-Ray tracing)

ECS Task Role: ips-autopost-fargate-role
Permissions: (same as Lambda role above)

EventBridge Scheduler Role: ips-eventbridge-role
Permissions:
  - lambda:InvokeFunction                   (invoke Lambda)
    Resource: arn:aws:lambda:*:*:function:ips-autopost-platform
  - sqs:SendMessage                         (send to long-job queue)
    Resource: arn:aws:sqs:*:*:ips-long-job-queue
```

### 6.2 Network Security

```
Security Group: ips-autopost-lambda-sg
  Inbound:  NONE (Lambda does not accept inbound connections)
  Outbound: 
    - Port 1433 to RDS security group (SQL Server)
    - Port 443 to 0.0.0.0/0 via NAT Gateway (external ERP APIs)

Security Group: ips-rds-sg
  Inbound:
    - Port 1433 from ips-autopost-lambda-sg ONLY
    - Port 1433 from ips-autopost-fargate-sg ONLY
  Outbound: NONE

Security Group: ips-autopost-fargate-sg
  Inbound:  NONE
  Outbound:
    - Port 1433 to RDS security group
    - Port 443 to 0.0.0.0/0 via NAT Gateway
```

### 6.3 Encryption

| Data | Encryption |
|---|---|
| RDS database | AES-256 at rest (AWS managed key) |
| S3 buckets | SSE-S3 (server-side encryption) |
| Secrets Manager | AES-256 (AWS managed key) |
| SQS messages | SSE-SQS |
| All network traffic | TLS 1.2+ in transit |
| Lambda environment variables | KMS encrypted |

### 6.4 Secrets - No Credentials in Code

```
OLD WAY (bad):
  App.config:
    <add key="PostServiceUrl" value="https://api.oracle.com"/>
    <add key="Username" value="ips_user"/>
    <add key="Password" value="P@ssw0rd123"/>   <- in source code!

NEW WAY (correct):
  Code:
    var secret = await _secretsManager.GetSecretValueAsync(
        "/IPS/InvitedClub/prod/PostAuth");
    var creds = JsonSerializer.Deserialize<PostAuthSecret>(secret.SecretString);
    // creds.Username, creds.Password - fetched at runtime, never in code
```

---

## 7. Observability - Logs, Metrics, Alerts

### 7.1 Logging Strategy

Every log message from the old `CommonMethods.WriteToFile()` now goes to CloudWatch Logs.

```csharp
// OLD WAY - writes to a text file on the Windows local server
CommonMethods.WriteToFile("InvitedClub Job 371 - PostData started");

// NEW WAY - writes to CloudWatch Logs
_logger.LogInformation("[{ClientType}] Job {JobId} - PostData started",
    config.ClientType, config.JobId);
// Automatically goes to: /ips/autopost/invitedclub/371
```

**Log Group Structure:**
```
/ips/autopost/invitedclub/371     <- InvitedClub job 371
/ips/autopost/media/100           <- Media job 100
/ips/autopost/greenthal/200       <- Greenthal job 200
/ips/autopost/mds/300             <- MDS job 300
```

**Searching Logs (CloudWatch Insights):**
```sql
-- Find all errors in the last hour across ALL clients
fields @timestamp, @message
| filter @message like /ERROR/
| sort @timestamp desc
| limit 50

-- Find all failed posts for InvitedClub today
fields @timestamp, @message
| filter @logStream like /invitedclub/
| filter @message like /FAILED/
| sort @timestamp desc
```

### 7.2 Custom Metrics

```csharp
// Published after each job execution
await _cloudWatch.PutMetricDataAsync(new PutMetricDataRequest
{
    Namespace = "IPS/AutoPost",
    MetricData = new List<MetricDatum>
    {
        new MetricDatum
        {
            MetricName = "PostSuccessCount",
            Value = results.Count(r => r.IsSuccess),
            Dimensions = new List<Dimension>
            {
                new Dimension { Name = "ClientType", Value = config.ClientType },
                new Dimension { Name = "JobId", Value = config.JobId.ToString() }
            }
        },
        new MetricDatum
        {
            MetricName = "PostFailCount",
            Value = results.Count(r => !r.IsSuccess),
            // same dimensions
        },
        new MetricDatum
        {
            MetricName = "ExecutionDurationSeconds",
            Value = (DateTime.Now - startTime).TotalSeconds,
            // same dimensions
        }
    }
});
```

### 7.3 CloudWatch Alarms

| Alarm | Condition | Action |
|---|---|---|
| `PostFailRate-Critical` | PostFailCount > 10 in 5 min | SNS -> email + Slack |
| `PostFailRate-Warning` | PostFailCount > 3 in 5 min | SNS -> email |
| `LambdaTimeout` | Lambda duration > 14 min | SNS -> email |
| `LambdaErrors` | Lambda error count > 0 | SNS -> email |
| `RDS-HighCPU` | RDS CPU > 80% for 5 min | SNS -> email |
| `SQS-DLQ-Messages` | DLQ message count > 0 | SNS -> email (investigate stuck jobs) |
| `FeedDownloadFailed` | Feed execution status = FAILED | SNS -> email |

### 7.4 Operations Dashboard

The CloudWatch Dashboard "IPS AutoPost Operations" shows:

```
+------------------------------------------------------------------+
|  IPS AutoPost Operations Dashboard                               |
+------------------------------------------------------------------+
|  Jobs Run Today: 47        Success Rate: 98.2%    Failures: 3   |
+------------------------------------------------------------------+
|  [Post Success Count - 24h]    [Post Fail Count - 24h]          |
|  ████████████████████          ▂▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁           |
|                                                                  |
|  [Execution Duration by Client]  [Lambda Invocations]           |
|  InvitedClub: 4m 32s             ████████████████████           |
|  Media:       8m 15s                                            |
|  Greenthal:   2m 10s                                            |
|  MDS:         22m 40s (Fargate)                                 |
|                                                                  |
|  [RDS Connections]               [SQS Queue Depth]              |
|  ████▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄       ▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁           |
+------------------------------------------------------------------+
```

---

## 8. Cost Architecture

### 8.1 Cost New Architecture


**New Architecture (Lambda + Fargate):**
```
Lambda:
  15 jobs x 2 runs/day x 5 min avg x 1024MB
  = 15 x 2 x 300 sec x 1GB
  = 9,000 GB-seconds/day
  = 270,000 GB-seconds/month
  Cost: $0.0000166667 per GB-second
  = $4.50/month

ECS Fargate (Media + MDS only, ~2 hours/day):
  2 tasks x 2 hours/day x 30 days x (0.25 vCPU + 2GB)
  = ~$15/month

EventBridge Scheduler:
  15 rules x 2 fires/day x 30 days = 900 invocations
  = $0.01/month (essentially free)

SQS:
  < 10,000 messages/month = $0.00 (free tier)

S3:
  100GB feed archives + output files = $2.30/month

Secrets Manager:
  20 secrets x $0.40 = $8/month

CloudWatch:
  Logs + metrics + alarms = ~$10/month

RDS (existing, no change):
  Already running = $0 additional

Total New Cost: ~$40/month
Savings: ~$560-760/month (~87% reduction)
```

### 8.2 Cost Optimization Tips

| Optimization | Saving |
|---|---|
| Lambda ARM64 architecture | 20% cheaper than x86_64 |
| S3 Intelligent-Tiering for feed archives | Moves old files to cheaper storage automatically |
| CloudWatch log retention = 90 days (not forever) | Prevents log storage costs growing unbounded |
| ECS Fargate Spot for non-critical jobs | Up to 70% cheaper (with interruption tolerance) |
| Reserved Lambda concurrency | Predictable cost for high-volume jobs |

---

## 9. Infrastructure as Code - CloudFormation

All AWS resources are defined in CloudFormation templates. This means the entire infrastructure can be recreated in a new AWS account with one command.

### 9.1 CloudFormation Stack Structure

```
infra/cloudformation/
  +-- platform-stack.yaml           (main stack - imports all nested stacks)
  +-- network-stack.yaml            (VPC, subnets, NAT gateway, VPC endpoints)
  +-- compute-stack.yaml            (Lambda, ECS cluster, task definitions)
  +-- data-stack.yaml               (S3 buckets, RDS parameter groups)
  +-- security-stack.yaml           (IAM roles, security groups)
  +-- observability-stack.yaml      (CloudWatch log groups, alarms, dashboards)
  +-- scheduler-stack.yaml          (EventBridge rules - generated from DB)
```

### 9.2 Sample CloudFormation Template (Lambda)

```yaml
AWSTemplateFormatVersion: '2010-09-09'
Description: IPS AutoPost Platform - Lambda Function

Parameters:
  Environment:
    Type: String
    AllowedValues: [dev, staging, prod]
    Default: prod
  
  LambdaMemorySize:
    Type: Number
    Default: 1024
    Description: Lambda memory in MB
  
  LambdaTimeout:
    Type: Number
    Default: 900
    Description: Lambda timeout in seconds (max 900 = 15 min)

Resources:
  AutoPostLambdaFunction:
    Type: AWS::Lambda::Function
    Properties:
      FunctionName: !Sub ips-autopost-platform-${Environment}
      Runtime: dotnet8
      Handler: IPS.AutoPost.Host.Lambda::IPS.AutoPost.Host.Lambda.Function::FunctionHandler
      Code:
        S3Bucket: !Sub ips-deployment-artifacts-${AWS::Region}
        S3Key: !Sub autopost/${Environment}/lambda-package.zip
      MemorySize: !Ref LambdaMemorySize
      Timeout: !Ref LambdaTimeout
      Role: !GetAtt LambdaExecutionRole.Arn
      VpcConfig:
        SecurityGroupIds:
          - !Ref LambdaSecurityGroup
        SubnetIds:
          - !Ref PrivateSubnetA
          - !Ref PrivateSubnetB
      Environment:
        Variables:
          ENVIRONMENT: !Ref Environment
          DB_SECRET_ARN: !Sub arn:aws:secretsmanager:${AWS::Region}:${AWS::AccountId}:secret:/IPS/Common/${Environment}/Database/Workflow
      TracingConfig:
        Mode: Active
      Tags:
        - Key: Project
          Value: IPS-AutoPost
        - Key: Environment
          Value: !Ref Environment

  LambdaExecutionRole:
    Type: AWS::IAM::Role
    Properties:
      RoleName: !Sub ips-autopost-lambda-role-${Environment}
      AssumeRolePolicyDocument:
        Version: '2012-10-17'
        Statement:
          - Effect: Allow
            Principal:
              Service: lambda.amazonaws.com
            Action: sts:AssumeRole
      ManagedPolicyArns:
        - arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole
        - arn:aws:iam::aws:policy/AWSXRayDaemonWriteAccess
      Policies:
        - PolicyName: AutoPostLambdaPolicy
          PolicyDocument:
            Version: '2012-10-17'
            Statement:
              - Effect: Allow
                Action:
                  - secretsmanager:GetSecretValue
                Resource: !Sub arn:aws:secretsmanager:${AWS::Region}:${AWS::AccountId}:secret:/IPS/*
              - Effect: Allow
                Action:
                  - s3:GetObject
                Resource: arn:aws:s3:::ips-invoice-images/*
              - Effect: Allow
                Action:
                  - s3:PutObject
                Resource:
                  - arn:aws:s3:::ips-feed-archive/*
                  - arn:aws:s3:::ips-output-files/*
              - Effect: Allow
                Action:
                  - sqs:SendMessage
                Resource: !GetAtt RetryQueue.Arn
              - Effect: Allow
                Action:
                  - cloudwatch:PutMetricData
                Resource: '*'

  LambdaSecurityGroup:
    Type: AWS::EC2::SecurityGroup
    Properties:
      GroupName: !Sub ips-autopost-lambda-sg-${Environment}
      GroupDescription: Security group for AutoPost Lambda
      VpcId: !Ref VPC
      SecurityGroupEgress:
        - IpProtocol: tcp
          FromPort: 1433
          ToPort: 1433
          DestinationSecurityGroupId: !Ref RDSSecurityGroup
        - IpProtocol: tcp
          FromPort: 443
          ToPort: 443
          CidrIp: 0.0.0.0/0

  RetryQueue:
    Type: AWS::SQS::Queue
    Properties:
      QueueName: !Sub ips-retry-queue-${Environment}
      VisibilityTimeout: 3600
      MessageRetentionPeriod: 345600
      RedrivePolicy:
        deadLetterTargetArn: !GetAtt DeadLetterQueue.Arn
        maxReceiveCount: 3

  DeadLetterQueue:
    Type: AWS::SQS::Queue
    Properties:
      QueueName: !Sub ips-dlq-${Environment}
      MessageRetentionPeriod: 1209600

Outputs:
  LambdaFunctionArn:
    Description: ARN of the Lambda function
    Value: !GetAtt AutoPostLambdaFunction.Arn
    Export:
      Name: !Sub ${AWS::StackName}-LambdaArn
  
  RetryQueueUrl:
    Description: URL of the retry queue
    Value: !Ref RetryQueue
    Export:
      Name: !Sub ${AWS::StackName}-RetryQueueUrl
```

### 9.3 Deploying the Stack

```bash
# Deploy to dev environment
aws cloudformation deploy \
  --template-file infra/cloudformation/platform-stack.yaml \
  --stack-name ips-autopost-dev \
  --parameter-overrides Environment=dev \
  --capabilities CAPABILITY_NAMED_IAM \
  --region us-east-1

# Deploy to prod environment
aws cloudformation deploy \
  --template-file infra/cloudformation/platform-stack.yaml \
  --stack-name ips-autopost-prod \
  --parameter-overrides Environment=prod LambdaMemorySize=2048 \
  --capabilities CAPABILITY_NAMED_IAM \
  --region us-east-1
```

### 9.4 EventBridge Rules - Generated from Database

EventBridge rules are created dynamically based on `generic_execution_schedule` table rows. A separate Lambda function (or script) reads the database and creates/updates EventBridge rules.

```csharp
// Scheduler Sync Lambda (runs once per hour)
public async Task SyncSchedulesAsync()
{
    var schedules = await _db.GetActiveSchedulesAsync();
    
    foreach (var schedule in schedules)
    {
        var ruleName = $"ips-autopost-job{schedule.JobId}-{schedule.ScheduleType}";
        
        await _eventBridge.PutRuleAsync(new PutRuleRequest
        {
            Name = ruleName,
            ScheduleExpression = schedule.CronExpression ?? ConvertHHmmToCron(schedule.ExecutionTime),
            State = schedule.IsActive ? RuleState.ENABLED : RuleState.DISABLED,
            Description = $"AutoPost schedule for Job {schedule.JobId} ({schedule.ClientType})"
        });
        
        await _eventBridge.PutTargetsAsync(new PutTargetsRequest
        {
            Rule = ruleName,
            Targets = new List<Target>
            {
                new Target
                {
                    Id = "1",
                    Arn = _lambdaArn,
                    Input = JsonSerializer.Serialize(new
                    {
                        JobId = schedule.JobId,
                        ClientType = schedule.ClientType,
                        TriggerType = "Scheduled"
                    })
                }
            }
        });
    }
}
```

---

## 10. Deployment Pipeline

### 10.1 CI/CD Pipeline (GitHub Actions Example)

```yaml
name: Deploy IPS AutoPost Platform

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

env:
  AWS_REGION: us-east-1
  DOTNET_VERSION: '8.0.x'

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}
      
      - name: Restore dependencies
        run: dotnet restore IPS.AutoPost.Platform.sln
      
      - name: Build
        run: dotnet build IPS.AutoPost.Platform.sln --configuration Release --no-restore
      
      - name: Run tests
        run: dotnet test IPS.AutoPost.Platform.sln --configuration Release --no-build --verbosity normal
      
      - name: Publish Lambda
        run: |
          dotnet publish src/IPS.AutoPost.Host.Lambda/IPS.AutoPost.Host.Lambda.csproj \
            --configuration Release \
            --runtime linux-x64 \
            --self-contained false \
            --output ./publish/lambda
      
      - name: Package Lambda
        run: |
          cd ./publish/lambda
          zip -r ../../lambda-package.zip .
      
      - name: Upload Lambda artifact
        uses: actions/upload-artifact@v3
        with:
          name: lambda-package
          path: lambda-package.zip

  deploy-dev:
    needs: build-and-test
    if: github.ref == 'refs/heads/develop'
    runs-on: ubuntu-latest
    environment: dev
    steps:
      - uses: actions/checkout@v3
      
      - name: Download Lambda artifact
        uses: actions/download-artifact@v3
        with:
          name: lambda-package
      
      - name: Configure AWS credentials
        uses: aws-actions/configure-aws-credentials@v2
        with:
          aws-access-key-id: ${{ secrets.AWS_ACCESS_KEY_ID }}
          aws-secret-access-key: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
          aws-region: ${{ env.AWS_REGION }}
      
      - name: Upload to S3
        run: |
          aws s3 cp lambda-package.zip \
            s3://ips-deployment-artifacts-${{ env.AWS_REGION }}/autopost/dev/lambda-package.zip
      
      - name: Update Lambda function
        run: |
          aws lambda update-function-code \
            --function-name ips-autopost-platform-dev \
            --s3-bucket ips-deployment-artifacts-${{ env.AWS_REGION }} \
            --s3-key autopost/dev/lambda-package.zip
      
      - name: Wait for Lambda update
        run: aws lambda wait function-updated --function-name ips-autopost-platform-dev
      
      - name: Run smoke test
        run: |
          aws lambda invoke \
            --function-name ips-autopost-platform-dev \
            --payload '{"JobId": 999, "TriggerType": "SmokeTest"}' \
            response.json
          cat response.json

  deploy-prod:
    needs: build-and-test
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    environment: prod
    steps:
      # Same as deploy-dev but with Environment=prod
      # Requires manual approval in GitHub Actions
```

### 10.2 Blue/Green Deployment for Lambda

Lambda supports aliases and versions for zero-downtime deployments.

```bash
# Publish new version
NEW_VERSION=$(aws lambda publish-version \
  --function-name ips-autopost-platform-prod \
  --query 'Version' --output text)

# Update alias to point to new version (gradual rollout)
aws lambda update-alias \
  --function-name ips-autopost-platform-prod \
  --name live \
  --function-version $NEW_VERSION \
  --routing-config AdditionalVersionWeights={"$NEW_VERSION"=0.1}

# Monitor for 10 minutes, then shift 100% traffic
aws lambda update-alias \
  --function-name ips-autopost-platform-prod \
  --name live \
  --function-version $NEW_VERSION
```

### 10.3 Rollback Strategy

```bash
# Rollback Lambda to previous version
PREVIOUS_VERSION=$(aws lambda get-alias \
  --function-name ips-autopost-platform-prod \
  --name live \
  --query 'FunctionVersion' --output text)

aws lambda update-alias \
  --function-name ips-autopost-platform-prod \
  --name live \
  --function-version $((PREVIOUS_VERSION - 1))
```

---

## 11. Beginner Glossary

If you are new to AWS, here is a plain-English explanation of every term used in this document.

| Term | Plain English Explanation |
|---|---|
| **AWS** | Amazon Web Services - Amazon's cloud computing platform. Instead of buying servers, you rent computing power from Amazon and pay only for what you use. |
| **Lambda** | Code that runs without a server. You upload your app, AWS runs it when triggered, you pay per millisecond of execution. Zero cost when idle. |
| **ECS Fargate** | Containers without managing servers. You package your app in Docker, AWS runs it. No 15-minute limit like Lambda. |
| **EC2** | A virtual machine in the cloud. Like renting a computer from Amazon. Runs 24/7 whether you use it or not. |
| **EventBridge Scheduler** | A cloud alarm clock. You tell it "run this at 8 AM every day" and it fires a trigger at that time. No server needed. |
| **SQS** | A message queue. A holding area for tasks. One service puts a message in, another picks it up and processes it. Messages are never lost. |
| **RDS** | A managed database in the cloud. AWS handles backups, patching, and failover. You just connect to it like any database. |
| **S3** | Unlimited file storage in the cloud. Like a hard drive that never fills up. Files are stored in "buckets". |
| **Secrets Manager** | A secure vault for passwords and API keys. Your code fetches credentials at runtime instead of storing them in config files. |
| **CloudWatch** | AWS's monitoring and logging system. All logs, metrics, and alarms in one place. |
| **X-Ray** | A performance tracer. Shows you exactly where time is spent in a request (database, HTTP call, S3, etc.). |
| **SNS** | A notification broadcaster. When an alarm fires, SNS sends the alert to email, Slack, etc. |
| **ECR** | A private Docker image registry. Where you store your container images before ECS pulls them. |
| **IAM** | Identity and Access Management. Controls who (or what) can access which AWS resources. |
| **IAM Role** | A set of permissions assigned to an AWS service (like Lambda). Lambda uses its role to access RDS, S3, etc. |
| **VPC** | Virtual Private Cloud. A private network inside AWS. Your Lambda and RDS live inside this network, isolated from the internet. |
| **Subnet** | A section of a VPC. Private subnets have no direct internet access (more secure). |
| **Security Group** | A firewall rule for AWS resources. Controls which ports and IP addresses can connect. |
| **NAT Gateway** | Allows resources in private subnets (Lambda, Fargate) to make outbound internet calls (to external ERP APIs) without being directly accessible from the internet. |
| **VPC Endpoint** | A private connection from your VPC to AWS services (S3, Secrets Manager) without going through the internet. Faster and more secure. |
| **CloudFormation** | Infrastructure as Code. You describe your AWS resources in a YAML file and AWS creates them automatically. |
| **Docker / Container** | A package that contains your application and all its dependencies. Runs the same way on any machine. |
| **CI/CD Pipeline** | Continuous Integration / Continuous Deployment. Automated process that builds, tests, and deploys your code when you push to Git. |
| **Dead Letter Queue (DLQ)** | A special SQS queue where messages go after failing too many times. Used for investigation without losing the message. |
| **Multi-AZ** | Multiple Availability Zones. AWS runs your database in two physical locations simultaneously. If one fails, the other takes over automatically. |
| **Cold Start** | The time it takes Lambda to start up when it has not been used recently (~1-2 seconds for .NET). After the first invocation, subsequent ones are faster. |
| **Cron Expression** | A text pattern that describes a schedule. `cron(0 8 * * ? *)` means "every day at 8:00 AM". |
| **ARM64** | A processor architecture (used in Apple M1 chips and AWS Graviton). Lambda on ARM64 is 20% cheaper than x86_64. |
| **Blue/Green Deployment** | A deployment strategy where you run the new version alongside the old one, gradually shift traffic, and roll back instantly if something goes wrong. |
| **Alias (Lambda)** | A pointer to a specific Lambda version. The `live` alias points to the current production version. You can shift traffic between versions using aliases. |

---

## Summary - Architecture at a Glance

```
+=========================================================================+
|              IPS AUTOPOST PLATFORM - ARCHITECTURE SUMMARY               |
+=========================================================================+

WHAT TRIGGERS JOBS:
  EventBridge Scheduler  -> Scheduled jobs (cron per job_config row)
  API Gateway            -> Manual triggers from Workflow UI
  SQS                    -> Long-running job queue + retry queue

WHERE JOBS RUN:
  Lambda                 -> All short jobs (< 15 min): InvitedClub, Greenthal,
                            Vantaca, Akron, Caliber, Michelman, Signature,
                            Rent Manager, ReactorNet, Workday, Trump, MOB
  ECS Fargate            -> Long jobs: Media feed download, MDS TIFF processing

WHERE DATA LIVES:
  RDS SQL Server         -> Workflow database (existing + new generic tables)
  S3                     -> Images, feed archives, output files
  Secrets Manager        -> All credentials (DB, API keys, SMTP)

HOW WE MONITOR:
  CloudWatch Logs        -> All log output, searchable, 90-day retention
  CloudWatch Metrics     -> PostSuccessCount, PostFailCount, Duration
  CloudWatch Alarms      -> Alert on failures, timeouts, high error rates
  SNS                    -> Email + Slack notifications
  X-Ray                  -> Performance tracing

HOW WE DEPLOY:
  CloudFormation         -> All infrastructure as code
  GitHub Actions         -> Build, test, deploy pipeline
  Lambda Aliases         -> Blue/green deployment, instant rollback
