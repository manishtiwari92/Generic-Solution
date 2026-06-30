# Per-Client SQS Queue Design — Dynamic Provisioning

## Overview

This document describes how the IPS AutoPost Platform would implement **per-client SQS queues** with fully dynamic provisioning. The goal is that onboarding a new client requires **only a database INSERT** — no CloudFormation changes, no manual AWS Console work, no worker restarts.

---

## Current State (Shared Queue Model)

```
┌─────────────────────────────────────────────────────────┐
│  EventBridge Scheduler                                   │
│  ┌──────────────────┐  ┌──────────────────┐            │
│  │ INVITEDCLUB POST │  │ SEVITA POST      │            │
│  └────────┬─────────┘  └────────┬─────────┘            │
│           │                      │                       │
│           └──────────┬───────────┘                       │
│                      ▼                                   │
│           ┌─────────────────────┐                        │
│           │  ips-post-queue     │  ← single shared queue │
│           └────────┬────────────┘                        │
│                    ▼                                     │
│           ┌─────────────────────┐                        │
│           │  PostWorker (ECS)   │  reads ClientType      │
│           │  resolves plugin    │  from message body     │
│           └─────────────────────┘                        │
└─────────────────────────────────────────────────────────┘

Queues: 4 total (2 primary + 2 DLQ)
Scaling: Shared across all clients
Isolation: Logical (message body routing)
```

---

## Proposed State (Per-Client Queue Model)

```
┌─────────────────────────────────────────────────────────────────────────┐
│  EventBridge Scheduler                                                   │
│  ┌──────────────────┐  ┌──────────────────┐  ┌───────────────────────┐ │
│  │ INVITEDCLUB POST │  │ SEVITA POST      │  │ VANTACA POST (new!)   │ │
│  └────────┬─────────┘  └────────┬─────────┘  └──────────┬────────────┘ │
│           │                      │                        │              │
│           ▼                      ▼                        ▼              │
│  ┌──────────────────┐  ┌──────────────────┐  ┌───────────────────────┐ │
│  │ips-post-         │  │ips-post-         │  │ips-post-              │ │
│  │invitedclub-prod  │  │sevita-prod       │  │vantaca-prod           │ │
│  └────────┬─────────┘  └────────┬─────────┘  └──────────┬────────────┘ │
│           │                      │                        │              │
│           └──────────────────────┼────────────────────────┘              │
│                                  ▼                                       │
│                   ┌────────────────────────────────┐                     │
│                   │  PostWorker (ECS)               │                     │
│                   │  Discovers all queues from DB   │                     │
│                   │  Polls each queue in parallel   │                     │
│                   └────────────────────────────────┘                     │
└─────────────────────────────────────────────────────────────────────────┘

Queues: N×4 (post + post-DLQ + feed + feed-DLQ per client)
Scaling: Can be per-client or shared pool
Isolation: Physical (separate queues)
```

---

## Database Schema Changes

### New columns on `generic_job_configuration`

```sql
ALTER TABLE generic_job_configuration ADD
    post_queue_url      VARCHAR(500)  NULL,   -- SQS URL for posting messages
    post_queue_arn      VARCHAR(500)  NULL,   -- SQS ARN (used by EventBridge target)
    post_dlq_arn        VARCHAR(500)  NULL,   -- DLQ ARN (used for CloudWatch alarm)
    feed_queue_url      VARCHAR(500)  NULL,   -- SQS URL for feed messages
    feed_queue_arn      VARCHAR(500)  NULL,   -- SQS ARN
    feed_dlq_arn        VARCHAR(500)  NULL,   -- DLQ ARN
    queue_provisioned   BIT NOT NULL DEFAULT 0;  -- flag: queues have been created
```

When the Scheduler Lambda creates the queues, it writes the URLs/ARNs back to these columns and sets `queue_provisioned = 1`.

---

## Queue Naming Convention

```
Pattern:  ips-{pipeline}-{client_type_lowercase}-{env}
DLQ:      ips-{pipeline}-{client_type_lowercase}-dlq-{env}

Examples:
  ips-post-invitedclub-prod
  ips-post-invitedclub-dlq-prod
  ips-feed-invitedclub-prod
  ips-feed-invitedclub-dlq-prod
  ips-post-sevita-prod
  ips-post-sevita-dlq-prod
  ips-post-vantaca-uat
  ips-post-vantaca-dlq-uat
```

Queue configuration (same for all):
- `VisibilityTimeout`: 7200 seconds (2 hours)
- `MessageRetentionPeriod`: 1,209,600 seconds (14 days)
- `maxReceiveCount`: 3 (→ DLQ after 3 failures)

---

## Scheduler Lambda — Queue Provisioning Logic

### Pseudocode

