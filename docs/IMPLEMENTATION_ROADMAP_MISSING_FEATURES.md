# Implementation Roadmap — Missing Features from Architecture Diagram

## Scope

This document covers features visible in the architecture diagram that are **not yet implemented** in our Generic-Solution. Focus: InvitedClub + Sevita clients only. Our platform is generic with plugin-based architecture, so enhancements benefit all clients automatically.

**Excluded from this document** (decided not to implement):
- ~~AWS X-Ray~~ — Not needed; CorrelationId in logs is sufficient
- ~~Slack Notifications~~ — Email via SNS is sufficient
- ~~AWS CloudTrail~~ — Default account-level trail is enough
- ~~AWS Config~~ — Not needed for a 2-person team
- ~~Backup & DR / RDS Multi-AZ / S3 Versioning~~ — RDS is pre-existing (managed separately); S3 versioning is on deployment bucket only

---

## Feature 1: AWS Step Functions (Invoice Posting Orchestration)

**Diagram location:** Left purple box — "AWS Step Functions (Orchestrates Invoice Posting Workflow)" with 4 Lambda steps

### What We Have Currently

Our posting flow runs inside a single C# class (`AutoPostOrchestrator`) on an ECS Fargate worker:

```
SQS Message → PostWorker → MediatR Pipeline → AutoPostOrchestrator
                                                  ├─ Load config from DB
                                                  ├─ Check schedule window
                                                  ├─ OnBeforePostAsync (plugin hook)
                                                  ├─ Fetch workitems
                                                  ├─ plugin.ExecutePostAsync() ← sequential loop
                                                  ├─ Write execution history
                                                  └─ Publish CloudWatch metrics
```

All steps run in a single process. If the process crashes mid-batch, the SQS message becomes visible again after the visibility timeout (2 hours), and the next worker picks it up — reprocessing only items with `PostInProcess=0`.

### What Step Functions Would Add

Replace `AutoPostOrchestrator` with an AWS-managed state machine:

```
SQS Message → Step Functions State Machine
                ├─ State 1: Lambda — Configuration Loader
                ├─ State 2: Lambda — Work Item Fetcher
                ├─ State 3: Map State — Batch Processors (PARALLEL)
                │            ├─ Lambda invocation for Item 101
                │            ├─ Lambda invocation for Item 102
                │            └─ Lambda invocation for Item 103 (concurrent)
                ├─ State 4: Lambda — Results Aggregator
                └─ Catch: Lambda — Error Handler / Notification
```

### Benefits

| Benefit | Description |
|---|---|
| Visual execution tracking | See real-time in AWS Console — which step is running, which failed, full input/output |
| Parallel workitem processing | Map State processes 20-50 items simultaneously (one Lambda per item) |
| Per-step retry with backoff | Each step has its own retry config (exponential backoff on Oracle 503) |
| Resume from failure | Restart from the failed step, not the beginning |
| Built-in execution history | Every execution stored 90 days free |
| Error routing | Catch blocks route to specific handlers per error type |

### Cons

| Con | Description |
|---|---|
| Lambda 15-minute limit | Each Lambda has max 15-min runtime. Fine per-item, but limits large batch processing within a single invocation. |
| Cold start latency | 1-3 seconds per Lambda invocation. For 50-item manual post = 100s overhead. Our ECS has zero cold start. |
| Increased complexity | State machine JSON, IAM roles per Lambda, separate deployments. More moving parts. |
| Not suitable for Sevita's OnBeforePostAsync | Sevita loads ValidIds via raw SqlConnection before the loop. Passing large HashSet between Lambda steps is awkward. |
| Team skill requirement | Need to learn Step Functions ASL (Amazon States Language) and distributed Lambda debugging. |

### Effort: ~11 days

### Recommendation

**Don't implement now.** Our ECS-based orchestrator works correctly. Step Functions becomes valuable when we need parallel processing for 500+ items or visual operational dashboards. For InvitedClub + Sevita with 10-100 item batches, the sequential C# approach is simpler, faster, and equally reliable.

---

## Feature 2: Parallel Batch Processing

**Diagram location:** Left purple box — "3. Batch Processors (Lambda, Parallel)" and "Workflow Capabilities: Parallel Batch Processing"

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

100 workitems = 100 × 2-5 seconds = 200-500 seconds total.

However, the PostWorker polls **10 SQS messages simultaneously** (`MaxNumberOfMessages=10`), so 10 different jobs run concurrently. Parallelism is at the **job level**, not the workitem level within a single job.

