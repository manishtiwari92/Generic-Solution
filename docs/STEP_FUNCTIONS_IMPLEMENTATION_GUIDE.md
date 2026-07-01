# Step Functions Implementation Guide — Detailed Technical Design

### IPS.AutoPost Platform | AWS Step Functions + .NET Lambda | Multi-Mode Execution

> **Based on:** IMPLEMENTATION_ROADMAP_MISSING_FEATURES.md
> **Date:** July 1, 2026
> **Scope:** Complete implementation details for the Step Functions architecture
> **Existing code:** IPS.AutoPost.Core, IPS.AutoPost.Plugins (InvitedClub + Sevita), ECS PostWorker

---

## Table of Contents

1. [New Projects to Create](#1-new-projects-to-create)
2. [Database Schema Changes](#2-database-schema-changes)
3. [S3 Temp Storage Design](#3-s3-temp-storage-design)
4. [Lambda 1: Start Workflow](#4-lambda-1-start-workflow)
5. [Lambda 2: Prepare Execution](#5-lambda-2-prepare-execution)
6. [Lambda 3A: Item Processor (PER_ITEM)](#6-lambda-3a-item-processor-per_item)
7. [Lambda 3B: Batch File Processor (BATCH_FILE)](#7-lambda-3b-batch-file-processor-batch_file)
8. [Lambda 3C: Hybrid Processor](#8-lambda-3c-hybrid-processor)
9. [Lambda 4: Aggregate Results](#9-lambda-4-aggregate-results)
10. [Lambda 5: Notify](#10-lambda-5-notify)
11. [Step Functions State Machine (ASL)](#11-step-functions-state-machine-asl)
12. [How Aggregate Knows All Items Are Done](#12-how-aggregate-knows-all-items-are-done)
13. [Scheduler Lambda Updates](#13-scheduler-lambda-updates)
14. [API Changes (Manual Post → Async)](#14-api-changes-manual-post--async)
15. [RDS Proxy Configuration](#15-rds-proxy-configuration)
16. [CloudFormation Resources](#16-cloudformation-resources)
17. [Migration Strategy (ECS → Lambda)](#17-migration-strategy-ecs--lambda)
18. [Error Handling and Retry Logic](#18-error-handling-and-retry-logic)

---

## 1. New Projects to Create

```
IPS.AutoPost.Platform.sln (add these new projects)
│
├── src/
│   ├── IPS.AutoPost.Lambda.StartWorkflow/        # Lambda 1: SQS → Start SF execution
│   │   ├── Function.cs
│   │   ├── IPS.AutoPost.Lambda.StartWorkflow.csproj
│   │   └── aws-lambda-tools-defaults.json
│   │
│   ├── IPS.AutoPost.Lambda.Prepare/              # Lambda 2: Load config, fetch items
│   │   ├── Function.cs
│   │   ├── IPS.AutoPost.Lambda.Prepare.csproj
│   │   └── aws-lambda-tools-defaults.json
│   │
│   ├── IPS.AutoPost.Lambda.ItemProcessor/        # Lambda 3A: PER_ITEM (1 item per invoke)
│   │   ├── Function.cs
│   │   ├── IPS.AutoPost.Lambda.ItemProcessor.csproj
│   │   └── aws-lambda-tools-defaults.json
│   │
│   ├── IPS.AutoPost.Lambda.BatchProcessor/       # Lambda 3B: BATCH_FILE (all items)
│   │   ├── Function.cs
│   │   ├── IPS.AutoPost.Lambda.BatchProcessor.csproj
│   │   └── aws-lambda-tools-defaults.json
│   │
│   ├── IPS.AutoPost.Lambda.Aggregate/            # Lambda 4: Collect results, write history
│   │   ├── Function.cs
│   │   ├── IPS.AutoPost.Lambda.Aggregate.csproj
│   │   └── aws-lambda-tools-defaults.json
│   │
│   ├── IPS.AutoPost.Lambda.Notify/               # Lambda 5: SNS email
│   │   ├── Function.cs
│   │   ├── IPS.AutoPost.Lambda.Notify.csproj
│   │   └── aws-lambda-tools-defaults.json
│   │
│   └── IPS.AutoPost.Lambda.Shared/               # Shared models between Lambdas
│       ├── Models/
│       │   ├── StepFunctionPayload.cs
│       │   ├── PrepareResult.cs
│       │   ├── ItemProcessorResult.cs
│       │   ├── BatchProcessorResult.cs
│       │   └── AggregateResult.cs
│       └── IPS.AutoPost.Lambda.Shared.csproj
│
└── infra/
    └── cloudformation/
        └── stepfunctions.yaml                    # State machine + Lambda functions
```

**All Lambda projects reference:**
- `IPS.AutoPost.Core` (engine, repositories, models, interfaces)
- `IPS.AutoPost.Plugins` (client plugins)
- `IPS.AutoPost.Lambda.Shared` (inter-Lambda payload models)
- `Amazon.Lambda.Core`, `Amazon.Lambda.Serialization.SystemTextJson`

---

## 2. Database Schema Changes

```sql
-- Add to generic_job_configuration
ALTER TABLE generic_job_configuration ADD execution_mode VARCHAR(20) NOT NULL DEFAULT 'PER_ITEM';
ALTER TABLE generic_job_configuration ADD execution_target VARCHAR(10) NOT NULL DEFAULT 'ECS';
ALTER TABLE generic_job_configuration ADD max_concurrency INT NOT NULL DEFAULT 50;
ALTER TABLE generic_job_configuration ADD suspected_duplicates_queue_id INT NULL;

-- New table: track Step Functions executions for polling API
CREATE TABLE generic_step_function_execution (
    id BIGINT IDENTITY(1,1) PRIMARY KEY,
    execution_id VARCHAR(100) NOT NULL,          -- SF execution ARN or custom ID
    sf_execution_arn VARCHAR(500) NULL,           -- Full Step Functions execution ARN
    job_id INT NOT NULL,
    client_type VARCHAR(50) NOT NULL,
    execution_mode VARCHAR(20) NOT NULL,
    trigger_type VARCHAR(20) NOT NULL,            -- 'Scheduled' or 'Manual'
    item_ids VARCHAR(MAX) NULL,                   -- Original item IDs (manual post)
    status VARCHAR(20) NOT NULL DEFAULT 'QUEUED', -- QUEUED→PREPARING→PROCESSING→COMPLETED→FAILED
    total_items INT NULL,
    items_success INT NULL,
    items_failed INT NULL,
    files_generated VARCHAR(MAX) NULL,            -- JSON array of file names (BATCH_FILE)
    error_message VARCHAR(MAX) NULL,
    start_time DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    end_time DATETIME2 NULL,
    created_at DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

CREATE INDEX IX_sf_execution_id ON generic_step_function_execution(execution_id);
CREATE INDEX IX_sf_job_status ON generic_step_function_execution(job_id, status);
```

---

## 3. S3 Temp Storage Design

```
Bucket: ips-autopost-temp-{env}
Lifecycle: auto-delete after 24 hours (safety net for cleanup failures)

Structure per execution:
  s3://ips-autopost-temp-{env}/{executionId}/
    ├── config.json              (5-10KB — GenericJobConfig + auth + S3Config)
    └── valid-ids.json           (Sevita PER_ITEM only — ~70KB for 2000 vendor+employee IDs)

Output files (permanent):
  s3://ips-output-files/{clientType}/{date}/{filename}
```

**What goes to S3 and what stays in DB:**

| Data | Stored In | Why |
|---|---|---|
| `config.json` | S3 (cached) | PER_ITEM: avoids 5000 config+secrets queries. All modes: resolved credentials cached once. |
| `valid-ids.json` | S3 (cached) | Sevita PER_ITEM only: loaded once by Prepare, read by 5000 Item Processors. |
| Batch header+detail data | **DB query (NOT S3)** | Batch Lambda queries DB directly — only 1 Lambda, 1 query, no pressure. |
| Item results | Step Functions payload | Map state collects results automatically (fits in 256KB). |

**Why NOT cache batch-data.json to S3:**
- BATCH_FILE mode has ONE Lambda making ONE DB query — no connection pressure.
- The existing `WFGeneric_Post_GetHeaderDataByJobID` SP returns the full DataSet in 200-500ms (even for 20K items).
- Avoids serializing/deserializing 30-100MB JSON unnecessarily.
- Simpler architecture — fewer moving parts.

**Why config.json IS cached to S3:**
- PER_ITEM mode has 5000 concurrent Lambdas. Without caching, each would query DB for config + call Secrets Manager = 5000 redundant round-trips.
- S3 GET for a 5KB file = 50ms. DB query + Secrets Manager = 200-400ms.
- Total savings: 5000 × 300ms = 25 minutes of wasted DB time eliminated.

---

## 4. Lambda 1: Start Workflow

**Trigger:** SQS event source mapping (one mapping per client queue)
**Runtime:** .NET 10 | Memory: 256MB | Timeout: 30 seconds
**Purpose:** Lightweight SQS consumer that starts a Step Functions execution

```csharp
// IPS.AutoPost.Lambda.StartWorkflow/Function.cs
public class Function
{
    private readonly IAmazonStepFunctions _sfClient;
    private readonly IConfigurationRepository _configRepo;
    private readonly string _stateMachineArn;

    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        foreach (var record in sqsEvent.Records)
        {
            var message = JsonSerializer.Deserialize<SqsMessagePayload>(record.Body);

            // Load basic config to get execution_mode and execution_target
            var config = await _configRepo.GetByJobIdAsync(message.JobId, CancellationToken.None);

            if (config == null || !config.IsActive)
            {
                context.Logger.LogWarning($"Job {message.JobId} inactive. Skipping.");
                return; // Message will be deleted (successful Lambda return)
            }

            // If execution_target is ECS, don't start SF — let ECS handle it
            if (config.ExecutionTarget == "ECS")
            {
                // Forward to ECS queue (existing ips-post-queue for backward compat)
                await ForwardToEcsQueue(message);
                return;
            }

            var executionId = $"{config.ClientType}-{config.JobId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";

            // Record execution in DB for polling API
            await SaveExecutionRecord(executionId, config, message);

            // Start Step Functions execution
            var input = new StepFunctionPayload
            {
                ExecutionId = executionId,
                JobId = message.JobId,
                ClientType = config.ClientType,
                ExecutionMode = config.ExecutionMode,
                TriggerType = message.TriggerType,
                ItemIds = message.ItemIds,       // null for scheduled, comma-sep for manual
                UserId = message.UserId,
                MaxConcurrency = config.MaxConcurrency
            };

            await _sfClient.StartExecutionAsync(new StartExecutionRequest
            {
                StateMachineArn = _stateMachineArn,
                Name = executionId,  // Must be unique (used as idempotency key)
                Input = JsonSerializer.Serialize(input)
            });

            context.Logger.LogInformation($"Started SF execution: {executionId}");
        }
    }
}
```

**Key behaviors:**
- If `execution_target == 'ECS'`: forwards message to shared ECS queue (rollback path)
- Generates unique `executionId` for tracking
- Inserts record into `generic_step_function_execution` with status `QUEUED`
- Starts SF execution with the payload

---

## 5. Lambda 2: Prepare Execution

**Trigger:** Step Functions Task state (first state in the machine)
**Runtime:** .NET 10 | Memory: 512MB | Timeout: 2 minutes
**Purpose:** Load config, fetch items, cache data to S3

```csharp
// IPS.AutoPost.Lambda.Prepare/Function.cs
public class Function
{
    private readonly IConfigurationRepository _configRepo;
    private readonly IWorkitemRepository _workitemRepo;
    private readonly IAmazonS3 _s3Client;
    private readonly IPluginRegistry _pluginRegistry;
    private readonly string _tempBucket;

    public async Task<PrepareResult> FunctionHandler(StepFunctionPayload input, ILambdaContext ctx)
    {
        // 1. Update execution status to PREPARING
        await UpdateExecutionStatus(input.ExecutionId, "PREPARING");

        // 2. Load full job config from DB
        var config = await _configRepo.GetByJobIdAsync(input.JobId, CancellationToken.None);
        var s3Config = await _configRepo.GetEdenredApiUrlConfigAsync(CancellationToken.None);

        // 3. Plugin-specific OnBeforePostAsync
        var plugin = _pluginRegistry.Resolve(input.ClientType);
        await plugin.OnBeforePostAsync(config, CancellationToken.None);

        // 4. Cache config to S3 (all Lambdas will read this instead of hitting DB)
        var configCacheKey = $"{input.ExecutionId}/config.json";
        var configPayload = new CachedConfig
        {
            JobConfig = config,
            S3Config = s3Config,
            // Sevita: ValidIds are cached separately by the plugin's OnBeforePostAsync
        };
        await UploadToS3(configCacheKey, configPayload);

        // 5. Fetch pending ItemIds
        List<long> itemIds;
        if (!string.IsNullOrEmpty(input.ItemIds))
        {
            // Manual post: use provided item IDs
            var workitemDs = await _workitemRepo.GetWorkitemsByItemIdsAsync(input.ItemIds, CancellationToken.None);
            itemIds = ExtractItemIds(workitemDs);
        }
        else
        {
            // Scheduled post: fetch all pending items (PostInProcess=0)
            var workitemDs = await _workitemRepo.GetWorkitemsAsync(config, CancellationToken.None);
            itemIds = ExtractItemIds(workitemDs);
        }

        if (itemIds.Count == 0)
        {
            return new PrepareResult
            {
                ExecutionMode = input.ExecutionMode,
                TotalCount = 0,
                ItemIds = Array.Empty<long>(),
                SkipProcessing = true   // Choice state will route to Aggregate directly
            };
        }

        // 7. Update execution status to PROCESSING
        await UpdateExecutionStatus(input.ExecutionId, "PROCESSING", totalItems: itemIds.Count);

        return new PrepareResult
        {
            ExecutionId = input.ExecutionId,
            ExecutionMode = input.ExecutionMode,
            ClientType = input.ClientType,
            JobId = input.JobId,
            TriggerType = input.TriggerType,
            UserId = input.UserId,
            MaxConcurrency = input.MaxConcurrency,
            ItemIds = itemIds.ToArray(),
            TotalCount = itemIds.Count,
            ConfigS3Key = configCacheKey,
            SkipProcessing = false
        };
    }
}
```

**Output flows to Choice state** which reads `ExecutionMode` and routes to Path A/B/C.
**Note:** Batch data is NOT cached to S3. The Batch Processor Lambda queries DB directly (one Lambda, one query — no pressure).

---

## 6. Lambda 3A: Item Processor (PER_ITEM)

**Trigger:** Step Functions Map state (one invocation per itemId)
**Runtime:** .NET 10 | Memory: 512MB | Timeout: 60 seconds
**Purpose:** Process ONE item — call external ERP API, route, write history

```csharp
// IPS.AutoPost.Lambda.ItemProcessor/Function.cs
public class Function
{
    private readonly IAmazonS3 _s3;
    private readonly IServiceProvider _serviceProvider;

    public async Task<ItemProcessorResult> FunctionHandler(ItemProcessorInput input, ILambdaContext ctx)
    {
        // 1. Load cached config from S3 (50ms vs 200ms from DB)
        var cachedConfig = await LoadFromS3<CachedConfig>(input.ConfigS3Key);
        var config = cachedConfig.JobConfig;

        // 2. Create scoped DI (same pattern as ECS PostWorker — scoped per message)
        await using var scope = _serviceProvider.CreateAsyncScope();
        var plugin = scope.ServiceProvider.GetRequiredService<PluginRegistry>().Resolve(config.ClientType);
        var routingRepo = scope.ServiceProvider.GetRequiredService<IRoutingRepository>();
        var auditRepo = scope.ServiceProvider.GetRequiredService<IAuditRepository>();
        var workitemRepo = scope.ServiceProvider.GetRequiredService<IWorkitemRepository>();

        long itemId = input.ItemId;
        var result = new ItemProcessorResult { ItemId = itemId };

        try
        {
            // 3. Load workitem header + detail from DB (this item only)
            var headerDs = await workitemRepo.GetHeaderDetailByItemIdAsync(itemId, config, CancellationToken.None);

            // 4. Set PostInProcess = 1
            await routingRepo.SetPostInProcessAsync(itemId, config.HeaderTable, CancellationToken.None);

            // 5. Build PostContext (same as ECS path)
            var context = new PostContext
            {
                TriggerType = input.TriggerType,
                UserId = input.UserId,
                ItemId = itemId,
                HeaderDetailData = headerDs,
                S3Config = cachedConfig.S3Config,
                CancellationToken = CancellationToken.None
            };

            // 6. Call plugin.ExecutePostAsync for this ONE item
            //    Plugin handles: get image, validate, call API, route, write history
            var batchResult = await plugin.ExecutePostAsync(config, context, CancellationToken.None);

            result.Success = batchResult.RecordsFailed == 0;
            result.QueueId = result.Success ? config.SuccessQueueId : config.PrimaryFailQueueId;
            result.ErrorMessage = batchResult.ErrorMessage;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            ctx.Logger.LogError($"Item {itemId} failed: {ex}");
        }
        finally
        {
            // 7. Clear PostInProcess = 0 (ALWAYS, even on exception)
            await plugin.ClearPostInProcessAsync(itemId, config, routingRepo, CancellationToken.None);
        }

        return result;
    }
}
```

**Important:** This Lambda reuses the SAME `IClientPlugin.ExecutePostAsync` that ECS uses.
The plugin code doesn't know whether it's running on ECS or Lambda — same interface, same logic.

---

## 7. Lambda 3B: Batch File Processor (BATCH_FILE)

**Trigger:** Step Functions Task state (single invocation for ALL items)
**Runtime:** .NET 10 | Memory: 1024-2048MB | Timeout: 5 minutes
**Purpose:** Generate file(s) for all items, route all items, write history

```csharp
// IPS.AutoPost.Lambda.BatchProcessor/Function.cs
public class Function
{
    private readonly IAmazonS3 _s3;
    private readonly IServiceProvider _serviceProvider;

    public async Task<BatchProcessorResult> FunctionHandler(PrepareResult input, ILambdaContext ctx)
    {
        // 1. Load config from S3 cache (credentials already resolved)
        var cachedConfig = await LoadFromS3<CachedConfig>(input.ConfigS3Key);
        var config = cachedConfig.JobConfig;

        // 2. Query DB directly for full header+detail DataSet (ONE query, not cached to S3)
        //    This is the same SP the ECS PostWorker uses: WFGeneric_Post_GetHeaderDataByJobID
        //    For 20K items: ~500ms. No S3 serialization/deserialization overhead.
        await using var scope = _serviceProvider.CreateAsyncScope();
        var workitemRepo = scope.ServiceProvider.GetRequiredService<IWorkitemRepository>();
        var batchData = await workitemRepo.GetBatchHeaderDetailAsync(
            config, input.ItemIds, CancellationToken.None);

        var plugin = scope.ServiceProvider.GetRequiredService<PluginRegistry>().Resolve(config.ClientType);
        var routingRepo = scope.ServiceProvider.GetRequiredService<IRoutingRepository>();
        var auditRepo = scope.ServiceProvider.GetRequiredService<IAuditRepository>();

        // 3. Plugin handles all file generation logic internally:
        //    - Grouping by CompanyCode/Month/InvoiceType
        //    - Format-specific file generation (CSV, pipe-delimited, H/L/S)
        //    - Validation (duplicate detection, ChargeAccount, etc.)
        var context = new PostContext
        {
            TriggerType = input.TriggerType,
            UserId = input.UserId,
            BatchData = batchData,
            S3Config = cachedConfig.S3Config,
            CancellationToken = CancellationToken.None
        };

        var batchResult = await plugin.ExecutePostAsync(config, context, CancellationToken.None);

        // 4. Route all items in parallel (20 concurrent DB calls)
        var semaphore = new SemaphoreSlim(20);
        var routingTasks = batchResult.ItemResults.Select(async item =>
        {
            await semaphore.WaitAsync();
            try
            {
                await routingRepo.RouteAsync(item.ItemId, item.TargetQueueId, input.UserId,
                    item.Success ? "Automatic Route:" : "Automatic Route:", item.Comment);
                await auditRepo.AddGeneralLogAsync(input.UserId, item.ItemId,
                    $"File Created: {item.FileName}", "Contents");
                await auditRepo.SavePostHistoryAsync(item.ItemId, config, item.FileName,
                    item.FilePath, item.Success, input.UserId);
            }
            finally { semaphore.Release(); }
        });
        await Task.WhenAll(routingTasks);

        return new BatchProcessorResult
        {
            ExecutionId = input.ExecutionId,
            TotalCount = batchResult.RecordsProcessed,
            SuccessCount = batchResult.RecordsSuccess,
            FailedCount = batchResult.RecordsFailed,
            FilesGenerated = batchResult.FilesGenerated,
            ItemResults = batchResult.ItemResults.Select(i => new ItemProcessorResult
            {
                ItemId = i.ItemId,
                Success = i.Success,
                QueueId = i.TargetQueueId,
                ErrorMessage = i.ErrorMessage
            }).ToArray()
        };
    }
}
```

**Why direct DB query (not S3 cache):** There's only ONE Lambda invocation for BATCH_FILE. One DB query (~500ms for 20K items) is simpler and faster than serializing 30MB to S3, then downloading and deserializing it. No connection pressure — it's a single connection.

---

## 8. Lambda 3C: Hybrid Processor

For HYBRID mode, State 2C splits into two sub-states. The first (file generation) uses the same BatchProcessor Lambda. The second (per-item API calls) uses a new Lambda similar to ItemProcessor but for post-file operations.

The Step Functions ASL handles this as two sequential states within the HYBRID path. See Section 11 for the full ASL.

---

## 9. Lambda 4: Aggregate Results

**Trigger:** Step Functions Task state (runs AFTER all items complete)
**Runtime:** .NET 10 | Memory: 512MB | Timeout: 60 seconds
**Purpose:** Write execution history, publish metrics, send emails, cleanup S3

### How Does Aggregate Know All Items Are Done?

**Step Functions handles this automatically.** This is the key insight:

```
Map State (State 2A) processes 5000 items in parallel (MaxConcurrency=50).
Step Functions WAITS until ALL 5000 item Lambdas return.
ONLY THEN does it transition to State 3 (Aggregate).

The Map state COLLECTS all 5000 ItemProcessorResult objects into an array
and passes that array as input to the Aggregate Lambda.

You don't need to:
  ❌ Poll for completion
  ❌ Use DynamoDB counters
  ❌ Implement your own barrier/latch
  ❌ Check S3 for result files

Step Functions does this FOR YOU. That's the entire point of the Map state.
```

**For BATCH_FILE (Path B):** There's only ONE Lambda invocation — it returns directly. No waiting needed.

**For HYBRID (Path C):** State 2C-1 returns → State 2C-2 (Map) waits for all API calls → then transitions to Aggregate.

```csharp
// IPS.AutoPost.Lambda.Aggregate/Function.cs
public class Function
{
    private readonly IAuditRepository _auditRepo;
    private readonly IConfigurationRepository _configRepo;
    private readonly ICloudWatchMetricsService _metrics;
    private readonly IEmailService _emailService;
    private readonly IAmazonS3 _s3;

    public async Task<AggregateResult> FunctionHandler(AggregateInput input, ILambdaContext ctx)
    {
        // INPUT VARIES BY PATH:
        // Path A (PER_ITEM): input.ItemResults = ItemProcessorResult[] (from Map output)
        // Path B (BATCH_FILE): input.BatchResult = BatchProcessorResult (from single Lambda)
        // Path C (HYBRID): input.HybridResult = { batchPart + apiResults[] }

        // 1. Count totals (unified regardless of path)
        int totalProcessed, successCount, failedCount;
        string[] filesGenerated = Array.Empty<string>();

        if (input.ExecutionMode == "PER_ITEM")
        {
            // Map state passes ALL item results as an array automatically
            var results = input.ItemResults;
            totalProcessed = results.Length;
            successCount = results.Count(r => r.Success);
            failedCount = results.Count(r => !r.Success);
        }
        else if (input.ExecutionMode == "BATCH_FILE")
        {
            totalProcessed = input.BatchResult.TotalCount;
            successCount = input.BatchResult.SuccessCount;
            failedCount = input.BatchResult.FailedCount;
            filesGenerated = input.BatchResult.FilesGenerated;
        }
        else // HYBRID
        {
            // Combine batch file results + API call results
            totalProcessed = input.HybridResult.TotalCount;
            successCount = input.HybridResult.FileSuccessCount + input.HybridResult.ApiSuccessCount;
            failedCount = input.HybridResult.FileFailedCount + input.HybridResult.ApiFailedCount;
            filesGenerated = input.HybridResult.FilesGenerated;
        }

        // 2. Write generic_execution_history
        var history = new GenericExecutionHistory
        {
            ExecutionId = input.ExecutionId,
            JobId = input.JobId,
            ClientType = input.ClientType,
            ExecutionType = "POST",
            TriggerType = input.TriggerType,
            Status = failedCount == 0 ? "SUCCESS" : "PARTIAL_SUCCESS",
            RecordsProcessed = totalProcessed,
            RecordsSucceeded = successCount,
            RecordsFailed = failedCount,
            StartTime = input.StartTime,
            EndTime = DateTime.UtcNow
        };
        await _auditRepo.SaveExecutionHistoryAsync(history, input.ConnectionString, CancellationToken.None);

        // 3. Update LastPostTime
        await _configRepo.UpdateLastPostTimeAsync(input.ConfigId, CancellationToken.None);

        // 4. Publish CloudWatch metrics
        var duration = (DateTime.UtcNow - input.StartTime).TotalSeconds;
        await _metrics.PostSuccessCountAsync(input.ClientType, input.JobId, successCount, CancellationToken.None);
        await _metrics.PostFailedCountAsync(input.ClientType, input.JobId, failedCount, CancellationToken.None);
        await _metrics.PostDurationSecondsAsync(input.ClientType, input.JobId, duration, CancellationToken.None);

        // 5. Send batch-level emails (plugin-specific)
        if (failedCount > 0)
        {
            await SendFailureEmailsAsync(input);
        }

        // 6. Update execution record for polling API
        await UpdateExecutionStatus(input.ExecutionId, "COMPLETED",
            successCount, failedCount, filesGenerated);

        // 7. Clean up S3 temp files (config.json, valid-ids.json only — no large batch-data)
        await DeleteS3Prefix($"{input.ExecutionId}/");

        return new AggregateResult
        {
            ExecutionId = input.ExecutionId,
            TotalProcessed = totalProcessed,
            SuccessCount = successCount,
            FailedCount = failedCount,
            DurationSeconds = duration,
            FilesGenerated = filesGenerated
        };
    }
}
```

---

## 10. Lambda 5: Notify

**Trigger:** Step Functions Task state (after Aggregate)
**Runtime:** .NET 10 | Memory: 256MB | Timeout: 30 seconds
**Purpose:** Publish ONE email per execution via SNS

```csharp
// IPS.AutoPost.Lambda.Notify/Function.cs
public class Function
{
    private readonly IAmazonSimpleNotificationService _sns;
    private readonly string _topicArn;

    public async Task FunctionHandler(AggregateResult input, ILambdaContext ctx)
    {
        var subject = $"[IPS AutoPost] {input.ClientType} — " +
            (input.FailedCount == 0 ? "Completed" : "Completed with failures") +
            $" ({input.SuccessCount} success, {input.FailedCount} failed)";

        var body = $"""
            Job: {input.ClientType} (Job ID: {input.JobId})
            Type: {input.TriggerType} Post
            Mode: {input.ExecutionMode}
            Duration: {input.DurationSeconds:F1} seconds

            Results:
              Records Processed: {input.TotalProcessed}
              Successful: {input.SuccessCount}
              Failed: {input.FailedCount}
            """;

        if (input.FilesGenerated?.Length > 0)
        {
            body += $"\n  Files Generated: {string.Join(", ", input.FilesGenerated)}";
        }

        await _sns.PublishAsync(new PublishRequest
        {
            TopicArn = _topicArn,
            Subject = subject,
            Message = body
        });
    }
}
```

---

## 11. Step Functions State Machine (ASL)

```json
{
  "Comment": "IPS AutoPost — Unified posting state machine (PER_ITEM, BATCH_FILE, HYBRID)",
  "StartAt": "PrepareExecution",
  "States": {
    "PrepareExecution": {
      "Type": "Task",
      "Resource": "arn:aws:lambda:{region}:{account}:function:ips-autopost-prepare-{env}",
      "ResultPath": "$.prepare",
      "Next": "CheckItemCount"
    },

    "CheckItemCount": {
      "Type": "Choice",
      "Choices": [
        {
          "Variable": "$.prepare.skipProcessing",
          "BooleanEquals": true,
          "Next": "AggregateResults"
        }
      ],
      "Default": "ChooseExecutionMode"
    },

    "ChooseExecutionMode": {
      "Type": "Choice",
      "Choices": [
        {
          "Variable": "$.prepare.executionMode",
          "StringEquals": "PER_ITEM",
          "Next": "ProcessItemsMap"
        },
        {
          "Variable": "$.prepare.executionMode",
          "StringEquals": "BATCH_FILE",
          "Next": "BatchFileProcessor"
        },
        {
          "Variable": "$.prepare.executionMode",
          "StringEquals": "HYBRID",
          "Next": "HybridFileGeneration"
        }
      ],
      "Default": "ProcessItemsMap"
    },

    "ProcessItemsMap": {
      "Type": "Map",
      "ItemsPath": "$.prepare.itemIds",
      "MaxConcurrencyPath": "$.prepare.maxConcurrency",
      "ItemSelector": {
        "itemId.$": "$$.Map.Item.Value",
        "jobId.$": "$.prepare.jobId",
        "clientType.$": "$.prepare.clientType",
        "configS3Key.$": "$.prepare.configS3Key",
        "triggerType.$": "$.prepare.triggerType",
        "userId.$": "$.prepare.userId"
      },
      "ItemProcessor": {
        "ProcessorConfig": { "Mode": "INLINE" },
        "StartAt": "ProcessItem",
        "States": {
          "ProcessItem": {
            "Type": "Task",
            "Resource": "arn:aws:lambda:{region}:{account}:function:ips-autopost-item-processor-{env}",
            "Retry": [
              {
                "ErrorEquals": ["ERP503Error", "ERP429Error"],
                "IntervalSeconds": 5,
                "MaxAttempts": 2,
                "BackoffRate": 2.0
              }
            ],
            "Catch": [
              {
                "ErrorEquals": ["States.ALL"],
                "ResultPath": "$.error",
                "Next": "ItemFailed"
              }
            ],
            "End": true
          },
          "ItemFailed": {
            "Type": "Pass",
            "Result": { "success": false },
            "End": true
          }
        }
      },
      "ResultPath": "$.itemResults",
      "Next": "AggregateResults"
    },

    "BatchFileProcessor": {
      "Type": "Task",
      "Resource": "arn:aws:lambda:{region}:{account}:function:ips-autopost-batch-processor-{env}",
      "InputPath": "$.prepare",
      "ResultPath": "$.batchResult",
      "TimeoutSeconds": 300,
      "Comment": "Single Lambda queries DB directly for full DataSet (no S3 cache needed for batch data)",
      "Next": "AggregateResults"
    },

    "HybridFileGeneration": {
      "Type": "Task",
      "Resource": "arn:aws:lambda:{region}:{account}:function:ips-autopost-batch-processor-{env}",
      "InputPath": "$.prepare",
      "ResultPath": "$.hybridFileResult",
      "TimeoutSeconds": 300,
      "Next": "CheckHybridApiNeeded"
    },

    "CheckHybridApiNeeded": {
      "Type": "Choice",
      "Choices": [
        {
          "Variable": "$.hybridFileResult.apiItemIds[0]",
          "IsPresent": true,
          "Next": "HybridApiCallsMap"
        }
      ],
      "Default": "AggregateResults"
    },

    "HybridApiCallsMap": {
      "Type": "Map",
      "ItemsPath": "$.hybridFileResult.apiItemIds",
      "MaxConcurrency": 10,
      "ItemProcessor": {
        "ProcessorConfig": { "Mode": "INLINE" },
        "StartAt": "PostFileApiCall",
        "States": {
          "PostFileApiCall": {
            "Type": "Task",
            "Resource": "arn:aws:lambda:{region}:{account}:function:ips-autopost-item-processor-{env}",
            "End": true
          }
        }
      },
      "ResultPath": "$.hybridApiResults",
      "Next": "AggregateResults"
    },

    "AggregateResults": {
      "Type": "Task",
      "Resource": "arn:aws:lambda:{region}:{account}:function:ips-autopost-aggregate-{env}",
      "ResultPath": "$.aggregateResult",
      "Next": "Notify"
    },

    "Notify": {
      "Type": "Task",
      "Resource": "arn:aws:lambda:{region}:{account}:function:ips-autopost-notify-{env}",
      "InputPath": "$.aggregateResult",
      "End": true
    }
  }
}
```

---

## 12. How Aggregate Knows All Items Are Done

This is the most common question. The answer is: **Step Functions does this automatically.**

### PER_ITEM Path (Map State)

```
Step Functions Map State behavior:

1. Map state receives: itemIds = [101, 102, 103, ..., 5000]
2. Map state invokes Item Processor Lambda for EACH itemId (up to MaxConcurrency=50 at a time)
3. Each Lambda returns: { itemId: 101, success: true, queueId: 200 }
4. Map state WAITS until ALL 5000 Lambdas have returned (success or caught error)
5. Map state COLLECTS all 5000 results into a JSON array
6. Map state transitions to next state (Aggregate) with the collected array as output

YOU DO NOT NEED:
  ❌ DynamoDB atomic counters ("processed 4999/5000...")
  ❌ SQS completion queues
  ❌ S3 result files that Aggregate polls for
  ❌ EventBridge events per completion
  ❌ SNS fan-in patterns
  ❌ Custom synchronization barriers

Step Functions IS the synchronization barrier. That's why we use it.
```

### What Happens If One Item Fails?

```
Map state has "Catch" on each item:
  - If Item Lambda throws → caught → returns { success: false, error: "..." }
  - Map state continues processing OTHER items (does not abort)
  - Failed item's result is included in the collected array
  - Aggregate Lambda receives ALL results (success + failures)
  - Aggregate counts success/failed from the array

If you WANT to abort the entire batch on first failure:
  - Set "ToleratedFailurePercentage": 0 in Map state config
  - Map aborts immediately on first failure
  - (We DON'T want this — we want all items to attempt processing)

Our config: "ToleratedFailurePercentage": 100
  - Process ALL items even if some fail
  - Collect all results
  - Aggregate handles the counting
```

### BATCH_FILE Path

```
No Map state — just a single Task state (BatchFileProcessor Lambda).
That Lambda processes ALL items and returns ONE result object.
Step Functions transitions directly to Aggregate with that result.
No synchronization needed — it's synchronous within the Lambda.
```

### HYBRID Path

```
State 2C-1 (file gen) → returns { apiItemIds: [201, 202, 203] }
State 2C-2 (Map state) → processes each apiItemId → WAITS for all → transitions
Aggregate receives: file results + API call results (combined)
```

### Visual Execution Timeline

```
PER_ITEM with 100 items, MaxConcurrency=50:

Time 0s:   PrepareExecution Lambda starts
Time 3s:   PrepareExecution returns { itemIds: [1..100] }
Time 3s:   Map state starts — launches 50 Lambdas immediately
Time 3s:   Items 1-50 start processing simultaneously
Time 8s:   Item 1 completes (7s) → slot freed → Item 51 starts
Time 9s:   Item 2 completes (8s) → slot freed → Item 52 starts
...
Time 15s:  Items 1-50 all done, Items 51-100 processing
Time 22s:  ALL 100 items done
Time 22s:  Map state AUTOMATICALLY transitions to AggregateResults
Time 22s:  AggregateResults Lambda receives: ItemProcessorResult[100]
Time 24s:  AggregateResults writes history, publishes metrics
Time 24s:  Transitions to Notify
Time 25s:  Notify sends ONE email
Time 25s:  State machine SUCCEEDS

Total wall-clock time: ~25 seconds for 100 items (vs 700s sequential)
```

---

## 13. Scheduler Lambda Updates

The existing Scheduler Lambda (`IPS.AutoPost.Scheduler`) needs these additions:

```csharp
// Additional responsibilities per run (every 10 minutes):

// 1. Per-client queue provisioning
foreach (var job in activeJobs.Where(j => j.ExecutionTarget == "LAMBDA"))
{
    var queueName = $"ips-post-{job.ClientType.ToLower()}-{env}";
    var dlqName = $"ips-post-{job.ClientType.ToLower()}-dlq-{env}";

    // CreateQueue is idempotent — safe to call every run
    var queueUrl = await _sqsClient.CreateQueueAsync(queueName);
    var dlqUrl = await _sqsClient.CreateQueueAsync(dlqName);

    // Set redrive policy (link DLQ)
    await SetRedrivePolicy(queueUrl, dlqUrl, maxReceiveCount: 3);

    // Ensure Lambda event source mapping exists
    await EnsureEventSourceMapping(queueUrl, _startWorkflowLambdaArn);

    // EventBridge rule targets THIS queue (not shared ips-post-queue)
    await CreateOrUpdateEventBridgeRule(job, queueUrl);
}

// 2. Disable queues for inactive jobs
foreach (var job in inactiveJobs)
{
    await DisableEventBridgeRule(job);
    await RemoveEventSourceMapping(job);
    // Queue remains — messages drain naturally
}
```

---

## 14. API Changes (Manual Post → Async)

Current API (sync): `POST /api/post/{jobId}/items/{itemIds}` → waits → returns result

New API (async): Same endpoint, different behavior based on `execution_target`:

```csharp
// IPS.AutoPost.Api/Controllers/PostController.cs
[HttpPost("{jobId}/items/{itemIds}")]
public async Task<IActionResult> Post(int jobId, string itemIds, [FromQuery] int userId = 0)
{
    var config = await _configRepo.GetByJobIdAsync(jobId, ct);

    if (config.ExecutionTarget == "ECS")
    {
        // EXISTING SYNC PATH (unchanged for ECS clients)
        var result = await _orchestrator.RunManualPostAsync(itemIds, userId, ct);
        return Ok(result);
    }

    // NEW ASYNC PATH (Lambda clients)
    var executionId = $"{config.ClientType}-{jobId}-{DateTime.UtcNow:yyyyMMddHHmmss}-manual";
    var queueName = $"ips-post-{config.ClientType.ToLower()}-{env}";

    // Send message to client's SQS queue
    await _sqsClient.SendMessageAsync(new SendMessageRequest
    {
        QueueUrl = DeriveQueueUrl(queueName),
        MessageBody = JsonSerializer.Serialize(new SqsMessagePayload
        {
            JobId = jobId,
            ClientType = config.ClientType,
            TriggerType = "Manual",
            ItemIds = itemIds,
            UserId = userId
        })
    });

    // Save execution record for polling
    await SaveExecutionRecord(executionId, config, itemIds);

    // Return 202 immediately
    return Accepted(new { executionId, status = "queued" });
}

// NEW: Status polling endpoint
[HttpGet("/api/status/{executionId}")]
public async Task<IActionResult> GetStatus(string executionId)
{
    var execution = await _executionRepo.GetByExecutionIdAsync(executionId);
    if (execution == null) return NotFound();

    return Ok(new
    {
        executionId = execution.ExecutionId,
        status = execution.Status,            // QUEUED, PREPARING, PROCESSING, COMPLETED, FAILED
        totalItems = execution.TotalItems,
        itemsSuccess = execution.ItemsSuccess,
        itemsFailed = execution.ItemsFailed,
        filesGenerated = execution.FilesGenerated,
        startTime = execution.StartTime,
        endTime = execution.EndTime
    });
}
```

---

## 15. RDS Proxy Configuration

**Required for PER_ITEM mode** where 50 concurrent Lambdas each open a DB connection.

```yaml
# CloudFormation
RdsProxy:
  Type: AWS::RDS::DBProxy
  Properties:
    DBProxyName: !Sub "ips-autopost-proxy-${Environment}"
    EngineFamily: SQLSERVER
    Auth:
      - AuthScheme: SECRETS
        SecretArn: !Ref WorkflowDbSecret
        IAMAuth: REQUIRED
    RoleArn: !GetAtt RdsProxyRole.Arn
    VpcSubnetIds:
      - !Ref PrivateSubnetA
      - !Ref PrivateSubnetB
    VpcSecurityGroupIds:
      - !Ref LambdaSecurityGroup
    IdleClientTimeout: 600     # Close idle connections after 10 min
    MaxConnectionsPercent: 80  # Use up to 80% of RDS max connections
    MaxIdleConnectionsPercent: 20
```

**Lambda connection string change:**
```
// Instead of direct RDS endpoint:
Server=ips-rds-database-1.cmrmduasa2gk.us-east-1.rds.amazonaws.com

// Use RDS Proxy endpoint:
Server=ips-autopost-proxy-prod.proxy-cmrmduasa2gk.us-east-1.rds.amazonaws.com
```

**For BATCH_FILE mode:** RDS Proxy is still beneficial (20 concurrent connections within one Lambda) but not strictly mandatory since it's only 1 Lambda instance.

---

## 16. CloudFormation Resources

New resources needed in `infra/cloudformation/stepfunctions.yaml`:

```yaml
Resources:
  # State Machine
  AutoPostStateMachine:
    Type: AWS::StepFunctions::StateMachine
    Properties:
      StateMachineName: !Sub "ips-autopost-${Environment}"
      DefinitionString: ... (ASL JSON from Section 11)
      RoleArn: !GetAtt StepFunctionsRole.Arn

  # Lambda Functions (6 total)
  StartWorkflowFunction:
    Type: AWS::Lambda::Function
    Properties:
      FunctionName: !Sub "ips-autopost-start-workflow-${Environment}"
      Runtime: dotnet10
      MemorySize: 256
      Timeout: 30
      Handler: IPS.AutoPost.Lambda.StartWorkflow::Function::FunctionHandler

  PrepareFunction:
    Type: AWS::Lambda::Function
    Properties:
      FunctionName: !Sub "ips-autopost-prepare-${Environment}"
      Runtime: dotnet10
      MemorySize: 512
      Timeout: 120

  ItemProcessorFunction:
    Type: AWS::Lambda::Function
    Properties:
      FunctionName: !Sub "ips-autopost-item-processor-${Environment}"
      Runtime: dotnet10
      MemorySize: 512
      Timeout: 60
      # ReservedConcurrentExecutions: 200 (prevent account limit exhaustion)

  BatchProcessorFunction:
    Type: AWS::Lambda::Function
    Properties:
      FunctionName: !Sub "ips-autopost-batch-processor-${Environment}"
      Runtime: dotnet10
      MemorySize: 2048
      Timeout: 300

  AggregateFunction:
    Type: AWS::Lambda::Function
    Properties:
      FunctionName: !Sub "ips-autopost-aggregate-${Environment}"
      Runtime: dotnet10
      MemorySize: 512
      Timeout: 60

  NotifyFunction:
    Type: AWS::Lambda::Function
    Properties:
      FunctionName: !Sub "ips-autopost-notify-${Environment}"
      Runtime: dotnet10
      MemorySize: 256
      Timeout: 30

  # SNS Topics
  PostNotificationTopic:
    Type: AWS::SNS::Topic
    Properties:
      TopicName: !Sub "ips-post-notifications-${Environment}"

  # S3 Temp Bucket
  TempBucket:
    Type: AWS::S3::Bucket
    Properties:
      BucketName: !Sub "ips-autopost-temp-${Environment}"
      LifecycleConfiguration:
        Rules:
          - Id: AutoDeleteAfter24Hours
            Status: Enabled
            ExpirationInDays: 1

  # RDS Proxy (see Section 15)
  # Per-client SQS queues are created dynamically by Scheduler Lambda (not in CF)
```

---

## 17. Migration Strategy (ECS → Lambda)

### Phase 1: Both Paths Running (Side-by-Side)

```
generic_job_configuration:
  execution_target = 'ECS'   → existing PostWorker handles it (unchanged)
  execution_target = 'LAMBDA' → new Step Functions path handles it

Step 1: Deploy all Lambda functions + State Machine + RDS Proxy
Step 2: All clients remain execution_target = 'ECS' (no behavior change)
Step 3: Flip InvitedClub to execution_target = 'LAMBDA' (one client only)
Step 4: Monitor for 2 weeks (compare success rates, duration, errors)
Step 5: If issues → flip back to 'ECS' instantly (one DB update, no deployment)
Step 6: If stable → flip Sevita to 'LAMBDA'
Step 7: Repeat for each client
```

### Rollback (30-second recovery)

```sql
-- Instant rollback for any client:
UPDATE generic_job_configuration SET execution_target = 'ECS' WHERE client_type = 'INVITEDCLUB';
-- Next scheduled run goes through ECS PostWorker. No deployment needed.
```

### What Changes in ECS PostWorker (Nothing)

The ECS PostWorker remains unchanged. It still:
- Polls SQS (now: shared `ips-post-queue` for ECS-target clients)
- Uses MediatR → AutoPostOrchestrator → Plugin
- Works exactly as before

The Start Workflow Lambda checks `execution_target`:
- If `'LAMBDA'` → starts Step Functions
- If `'ECS'` → forwards message to shared `ips-post-queue` (ECS picks it up)

---

## 18. Error Handling and Retry Logic

### Per-Item Errors (PER_ITEM Path)

```json
// In Map state → ItemProcessor has Retry + Catch:
"Retry": [
  {
    "ErrorEquals": ["ERP503Error", "ERP429Error"],
    "IntervalSeconds": 5,
    "MaxAttempts": 2,
    "BackoffRate": 2.0
  }
],
"Catch": [
  {
    "ErrorEquals": ["States.ALL"],
    "ResultPath": "$.error",
    "Next": "ItemFailed"
  }
]
```

**Behavior:**
- ERP returns 503/429 → Step Functions retries that ONE item (not the whole batch)
- After 2 retries → catches error → marks item as failed → continues other items
- Item Processor Lambda clears PostInProcess in `finally` (even on retry/failure)

### Batch File Errors (BATCH_FILE Path)

```json
// BatchFileProcessor has a Catch but NO Retry (re-running generates duplicate files):
"Catch": [
  {
    "ErrorEquals": ["States.ALL"],
    "Next": "ErrorHandler"
  }
]
```

**Behavior:**
- If batch Lambda fails mid-way → ErrorHandler marks execution FAILED
- Items that were already routed remain routed (routing is per-item, not transactional)
- Items not yet routed remain in source queue (PostInProcess=0)
- Admin can trigger a re-run (manual post) — only unprocessed items will be picked up

### State Machine Level Error Handler

```json
// Catch-all at the top level:
"ErrorHandler": {
  "Type": "Task",
  "Resource": "arn:aws:lambda:...:ips-autopost-aggregate-{env}",
  "Parameters": {
    "executionId.$": "$.prepare.executionId",
    "status": "FAILED",
    "error.$": "$.error"
  },
  "End": true
}
```

### Lambda Timeout Handling

| Lambda | Timeout | What happens on timeout |
|---|---|---|
| StartWorkflow | 30s | SQS message NOT deleted → retried by SQS (visibility timeout) |
| Prepare | 2min | SF execution fails → ErrorHandler → email alert |
| ItemProcessor | 60s | Map catches timeout → marks item failed → continues others |
| BatchProcessor | 5min | SF catches timeout → ErrorHandler → email alert |
| Aggregate | 60s | SF execution fails → email alert (but items already processed) |
| Notify | 30s | SF execution fails → non-critical (items are already done) |

### DLQ Handling (Per-Client)

```
If StartWorkflow Lambda fails 3 times for the same SQS message:
  → Message moves to ips-post-{clientType}-dlq-{env}
  → CloudWatch alarm fires
  → SNS sends alert email to ops team
  → Admin investigates and can:
    1. Fix the issue, redrive from DLQ
    2. Move message back to main queue manually
```

---

## Summary: Complete Data Flow (PER_ITEM Example)

```
1. EventBridge fires at 08:00 for InvitedClub (Job 1001)
2. Drops message into: ips-post-invitedclub-prod
3. Lambda event source mapping triggers StartWorkflow Lambda
4. StartWorkflow: validates message, starts SF execution "INVITEDCLUB-1001-20260701-080000"
5. SF State 1 (Prepare): loads config, fetches 5000 pending ItemIds, caches config to S3
6. SF Choice: execution_mode = PER_ITEM → go to Map state
7. SF State 2A (Map): launches 50 Item Processor Lambdas simultaneously
   - Each Lambda: loads config from S3, loads 1 workitem from DB, calls Oracle Fusion (3 APIs),
     routes item, writes history, clears PostInProcess
   - Takes 7 seconds per item, 50 parallel = 100 items every 7 seconds
   - 5000 items ÷ 50 concurrent × 7s = ~12 minutes total
8. Map state AUTOMATICALLY waits for all 5000 to complete
9. Map state collects 5000 ItemProcessorResult objects into array
10. SF State 3 (Aggregate): receives the array, counts 4800 success + 200 failed,
    writes execution_history, publishes CloudWatch metrics, sends image-failure email
11. SF State 4 (Notify): publishes SNS email "InvitedClub: 5000 processed, 4800 success"
12. State machine SUCCEEDS. Total wall-clock: ~13 minutes.

vs CURRENT (sequential on ECS): 5000 × 7s = 35,000s = 9.7 hours
Speedup: 9.7 hours → 13 minutes = 45× faster
```

---

## Summary: Complete Data Flow (BATCH_FILE Example)

```
1. EventBridge fires at 09:00 for Northstar (Job 3001)
2. Drops message into: ips-post-northstar-prod
3. StartWorkflow Lambda: starts SF execution "NORTHSTAR-3001-20260701-090000"
4. SF State 1 (Prepare): loads config, fetches 2000 ItemIds,
   caches config.json to S3 (5KB). Does NOT fetch batch data — that's the Batch Lambda's job.
5. SF Choice: execution_mode = BATCH_FILE → go to BatchFileProcessor
6. SF State 2B (BatchProcessor): ONE Lambda invocation
   - Reads config.json from S3 (50ms)
   - Queries DB directly: GetHeaderAndDetailDataByJobID → full DataSet (~500ms)
   - Groups by CompanyCode: CompanyA (800 items), CompanyB (1200 items)
   - Generates CompanyA_20260701.csv and CompanyB_20260701.csv
   - Writes both files to S3: s3://ips-output-files/northstar/
   - Runs duplicate check: 50 items flagged → routes to SuspectedDuplicatesQueue
   - Routes remaining 1950 items to SuccessQueue (20 concurrent DB calls, ~10s)
   - Total time: ~15 seconds
7. SF State 3 (Aggregate): 1950 success, 50 duplicates, writes history, metrics
8. SF State 4 (Notify): "Northstar: 2 files generated, 1950 success, 50 duplicates"
9. State machine SUCCEEDS. Total wall-clock: ~25 seconds.

vs CURRENT (sequential): Same logic, similar speed (file gen is already fast).
Benefit: unified monitoring, SNS notifications, per-client queue isolation.
```