```
function SyncAsync():
    rows = LoadAllActiveJobs()
    
    for each row in rows:
        if row.queue_provisioned == false:
            // --- PROVISION NEW QUEUES ---
            postDlqArn = CreateQueue("ips-post-{clientType}-dlq-{env}")
            postQueueArn = CreateQueue("ips-post-{clientType}-{env}",
                                       redrivePolicy: { dlqArn: postDlqArn, maxReceiveCount: 3 })
            
            feedDlqArn = CreateQueue("ips-feed-{clientType}-dlq-{env}")
            feedQueueArn = CreateQueue("ips-feed-{clientType}-{env}",
                                       redrivePolicy: { dlqArn: feedDlqArn, maxReceiveCount: 3 })
            
            // Create CloudWatch alarm on each DLQ
            CreateDlqAlarm("ips-post-{clientType}-dlq-alarm-{env}", postDlqArn)
            CreateDlqAlarm("ips-feed-{clientType}-dlq-alarm-{env}", feedDlqArn)
            
            // Write back to DB
            UPDATE generic_job_configuration
            SET post_queue_url = ..., post_queue_arn = ..., post_dlq_arn = ...,
                feed_queue_url = ..., feed_queue_arn = ..., feed_dlq_arn = ...,
                queue_provisioned = 1
            WHERE id = row.id
        
        // --- SYNC EVENTBRIDGE RULE (same as today) ---
        targetArn = row.post_queue_arn  // now client-specific!
        CreateOrUpdateEventBridgeRule(targetArn, ...)
```

### IAM Permissions Required (Scheduler Lambda)

```json
{
  "Effect": "Allow",
  "Action": [
    "sqs:CreateQueue",
    "sqs:GetQueueAttributes",
    "sqs:SetQueueAttributes",
    "sqs:TagQueue",
    "cloudwatch:PutMetricAlarm"
  ],
  "Resource": "arn:aws:sqs:us-east-1:*:ips-*"
}
```

---

## Worker Discovery — How Workers Find Client Queues

### Option A: Database-Driven Discovery (Recommended)

```csharp
// PostWorker.cs — modified ExecuteAsync

protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        // Refresh queue list every 5 minutes from DB
        var queueUrls = await _configRepo.GetActivePostQueueUrlsAsync(stoppingToken);
        // Returns: ["https://sqs.../ips-post-invitedclub-prod",
        //           "https://sqs.../ips-post-sevita-prod",
        //           "https://sqs.../ips-post-vantaca-prod"]
        
        // Poll all queues in parallel
        var pollTasks = queueUrls.Select(url => PollQueueAsync(url, stoppingToken));
        await Task.WhenAll(pollTasks);
        
        // Brief pause before next discovery cycle
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
    }
}

private async Task PollQueueAsync(string queueUrl, CancellationToken ct)
{
    var response = await _sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
    {
        QueueUrl = queueUrl,
        WaitTimeSeconds = 20,
        MaxNumberOfMessages = 10
    }, ct);
    
    foreach (var message in response.Messages)
        await ProcessMessageAsync(queueUrl, message, ct);
}
```

### Option B: Environment Variable with Refresh

Workers start with no queue URLs. A sidecar process or the worker itself queries the DB and updates an in-memory list. Less elegant than Option A but workable.

### Option C: One Worker Per Client (ECS Service per client)

Heavy-handed — each client gets its own ECS Service with a dedicated `SQS_QUEUE_URL`. Requires infrastructure changes to add a new client. Not recommended for this platform.

**Recommended: Option A** — single worker pool polls all queues dynamically.

---

## Onboarding a New Client — Complete Flow

```
Step 1: DBA/Engineer
────────────────────
INSERT INTO generic_job_configuration (
    client_type, job_id, job_name, is_active, source_queue_id,
    success_queue_id, primary_fail_queue_id, header_table, ...
) VALUES (
    'VANTACA', 3001, 'Vantaca AutoPost', 1, '401,402',
    500, 600, 'WFVantacaIndexHeader', ...
);

INSERT INTO generic_execution_schedule (
    job_config_id, schedule_type, cron_expression, is_active
) VALUES (
    @newJobConfigId, 'POST', 'cron(0 8 * * ? *)', 1
);


Step 2: Wait ≤ 10 minutes (Scheduler Lambda fires)
────────────────────────────────────────────────────
Scheduler Lambda:
  1. Reads VANTACA job — sees queue_provisioned = 0
  2. Creates ips-post-vantaca-prod queue + DLQ
  3. Creates ips-feed-vantaca-prod queue + DLQ (if download_feed=1)
  4. Creates CloudWatch DLQ alarms
  5. Updates DB: queue URLs/ARNs, queue_provisioned = 1
  6. Creates EventBridge rule: ips-autopost-3001-post
     targeting ips-post-vantaca-prod


Step 3: Worker picks it up (next discovery cycle, ≤ 5 minutes)
──────────────────────────────────────────────────────────────
PostWorker:
  - Refreshes active queue list from DB
  - Sees new URL: https://sqs.../ips-post-vantaca-prod
  - Starts polling it alongside existing queues
  - No restart needed


Step 4: First scheduled execution fires
────────────────────────────────────────
EventBridge → ips-post-vantaca-prod → PostWorker picks up message
→ PluginRegistry.Resolve("VANTACA") → VantacaPlugin.ExecutePostAsync()


TOTAL TIME FROM DB INSERT TO FIRST EXECUTION: ≤ 15 minutes
MANUAL AWS WORK REQUIRED: ZERO
```