### What Parallel Processing Would Add

```csharp
var batches = workitems.Chunk(10);
foreach (var batch in batches)
{
    var tasks = batch.Select(item => ProcessSingleItemAsync(item));
    await Task.WhenAll(tasks);
}
```

### Benefits

| Benefit | Description |
|---|---|
| Faster batch completion | 100 items at 10-concurrent = ~50 seconds instead of 500 |
| Better for manual posts | User waits less time for synchronous response |
| Better queue throughput | Messages leave the queue faster |

### Cons

| Con | Description |
|---|---|
| Oracle Fusion rate limits | Oracle may throttle concurrent requests from same IP (HTTP 429) |
| Sevita API rate limits | Same concern for Sevita's OAuth2 API |
| Error complexity | Multiple items failing simultaneously need independent error handling |
| Database connection pressure | 10 concurrent × 10 polled messages = 100 simultaneous DB connections (pool is 2000, so safe) |

### Effort: ~7 days

### Recommendation

**Low priority.** Typical batches (10-50 items) complete in 30-120 seconds sequentially. Implement when batch sizes regularly exceed 200+ items or users complain about wait times.

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

## Feature 5: Notifications — Push Status to UI

**Diagram location:** Top right — "NOTIFICATIONS: Amazon SNS → Email" and bottom flow step 5 — "Status pushed to UI via API / WebSocket / Polling"

### What We Have Currently

- **Scheduled posts:** Results written to `generic_execution_history`. No push notification to anyone. Users must check the Status API.
- **Manual posts:** Synchronous response — user gets the result immediately.
- **Failure alerts:** CloudWatch Alarm → SNS → Email to ops team.

### What the Diagram Shows

After every execution (scheduled or manual), status is actively pushed to the Workflow UI so users see results without manually checking.

### Benefits

| Benefit | Description |
|---|---|
| Ops awareness | Team knows immediately when a scheduled batch completes or fails |
| User experience | No need to manually check status API after triggering a post |
| Proactive alerts | UI can show "InvitedClub 8:00 AM batch: 95 success, 5 failed" without user action |

### Cons

| Con | Description |
|---|---|
| UI integration required | Workflow UI needs a notification panel or WebSocket connection |
| Complexity | Need to define what events to push, to whom, and how (WebSocket vs polling vs email) |
| Over-notification | 4 scheduled batches/day × 2 clients = 8 notifications/day — potentially noisy |

### Effort: ~5 days (backend: SNS → notification endpoint) + UI team work

### Recommendation

**Not needed now.** Our CloudWatch Alarms handle failure notifications via email. Success notifications are low value — nobody needs to know "everything worked fine." Implement when the operations team actively monitors a dashboard and wants real-time feed.

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

| # | Feature | Effort | Benefit for InvitedClub + Sevita | Priority |
|---|---|---|---|---|
| 1 | **S3 Gateway Endpoint** (free) | 0.5 day | Better security, minor cost savings | **Do First** |
| 2 | **Parallel Batch Processing** | 7 days | Faster InvitedClub batches | **Medium — when batch sizes grow** |
| 3 | **Async Manual Post** | 8-10 days | Prevents timeouts on large manual posts | **Low — not needed at current volume** |
| 4 | **VPC Interface Endpoints** | 2.5 days | Cost savings (only if NAT > $60/month) | **Low — measure first** |
| 5 | **Notifications / Push to UI** | 5 days | Proactive ops awareness | **Low** |
| 6 | **Step Functions** | 11 days | Visual ops, parallel processing | **Not Now** |
| 7 | **Additional ERP Clients** | 6-11 days each | N/A (future scope) | **Not Now** |
| 8 | **SOAP/FTP/SFTP Feed Sources** | 7-9 days | N/A (no current client needs it) | **Not Now** |

---

## Suggested Implementation Order

### Immediate (this week)
1. **S3 Gateway Endpoint** — add to `infrastructure.yaml`. Free. Zero risk.

### When Needed (triggered by real usage)
2. **Parallel Batch Processing** — when InvitedClub batches regularly exceed 200 items
3. **Async Manual Post** — when users report browser timeout on large manual posts
4. **VPC Interface Endpoints** — when monthly NAT data transfer costs exceed $60

### Future (production scale)
5. **Notifications / Push to UI** — when ops team wants proactive status
6. **Step Functions** — when workflows become complex or visual dashboards are required
7. **Additional Clients** — one at a time, after platform proven in production
8. **SOAP/FTP/SFTP** — when a client that requires these is onboarded