---

## Deactivating a Client

```
Step 1: DBA sets is_active = 0

Step 2: Scheduler Lambda (next cycle):
  - Disables EventBridge rule (no new messages produced)
  - Does NOT delete the queue (existing messages must drain)

Step 3: Worker (next discovery cycle):
  - Sees queue_provisioned=1 but is_active=0
  - Stops polling that queue URL
  - Any remaining messages in the queue expire after 14 days
    (or can be manually purged)

Step 4: Optional cleanup (manual or automated after 30 days):
  - Delete empty queues
  - Delete CloudWatch alarms
  - Set queue_provisioned = 0
```

---

## Scaling Strategies

### Shared Worker Pool (Recommended for ≤ 10 clients)

All client queues are polled by the same ECS service (1-5 tasks). Auto-scaling is based on the **sum** of all queue depths across all client queues.

```
CloudWatch Metric: Sum of ApproximateNumberOfMessagesVisible
                   across all ips-post-* queues
Alarm threshold: > 10 → scale out
```

### Per-Client Scaling (For 10+ clients or SLA differentiation)

Each client's queue depth drives independent scaling. Requires:
- Separate ECS Services per client, OR
- A weighted polling strategy where the worker allocates more concurrent polls to deeper queues

---

## Monitoring & Alerting

| What | How | Granularity |
|---|---|---|
| DLQ alarm | One CloudWatch alarm per client DLQ | Per-client |
| Queue depth | Dashboard widget per client queue | Per-client |
| Processing latency | CloudWatch metric `PostDurationSeconds` dimensioned by ClientType | Per-client |
| Success/failure rate | CloudWatch metric `PostSuccessCount`/`PostFailedCount` | Per-client |
| Worker health | ECS task count, CPU, memory | Shared pool |

---

## IAM Policy Changes

### Worker IAM Role (ECSTaskRole)

```yaml
# Current: specific queue ARNs
- Effect: Allow
  Action: [sqs:ReceiveMessage, sqs:DeleteMessage, sqs:GetQueueAttributes]
  Resource: 
    - !GetAtt PostQueue.Arn
    - !GetAtt FeedQueue.Arn

# New: wildcard pattern
- Effect: Allow
  Action: [sqs:ReceiveMessage, sqs:DeleteMessage, sqs:GetQueueAttributes, sqs:GetQueueUrl]
  Resource:
    - !Sub "arn:aws:sqs:${AWS::Region}:${AWS::AccountId}:ips-post-*-${Environment}"
    - !Sub "arn:aws:sqs:${AWS::Region}:${AWS::AccountId}:ips-feed-*-${Environment}"
```

### Scheduler Lambda IAM Role

```yaml
# Add queue creation permissions
- Effect: Allow
  Action: [sqs:CreateQueue, sqs:GetQueueAttributes, sqs:SetQueueAttributes, sqs:TagQueue]
  Resource:
    - !Sub "arn:aws:sqs:${AWS::Region}:${AWS::AccountId}:ips-*-${Environment}"
- Effect: Allow
  Action: [cloudwatch:PutMetricAlarm, cloudwatch:DeleteAlarms]
  Resource: "*"
```

---

## Migration Path (Shared → Per-Client)

If we decide to adopt this approach later, the migration is non-breaking:

1. **Add DB columns** (`post_queue_url`, `feed_queue_url`, `queue_provisioned`)
2. **Update Scheduler Lambda** with queue provisioning logic
3. **Update Worker** to use DB-driven queue discovery instead of `SQS_QUEUE_URL` env var
4. **Run Scheduler Lambda** once — it creates per-client queues and updates DB
5. **Workers restart** — pick up new queue URLs from DB
6. **Old shared queues** drain naturally (keep them for 14 days, then delete)

No client-facing impact. No data loss. Fully backward-compatible.

---

## Trade-offs Summary

| Pro | Con |
|---|---|
| Complete client isolation (failures, retries) | More SQS queues to manage (cost is negligible — SQS is cheap) |
| Per-client DLQ alarms (instant visibility) | Scheduler Lambda becomes more complex |
| Independent retry policies possible | Worker polling logic more complex |
| Per-client scaling possible | IAM policy uses wildcards (slightly less restrictive) |
| Zero-infra onboarding preserved | Queue cleanup when deactivating a client |
| Better operational visibility | Dashboard with N queues instead of 1 |

---

## Recommendation

**For current scope (2 clients):** Shared queues are fine. Keep the current design.

**Trigger to migrate:** When we onboard the 5th client, OR when we hit a scenario where one client's high volume delays another client's processing. At that point, implement this design — it's a 2-3 day effort with zero downtime.
