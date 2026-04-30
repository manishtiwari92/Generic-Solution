# Design Document — IPS.AutoPost Platform
### .NET Core 10 | AWS ECS Fargate | Plugin Architecture | Multi-Client

> **Feature:** generic-autopost-platform
> **Date:** April 29, 2026
> **Based on:** requirements.md v1.0 | Generic_AutoPost_API_Implementation.md | InvitedClub + Sevita source analysis

---

## Table of Contents

1. [Solution Structure](#1-solution-structure)
2. [Core Interfaces and Contracts](#2-core-interfaces-and-contracts)
3. [GenericJobConfig Model](#3-genericjobconfig-model)
4. [AutoPostOrchestrator — Core Engine](#4-autopostorchestrator--core-engine)
5. [Database Schema — All Generic Tables](#5-database-schema--all-generic-tables)
6. [InvitedClubPlugin Design](#6-invitedclubplugin-design)
7. [SevitaPlugin Design](#7-sevitaplugin-design)
8. [Shared Infrastructure — SqlHelper and Services](#8-shared-infrastructure--sqlhelper-and-services)
9. [AWS Architecture — Three CloudFormation Stacks](#9-aws-architecture--three-cloudformation-stacks)
10. [Dockerfile — Multi-Stage Build](#10-dockerfile--multi-stage-build)
11. [GitHub Actions CI/CD Pipeline](#11-github-actions-cicd-pipeline)
12. [Data Flow Diagrams](#12-data-flow-diagrams)
13. [Key Design Decisions](#13-key-design-decisions)
14. [Correctness Properties — Test Implementation](#14-correctness-properties--test-implementation)

---

## 1. Solution Structure

> **Updated:** This structure reflects the patterns adopted from `GenericMissingInvoicesProcess` — MediatR Commands/Handlers, Pipeline Behaviors, CorrelationIdService, CloudWatchMetricsService, DynamicRecord, EF Core Migrations, and `Infrastructure/` folder for AWS integrations.

```
IPS.AutoPost.Platform.sln
|
+-- src/
|   |
|   +-- IPS.AutoPost.Core/                        # Generic engine — never changes for new clients
|   |   +-- IPS.AutoPost.Core.csproj
|   |   +-- Commands/                             # MediatR commands (CQRS)
|   |   |   +-- ExecutePostCommand.cs             # Sent by PostWorker per SQS message
|   |   |   +-- ExecuteFeedCommand.cs             # Sent by FeedWorker per SQS message
|   |   +-- Handlers/                             # MediatR handlers
|   |   |   +-- ExecutePostHandler.cs             # Handles ExecutePostCommand
|   |   |   +-- ExecuteFeedHandler.cs             # Handles ExecuteFeedCommand
|   |   +-- Behaviors/                            # MediatR pipeline behaviors
|   |   |   +-- LoggingBehavior.cs                # Logs every command start/end with CorrelationId
|   |   |   +-- ValidationBehavior.cs             # Runs FluentValidation before handler
|   |   +-- Engine/
|   |   |   +-- AutoPostOrchestrator.cs           # Main loop: config -> workitems -> plugin -> history
|   |   |   +-- SchedulerService.cs               # IsExecuteFileCreation (30-min window logic)
|   |   |   +-- PluginRegistry.cs                 # Maps client_type -> IClientPlugin
|   |   +-- Interfaces/
|   |   |   +-- IClientPlugin.cs                  # Plugin contract
|   |   |   +-- IConfigurationRepository.cs       # Load generic_job_configuration
|   |   |   +-- IWorkitemRepository.cs            # GetWorkitemData, GetWorkitemDataByItemId
|   |   |   +-- IRoutingRepository.cs             # WORKITEM_ROUTE, UpdateInProcessFlag
|   |   |   +-- IAuditRepository.cs               # GENERALLOG_INSERT, UpdateHistory
|   |   |   +-- IScheduleRepository.cs            # GetExecutionSchedule
|   |   |   +-- ICloudWatchMetricsService.cs      # Granular per-client metrics interface
|   |   |   +-- ICorrelationIdService.cs          # AsyncLocal correlation ID per SQS message
|   |   +-- Models/
|   |   |   +-- GenericJobConfig.cs               # Unified config model
|   |   |   +-- WorkitemData.cs
|   |   |   +-- DynamicRecord.cs                  # Schema-agnostic data model (key/value dict)
|   |   |   +-- PostContext.cs
|   |   |   +-- PostBatchResult.cs
|   |   |   +-- PostItemResult.cs
|   |   |   +-- FeedContext.cs
|   |   |   +-- FeedResult.cs
|   |   |   +-- ScheduleConfig.cs
|   |   |   +-- EdenredApiUrlConfig.cs
|   |   |   +-- GenericPostHistory.cs
|   |   |   +-- GenericExecutionHistory.cs
|   |   |   +-- SqsMessagePayload.cs
|   |   +-- Services/
|   |   |   +-- ConfigurationService.cs           # Secrets Manager + DB config loading
|   |   |   +-- S3ImageService.cs                 # GetBase64ImageFromS3Async wrapper
|   |   |   +-- EmailService.cs                   # SMTP email (shared)
|   |   |   +-- CloudWatchMetricsService.cs       # Granular per-client CloudWatch metrics
|   |   |   +-- CorrelationIdService.cs           # AsyncLocal correlation ID per SQS message
|   |   +-- DataAccess/
|   |   |   +-- SqlHelper.cs                      # ONE shared async SqlHelper
|   |   |   +-- ConfigurationRepository.cs
|   |   |   +-- WorkitemRepository.cs
|   |   |   +-- RoutingRepository.cs
|   |   |   +-- AuditRepository.cs
|   |   |   +-- ScheduleRepository.cs
|   |   +-- Migrations/                           # EF Core migrations for generic tables ONLY
|   |   |   +-- AutoPostDatabaseContext.cs        # EF Core DbContext (generic tables only)
|   |   |   +-- 20260430_InitialGenericTables.cs  # Creates all 10 generic tables
|   |   |   +-- DatabaseContextModelSnapshot.cs
|   |   +-- Infrastructure/                       # AWS and external system integrations
|   |   |   +-- SecretsManagerConfigurationProvider.cs  # Config-path Secrets Manager pattern
|   |   +-- Exceptions/
|   |   |   +-- PluginNotFoundException.cs
|   |   +-- Extensions/
|   |       +-- DataTableExtensions.cs            # GenerateHtmlTable(), ToDataTable<T>()
|   |       +-- ParserExtensions.cs               # ConvertDataTable<T>()
|   |
|   +-- IPS.AutoPost.Plugins/                     # All client-specific logic
|   |   +-- IPS.AutoPost.Plugins.csproj
|   |   +-- InvitedClub/
|   |   |   +-- InvitedClubPlugin.cs              # IClientPlugin implementation
|   |   |   +-- InvitedClubPostStrategy.cs        # 3-step Oracle Fusion post
|   |   |   +-- InvitedClubFeedStrategy.cs        # Supplier/Address/Site/COA download
|   |   |   +-- InvitedClubRetryService.cs        # RetryPostImages
|   |   |   +-- Models/
|   |   |   |   +-- InvitedClubConfig.cs          # client_config_json deserialization
|   |   |   |   +-- InvoiceRequest.cs
|   |   |   |   +-- AttachmentRequest.cs
|   |   |   |   +-- InvoiceCalculateTaxRequest.cs
|   |   |   |   +-- InvoiceResponse.cs
|   |   |   |   +-- AttachmentResponse.cs
|   |   |   |   +-- InvoiceCalculateTaxResponse.cs
|   |   |   |   +-- SupplierResponse.cs
|   |   |   |   +-- SupplierAddressResponse.cs
|   |   |   |   +-- SupplierSiteResponse.cs
|   |   |   |   +-- COAResponse.cs
|   |   |   |   +-- FailedImagesData.cs
|   |   |   |   +-- PostHistory.cs
|   |   |   +-- Constants/
|   |   |       +-- InvitedClubConstants.cs       # API URIs, table names
|   |   +-- Sevita/
|   |   |   +-- SevitaPlugin.cs                   # IClientPlugin implementation
|   |   |   +-- SevitaPostStrategy.cs             # OAuth2 + validation + line grouping
|   |   |   +-- SevitaTokenService.cs             # OAuth2 token caching
|   |   |   +-- SevitaValidationService.cs        # PO/Non-PO validation
|   |   |   +-- Models/
|   |   |   |   +-- SevitaConfig.cs               # client_config_json deserialization
|   |   |   |   +-- InvoiceRequest.cs             # vendorId, lineItems[], attachments[]
|   |   |   |   +-- InvoiceResponse.cs
|   |   |   |   +-- ValidIds.cs
|   |   |   |   +-- PostHistory.cs
|   |   |   |   +-- PostFailedRecord.cs
|   |   |   +-- Constants/
|   |   |       +-- SevitaConstants.cs
|   |   +-- PluginRegistration.cs                 # Registers all plugins at startup
|   |
|   +-- IPS.AutoPost.Host.FeedWorker/             # ECS Fargate Feed Worker
|   |   +-- IPS.AutoPost.Host.FeedWorker.csproj
|   |   +-- Program.cs                            # Host builder + DI wiring + AddSecretsManagerAsync
|   |   +-- FeedWorker.cs                         # BackgroundService: MaxNumberOfMessages=10, scoped DI per message
|   |   +-- appsettings.json
|   |
|   +-- IPS.AutoPost.Host.PostWorker/             # ECS Fargate Post Worker
|   |   +-- IPS.AutoPost.Host.PostWorker.csproj
|   |   +-- Program.cs
|   |   +-- PostWorker.cs                         # BackgroundService: MaxNumberOfMessages=10, scoped DI per message
|   |   +-- appsettings.json
|   |
|   +-- IPS.AutoPost.Api/                         # ASP.NET Core Web API (manual trigger)
|   |   +-- IPS.AutoPost.Api.csproj
|   |   +-- Program.cs
|   |   +-- Controllers/
|   |   |   +-- PostController.cs                 # POST /api/post/{jobId}/items/{itemIds}
|   |   |   +-- FeedController.cs                 # POST /api/feed/{jobId}
|   |   |   +-- StatusController.cs               # GET /api/status/{executionId}
|   |   +-- appsettings.json
|   |
|   +-- IPS.AutoPost.Scheduler/                   # AWS Lambda — EventBridge sync ONLY
|       +-- IPS.AutoPost.Scheduler.csproj
|       +-- Function.cs                           # Lambda handler
|       +-- SchedulerSyncService.cs               # Reads DB, creates/updates EventBridge rules
|
+-- tests/
|   +-- IPS.AutoPost.Core.Tests/                  # xUnit v3 + EF Core InMemory
|   |   +-- Handlers/
|   |   |   +-- ExecutePostHandlerTests.cs
|   |   |   +-- ExecuteFeedHandlerTests.cs
|   |   +-- Behaviors/
|   |   |   +-- LoggingBehaviorTests.cs
|   |   |   +-- ValidationBehaviorTests.cs
|   |   +-- Services/
|   |   |   +-- CloudWatchMetricsServiceTests.cs
|   |   |   +-- CorrelationIdServiceTests.cs
|   |   +-- Engine/
|   |   |   +-- AutoPostOrchestratorTests.cs
|   |   |   +-- SchedulerServiceTests.cs
|   |   |   +-- PluginRegistryTests.cs
|   |   +-- DataAccess/
|   |       +-- SqlHelperTests.cs
|   |       +-- WorkitemRepositoryTests.cs
|   +-- IPS.AutoPost.Plugins.Tests/               # xUnit v3 + WireMock.Net + EF Core InMemory
|       +-- InvitedClub/
|       |   +-- InvitedClubPostStrategyTests.cs
|       |   +-- InvitedClubFeedStrategyTests.cs
|       |   +-- InvitedClubRetryServiceTests.cs
|       |   +-- InvitedClubPluginTests.cs
|       +-- Sevita/
|       |   +-- SevitaPostStrategyTests.cs
|       |   +-- SevitaValidationServiceTests.cs
|       |   +-- SevitaTokenServiceTests.cs
|       |   +-- SevitaPluginTests.cs
|       +-- PropertyBased/
|       |   +-- PostInProcessInvariantTests.cs
|       |   +-- RoutingInvariantTests.cs
|       |   +-- HistoryCompletenessTests.cs
|       |   +-- UseTaxRoundTripTests.cs
|       |   +-- FeedIdempotenceTests.cs
|       |   +-- IncrementalFeedSubsetTests.cs
|       |   +-- PaginationCompletenessTests.cs
|       |   +-- ErrorConditionRoutingTests.cs
|       |   +-- RetryIdempotenceTests.cs
|       |   +-- SqsDeliveryGuaranteeTests.cs
|       +-- Integration/
|           +-- InvitedClubIntegrationTests.cs    # EF Core InMemory + WireMock.Net
|           +-- SevitaIntegrationTests.cs         # EF Core InMemory + WireMock.Net
|
+-- infra/
|   +-- cloudformation/
|   |   +-- infrastructure.yaml
|   |   +-- application.yaml
|   |   +-- monitoring.yaml
|   +-- docker/
|       +-- Dockerfile.FeedWorker
|       +-- Dockerfile.PostWorker
|
+-- .github/
    +-- workflows/
        +-- deploy.yml
```

---

## 2. Core Interfaces and Contracts

### 2.1 IClientPlugin Interface

```csharp
// IPS.AutoPost.Core/Interfaces/IClientPlugin.cs
namespace IPS.AutoPost.Core.Interfaces;

public interface IClientPlugin
{
    /// <summary>Unique identifier matching client_type in generic_job_configuration.</summary>
    string ClientType { get; }

    /// <summary>
    /// Called ONCE before the workitem loop. Use for batch-level pre-loading.
    /// Example: Sevita loads ValidIds from Sevita_Supplier_SiteInformation_Feed.
    /// Default: no-op.
    /// </summary>
    Task OnBeforePostAsync(GenericJobConfig config, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>
    /// Processes all workitems in a batch. Called by AutoPostOrchestrator.
    /// The plugin receives the full header+detail DataSet per workitem via PostContext.
    /// </summary>
    Task<PostBatchResult> ExecutePostAsync(
        GenericJobConfig config,
        PostContext context,
        CancellationToken ct);

    /// <summary>
    /// Downloads feed data (vendors, COA, etc.).
    /// Default: returns FeedResult.NotApplicable() — Core_Engine skips feed processing.
    /// Override for clients with feed downloads (InvitedClub).
    /// </summary>
    Task<FeedResult> ExecuteFeedDownloadAsync(
        GenericJobConfig config,
        FeedContext context,
        CancellationToken ct)
        => Task.FromResult(FeedResult.NotApplicable());

    /// <summary>
    /// Clears the PostInProcess flag after processing a workitem.
    /// Default: executes UPDATE {header_table} SET PostInProcess=0 WHERE UID=@uid.
    /// Override for clients using a stored procedure (Sevita: UpdateSevitaHeaderPostFields).
    /// </summary>
    Task ClearPostInProcessAsync(
        long itemId,
        GenericJobConfig config,
        IRoutingRepository routingRepo,
        CancellationToken ct)
        => routingRepo.ClearPostInProcessAsync(itemId, config.HeaderTable, ct);
}
```

### 2.2 PostContext and PostBatchResult

```csharp
// IPS.AutoPost.Core/Models/PostContext.cs
public class PostContext
{
    public string TriggerType { get; init; } = "Scheduled"; // "Scheduled" | "Manual"
    public string ItemIds { get; init; } = string.Empty;    // empty = auto, non-empty = manual
    public int UserId { get; init; }
    public bool ProcessManually => !string.IsNullOrEmpty(ItemIds) || TriggerType == "Manual";
    public EdenredApiUrlConfig S3Config { get; init; } = new();
    public CancellationToken CancellationToken { get; init; }
}

// IPS.AutoPost.Core/Models/PostBatchResult.cs
public class PostBatchResult
{
    public int RecordsProcessed { get; set; }
    public int RecordsSuccess { get; set; }
    public int RecordsFailed { get; set; }
    public List<PostItemResult> ItemResults { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

// IPS.AutoPost.Core/Models/PostItemResult.cs
public class PostItemResult
{
    public long ItemId { get; set; }
    public bool IsSuccess { get; set; }
    public int ResponseCode { get; set; }
    public string ResponseMessage { get; set; } = string.Empty;
    public long DestinationQueue { get; set; }
}
```

### 2.3 FeedResult

```csharp
// IPS.AutoPost.Core/Models/FeedResult.cs
public class FeedResult
{
    public bool IsApplicable { get; private set; }
    public bool Success { get; private set; }
    public int RecordsDownloaded { get; set; }
    public string? ErrorMessage { get; set; }

    public static FeedResult NotApplicable() => new() { IsApplicable = false };
    public static FeedResult Succeeded(int records) => new() { IsApplicable = true, Success = true, RecordsDownloaded = records };
    public static FeedResult Failed(string error) => new() { IsApplicable = true, Success = false, ErrorMessage = error };
}
```

### 2.4 PluginRegistry

```csharp
// IPS.AutoPost.Core/Engine/PluginRegistry.cs
public class PluginRegistry
{
    private readonly Dictionary<string, IClientPlugin> _plugins = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IClientPlugin plugin)
        => _plugins[plugin.ClientType] = plugin;

    public IClientPlugin Resolve(string clientType)
    {
        if (_plugins.TryGetValue(clientType, out var plugin))
            return plugin;
        throw new PluginNotFoundException($"No plugin registered for client_type: '{clientType}'");
    }

    public bool IsRegistered(string clientType)
        => _plugins.ContainsKey(clientType);
}

public class PluginNotFoundException : Exception
{
    public PluginNotFoundException(string message) : base(message) { }
}
```

---

## 3. GenericJobConfig Model

```csharp
// IPS.AutoPost.Core/Models/GenericJobConfig.cs
using System.Text.Json;

public class GenericJobConfig
{
    // Identity
    public int Id { get; set; }
    public string ClientType { get; set; } = string.Empty;
    public int JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public int DefaultUserId { get; set; } = 100;
    public bool IsActive { get; set; }

    // Queue IDs
    public string SourceQueueId { get; set; } = string.Empty;  // comma-separated
    public int SuccessQueueId { get; set; }
    public int PrimaryFailQueueId { get; set; }
    public int? SecondaryFailQueueId { get; set; }
    public int? QuestionQueueId { get; set; }
    public int? TerminatedQueueId { get; set; }

    // Table References
    public string HeaderTable { get; set; } = string.Empty;
    public string DetailTable { get; set; } = string.Empty;
    public string DetailUidColumn { get; set; } = string.Empty;
    public string HistoryTable { get; set; } = string.Empty;
    public string DbConnectionString { get; set; } = string.Empty;

    // Authentication
    public string AuthType { get; set; } = "BASIC"; // BASIC | OAUTH2 | APIKEY | NONE
    public string AuthUsername { get; set; } = string.Empty;
    public string AuthPassword { get; set; } = string.Empty;

    // Post Service
    public string PostServiceUrl { get; set; } = string.Empty;

    // Scheduling
    public bool AllowAutoPost { get; set; }
    public bool DownloadFeed { get; set; }
    public DateTime LastPostTime { get; set; }
    public DateTime LastDownloadTime { get; set; }

    // Paths
    public string OutputFilePath { get; set; } = string.Empty;
    public string FeedDownloadPath { get; set; } = string.Empty;
    public string ImageParentPath { get; set; } = string.Empty;
    public string NewUiImageParentPath { get; set; } = string.Empty;
    public bool IsLegacyJob { get; set; }

    // Client-specific extras (JSON blob)
    public string ClientConfigJson { get; set; } = string.Empty;

    /// <summary>
    /// Deserializes client_config_json into a typed client-specific config object.
    /// Example: config.GetClientConfig&lt;InvitedClubConfig&gt;()
    ///          config.GetClientConfig&lt;SevitaConfig&gt;()
    /// </summary>
    public T GetClientConfig<T>() where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(ClientConfigJson))
            return new T();
        return JsonSerializer.Deserialize<T>(ClientConfigJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new T();
    }
}
```

---

## 4. AutoPostOrchestrator — Core Engine

```csharp
// IPS.AutoPost.Core/Engine/AutoPostOrchestrator.cs
public class AutoPostOrchestrator
{
    private readonly IConfigurationRepository _configRepo;
    private readonly IWorkitemRepository _workitemRepo;
    private readonly IRoutingRepository _routingRepo;
    private readonly IAuditRepository _auditRepo;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly PluginRegistry _pluginRegistry;
    private readonly SchedulerService _scheduler;
    private readonly ILogger<AutoPostOrchestrator> _logger;

    // -----------------------------------------------------------------------
    // SCHEDULED POST (called by PostWorker when SQS message arrives)
    // -----------------------------------------------------------------------
    public async Task RunScheduledPostAsync(
        int jobId, string clientType, CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        var config = await _configRepo.GetByJobIdAsync(jobId, ct);
        if (config == null || !config.IsActive) return;

        var plugin = _pluginRegistry.Resolve(clientType);
        var schedules = await _scheduleRepo.GetSchedulesAsync(config.Id, config.JobId, ct);

        // Schedule window check (for HH:mm format schedules)
        if (!_scheduler.ShouldExecute(config.LastPostTime, schedules)) return;
        if (!config.AllowAutoPost) return;

        _logger.LogInformation("[{ClientType}] Job {JobId} - PostData started (Scheduled)", clientType, jobId);

        // Batch-level pre-loading hook (e.g. Sevita loads ValidIds)
        await plugin.OnBeforePostAsync(config, ct);

        var context = new PostContext
        {
            TriggerType = "Scheduled",
            UserId = config.DefaultUserId,
            S3Config = await _configRepo.GetEdenredApiUrlConfigAsync(ct)
        };

        var result = await ExecutePostBatchAsync(plugin, config, context, startTime, ct);

        await _configRepo.UpdateLastPostTimeAsync(config.Id, ct);
        _logger.LogInformation("[{ClientType}] Job {JobId} - PostData completed: {Success} success, {Failed} failed",
            clientType, jobId, result.RecordsSuccess, result.RecordsFailed);
    }

    // -----------------------------------------------------------------------
    // MANUAL POST (called by PostController directly)
    // -----------------------------------------------------------------------
    public async Task<PostBatchResult> RunManualPostAsync(
        string itemIds, int userId, CancellationToken ct)
    {
        // Resolve config by StatusId of first workitem
        var workitemDs = await _workitemRepo.GetWorkitemsByItemIdsAsync(itemIds, ct);
        if (workitemDs == null || workitemDs.Tables[0].Rows.Count == 0)
            return new PostBatchResult { ErrorMessage = "No workitems found." };

        var statusId = Convert.ToInt32(workitemDs.Tables[0].Rows[0]["StatusID"]);
        var config = await _configRepo.GetBySourceQueueIdAsync(statusId, ct);
        if (config == null)
            return new PostBatchResult { ResponseCode = -1, ErrorMessage = "Missing Configuration." };

        var plugin = _pluginRegistry.Resolve(config.ClientType);
        await plugin.OnBeforePostAsync(config, ct);

        var context = new PostContext
        {
            TriggerType = "Manual",
            ItemIds = itemIds,
            UserId = userId,
            S3Config = await _configRepo.GetEdenredApiUrlConfigAsync(ct)
        };

        return await ExecutePostBatchAsync(plugin, config, context, DateTime.UtcNow, ct);
    }

    // -----------------------------------------------------------------------
    // CORE BATCH EXECUTION (shared by scheduled and manual)
    // -----------------------------------------------------------------------
    private async Task<PostBatchResult> ExecutePostBatchAsync(
        IClientPlugin plugin,
        GenericJobConfig config,
        PostContext context,
        DateTime startTime,
        CancellationToken ct)
    {
        var executionHistory = new GenericExecutionHistory
        {
            JobConfigId = config.Id,
            ClientType = config.ClientType,
            JobId = config.JobId,
            ExecutionType = "POST",
            TriggerType = context.TriggerType,
            StartTime = startTime
        };

        PostBatchResult result;
        try
        {
            // Set PostInProcess = 1 for all fetched workitems
            // (done inside plugin per-workitem before API call)

            result = await plugin.ExecutePostAsync(config, context, ct);

            executionHistory.Status = result.RecordsFailed == 0 ? "SUCCESS" : "PARTIAL_SUCCESS";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ClientType}] Job {JobId} - Unhandled exception in ExecutePostAsync",
                config.ClientType, config.JobId);
            result = new PostBatchResult { ErrorMessage = ex.Message };
            executionHistory.Status = "FAILED";
            executionHistory.ErrorDetails = ex.ToString();
        }
        finally
        {
            executionHistory.EndTime = DateTime.UtcNow;
            executionHistory.RecordsProcessed = result?.RecordsProcessed ?? 0;
            executionHistory.RecordsSucceeded = result?.RecordsSuccess ?? 0;
            executionHistory.RecordsFailed = result?.RecordsFailed ?? 0;
            await _auditRepo.SaveExecutionHistoryAsync(executionHistory, ct);
        }

        return result ?? new PostBatchResult();
    }

    // -----------------------------------------------------------------------
    // SCHEDULED FEED DOWNLOAD (called by FeedWorker when SQS message arrives)
    // -----------------------------------------------------------------------
    public async Task RunScheduledFeedAsync(
        int jobId, string clientType, CancellationToken ct)
    {
        var config = await _configRepo.GetByJobIdAsync(jobId, ct);
        if (config == null || !config.IsActive || !config.DownloadFeed) return;

        var plugin = _pluginRegistry.Resolve(clientType);
        var feedContext = new FeedContext
        {
            TriggerType = "Scheduled",
            S3Config = await _configRepo.GetEdenredApiUrlConfigAsync(ct)
        };

        var result = await plugin.ExecuteFeedDownloadAsync(config, feedContext, ct);

        if (result.IsApplicable && result.Success)
            await _configRepo.UpdateLastDownloadTimeAsync(config.Id, ct);
    }
}
```

---

## 4A. MediatR Commands, Handlers, and Pipeline Behaviors

### 4A.1 Commands

```csharp
// IPS.AutoPost.Core/Commands/ExecutePostCommand.cs
public class ExecutePostCommand : IRequest<PostBatchResult>
{
    public int JobId { get; set; }
    public string ClientType { get; set; } = string.Empty;
    public string TriggerType { get; set; } = "Scheduled";
    public string ItemIds { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string? Mode { get; set; }  // Optional: "DryRun", "UAT" — no redeployment needed
}

// IPS.AutoPost.Core/Commands/ExecuteFeedCommand.cs
public class ExecuteFeedCommand : IRequest<FeedResult>
{
    public int JobId { get; set; }
    public string ClientType { get; set; } = string.Empty;
    public string TriggerType { get; set; } = "Scheduled";
    public string? Mode { get; set; }
}
```

### 4A.2 Handlers

```csharp
// IPS.AutoPost.Core/Handlers/ExecutePostHandler.cs
public class ExecutePostHandler : IRequestHandler<ExecutePostCommand, PostBatchResult>
{
    private readonly AutoPostOrchestrator _orchestrator;

    public async Task<PostBatchResult> Handle(ExecutePostCommand request, CancellationToken ct)
    {
        // Handler delegates to orchestrator — keeps handler thin
        if (string.IsNullOrEmpty(request.ItemIds))
            return await _orchestrator.RunScheduledPostAsync(request.JobId, request.ClientType, ct);
        else
            return await _orchestrator.RunManualPostAsync(request.ItemIds, request.UserId, ct);
    }
}

// IPS.AutoPost.Core/Handlers/ExecuteFeedHandler.cs
public class ExecuteFeedHandler : IRequestHandler<ExecuteFeedCommand, FeedResult>
{
    private readonly AutoPostOrchestrator _orchestrator;

    public async Task<FeedResult> Handle(ExecuteFeedCommand request, CancellationToken ct)
        => await _orchestrator.RunScheduledFeedAsync(request.JobId, request.ClientType, ct);
}
```

### 4A.3 Pipeline Behaviors

```csharp
// IPS.AutoPost.Core/Behaviors/LoggingBehavior.cs
// Registered once — wraps EVERY command automatically
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly ICorrelationIdService _correlationService;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        var correlationId = _correlationService.GetOrCreateCorrelationId();
        _logger.LogInformation("[{CorrelationId}] Handling {RequestName}", correlationId, requestName);
        
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();
        
        _logger.LogInformation("[{CorrelationId}] Handled {RequestName} in {ElapsedMs}ms",
            correlationId, requestName, sw.ElapsedMilliseconds);
        return response;
    }
}

// IPS.AutoPost.Core/Behaviors/ValidationBehavior.cs
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!_validators.Any()) return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = _validators
            .SelectMany(v => v.Validate(context).Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Any())
            throw new ValidationException(failures);

        return await next();
    }
}
```

### 4A.4 DI Registration

```csharp
// ServiceCollectionExtensions.cs
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ExecutePostHandler).Assembly));

// Pipeline behaviors — registered in order (LoggingBehavior runs first, then ValidationBehavior)
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// FluentValidation validators
services.AddValidatorsFromAssembly(typeof(ExecutePostCommand).Assembly);
```

---

## 5. Database Schema — All Generic Tables

```sql
-- ============================================================
-- Connection: Server=ips-rds-database-1.cmrmduasa2gk.us-east-1.rds.amazonaws.com
--             Database=Workflow
--             User ID=IPSAppsUser
-- ============================================================

-- 5.1 generic_job_configuration
-- Replaces all 15+ post_to_xxx_configuration tables
CREATE TABLE generic_job_configuration (
    id                          INT IDENTITY(1,1) PRIMARY KEY,
    client_type                 VARCHAR(50)   NOT NULL,  -- 'INVITEDCLUB','SEVITA','MEDIA',...
    job_id                      INT           NOT NULL,
    job_name                    VARCHAR(100)  NOT NULL,
    default_user_id             INT           NOT NULL DEFAULT 100,
    is_active                   BIT           NOT NULL DEFAULT 1,

    -- Queue IDs
    source_queue_id             VARCHAR(500)  NOT NULL,  -- comma-separated for multi-queue
    success_queue_id            INT           NULL,
    primary_fail_queue_id       INT           NULL,
    secondary_fail_queue_id     INT           NULL,
    question_queue_id           INT           NULL,
    terminated_queue_id         INT           NULL,

    -- Table References
    header_table                VARCHAR(200)  NULL,
    detail_table                VARCHAR(200)  NULL,
    detail_uid_column           VARCHAR(100)  NULL,
    history_table               VARCHAR(200)  NULL,
    db_connection_string        NVARCHAR(500) NULL,

    -- Authentication
    auth_type                   VARCHAR(20)   NOT NULL DEFAULT 'BASIC',
    -- 'BASIC' | 'OAUTH2' | 'APIKEY' | 'NONE'
    auth_username               VARCHAR(200)  NULL,
    auth_password               VARCHAR(200)  NULL,

    -- Post Service
    post_service_url            VARCHAR(500)  NULL,

    -- Download Service
    download_service_url        VARCHAR(500)  NULL,
    download_auth_type          VARCHAR(20)   NULL,
    download_auth_username      VARCHAR(200)  NULL,
    download_auth_password      VARCHAR(200)  NULL,

    -- Scheduling
    last_post_time              DATETIME      NULL,
    last_download_time          DATETIME      NULL,
    allow_auto_post             BIT           NOT NULL DEFAULT 0,
    download_feed               BIT           NOT NULL DEFAULT 0,

    -- Paths
    output_file_path            NVARCHAR(500) NULL,
    feed_download_path          NVARCHAR(500) NULL,
    image_parent_path           NVARCHAR(500) NULL,
    new_ui_image_parent_path    NVARCHAR(500) NULL,
    is_legacy_job               BIT           NOT NULL DEFAULT 0,

    -- Client-specific JSON blob
    -- InvitedClub: {"ImagePostRetryLimit":3,"EdenredFailQueueId":500}
    -- Sevita:      {"is_PO_record":false,"post_json_path":"s3://bucket/json/","token_expiration_min":60}
    client_config_json          NVARCHAR(MAX) NULL,

    created_date                DATETIME      NOT NULL DEFAULT GETDATE(),
    modified_date               DATETIME      NULL,

    CONSTRAINT UQ_generic_job_config_client_job UNIQUE (client_type, job_id)
);

-- 5.2 generic_execution_schedule
-- Replaces all schedule tables. Supports HH:mm (30-min window) and cron expressions.
CREATE TABLE generic_execution_schedule (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    job_config_id       INT          NOT NULL REFERENCES generic_job_configuration(id),
    schedule_type       VARCHAR(20)  NOT NULL DEFAULT 'POST',  -- 'POST' | 'DOWNLOAD'
    execution_time      VARCHAR(10)  NULL,   -- HH:mm format, e.g. '08:00'
    cron_expression     VARCHAR(100) NULL,   -- EventBridge cron, e.g. 'cron(0 8 * * ? *)'
    last_execution_time DATETIME     NULL,
    is_active           BIT          NOT NULL DEFAULT 1,
    CONSTRAINT CHK_schedule_has_time CHECK (
        execution_time IS NOT NULL OR cron_expression IS NOT NULL
    )
);

-- 5.3 generic_feed_configuration
-- Feed source definitions per job
CREATE TABLE generic_feed_configuration (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    job_config_id       INT          NOT NULL REFERENCES generic_job_configuration(id),
    feed_name           VARCHAR(100) NOT NULL,
    feed_source_type    VARCHAR(20)  NOT NULL DEFAULT 'REST',
    -- 'REST' | 'FTP' | 'SFTP' | 'S3' | 'FILE'
    feed_url            VARCHAR(500) NULL,
    ftp_host            VARCHAR(200) NULL,
    ftp_port            INT          NULL DEFAULT 21,
    ftp_path            VARCHAR(500) NULL,
    ftp_file_pattern    VARCHAR(200) NULL,
    s3_bucket           VARCHAR(200) NULL,
    s3_key_prefix       VARCHAR(500) NULL,
    local_file_path     VARCHAR(500) NULL,
    file_format         VARCHAR(20)  NULL,   -- 'CSV' | 'TXT' | 'XLSX' | 'JSON' | 'XML'
    has_header          BIT          NOT NULL DEFAULT 1,
    delimiter           VARCHAR(5)   NULL DEFAULT ',',
    feed_table_name     VARCHAR(200) NULL,
    refresh_strategy    VARCHAR(20)  NOT NULL DEFAULT 'TRUNCATE',
    -- 'TRUNCATE' | 'DELETE_BY_KEY' | 'INCREMENTAL'
    key_column          VARCHAR(100) NULL,
    last_download_time  DATETIME     NULL,
    is_active           BIT          NOT NULL DEFAULT 1,
    feed_config_json    NVARCHAR(MAX) NULL,
    sequence_no         INT          NOT NULL DEFAULT 1
);

-- 5.4 generic_auth_configuration
-- Credentials per job (for clients with multiple credential sets, e.g. MDS)
CREATE TABLE generic_auth_configuration (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    job_config_id   INT          NOT NULL REFERENCES generic_job_configuration(id),
    auth_purpose    VARCHAR(20)  NOT NULL,  -- 'POST' | 'DOWNLOAD' | 'CREDS_BY_KEY'
    auth_key        VARCHAR(100) NULL,      -- e.g. company_code_length for MDS
    auth_type       VARCHAR(20)  NOT NULL,  -- 'BASIC' | 'OAUTH2' | 'APIKEY' | 'SOAP'
    username        VARCHAR(200) NULL,
    password        VARCHAR(200) NULL,
    api_key         VARCHAR(500) NULL,
    token_url       VARCHAR(500) NULL,
    secret_arn      VARCHAR(500) NULL,
    extra_json      NVARCHAR(MAX) NULL
);

-- 5.5 generic_queue_routing_rules
-- Configurable queue routing per result type
CREATE TABLE generic_queue_routing_rules (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    job_config_id   INT          NOT NULL REFERENCES generic_job_configuration(id),
    result_type     VARCHAR(50)  NOT NULL,
    -- 'SUCCESS' | 'FAIL_POST' | 'FAIL_IMAGE' | 'DUPLICATE' | 'QUESTION' | 'TERMINATED'
    queue_id        INT          NOT NULL,
    is_active       BIT          NOT NULL DEFAULT 1
);

-- 5.6 generic_post_history
-- Replaces all xxx_posted_records_history tables
CREATE TABLE generic_post_history (
    id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    client_type         VARCHAR(50)   NOT NULL,
    job_id              INT           NOT NULL,
    item_id             BIGINT        NOT NULL,
    step_name           VARCHAR(100)  NULL,
    -- 'InvoicePost' | 'AttachmentPost' | 'CalculateTax' | 'FileGeneration'
    post_request        NVARCHAR(MAX) NULL,
    post_response       NVARCHAR(MAX) NULL,
    post_date           DATETIME      NOT NULL DEFAULT GETDATE(),
    posted_by           INT           NOT NULL,
    manually_posted     BIT           NOT NULL DEFAULT 0,
    output_file_path    NVARCHAR(500) NULL,
    comment             NVARCHAR(MAX) NULL,
    INDEX IX_generic_post_history_item (item_id, job_id),
    INDEX IX_generic_post_history_date (post_date DESC)
);

-- 5.7 generic_email_configuration
CREATE TABLE generic_email_configuration (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    job_config_id   INT           NOT NULL REFERENCES generic_job_configuration(id),
    email_type      VARCHAR(50)   NOT NULL,
    -- 'POST_FAILURE' | 'IMAGE_FAILURE' | 'MISSING_COA' | 'FEED_FAILURE'
    email_to        NVARCHAR(1000) NULL,
    email_cc        NVARCHAR(1000) NULL,
    email_bcc       NVARCHAR(1000) NULL,
    email_subject   NVARCHAR(500)  NULL,
    email_template  NVARCHAR(500)  NULL,
    smtp_server     VARCHAR(200)   NULL,
    smtp_port       INT            NULL DEFAULT 587,
    smtp_username   VARCHAR(200)   NULL,
    smtp_password   VARCHAR(200)   NULL,
    smtp_use_ssl    BIT            NOT NULL DEFAULT 1,
    is_active       BIT            NOT NULL DEFAULT 1
);

-- 5.8 generic_feed_download_history
CREATE TABLE generic_feed_download_history (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    job_config_id   INT          NOT NULL,
    feed_name       VARCHAR(100) NOT NULL,
    is_manual       BIT          NOT NULL DEFAULT 0,
    status          VARCHAR(20)  NOT NULL,  -- 'Start' | 'End' | 'Error'
    record_count    INT          NULL,
    error_message   NVARCHAR(MAX) NULL,
    download_date   DATETIME     NOT NULL DEFAULT GETDATE()
);

-- 5.9 generic_execution_history
-- Full execution audit trail per run
CREATE TABLE generic_execution_history (
    id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    job_config_id       INT          NOT NULL REFERENCES generic_job_configuration(id),
    client_type         VARCHAR(50)  NOT NULL,
    job_id              INT          NOT NULL,
    execution_type      VARCHAR(30)  NOT NULL,  -- 'POST' | 'FEED_DOWNLOAD'
    trigger_type        VARCHAR(20)  NOT NULL,  -- 'SCHEDULED' | 'MANUAL'
    status              VARCHAR(20)  NOT NULL,
    -- 'SUCCESS' | 'FAILED' | 'PARTIAL_SUCCESS' | 'NO_RECORDS'
    records_processed   INT          NULL DEFAULT 0,
    records_succeeded   INT          NULL DEFAULT 0,
    records_failed      INT          NULL DEFAULT 0,
    error_details       NVARCHAR(MAX) NULL,
    start_time          DATETIME     NOT NULL DEFAULT GETDATE(),
    end_time            DATETIME     NULL,
    duration_seconds    AS DATEDIFF(SECOND, start_time, end_time),
    triggered_by_user   INT          NULL,
    INDEX IX_exec_history_job (job_config_id, start_time DESC),
    INDEX IX_exec_history_status (status, start_time DESC)
);

-- 5.10 generic_field_mapping
-- Drives dynamic payload building for simple REST clients (no custom plugin needed)
CREATE TABLE generic_field_mapping (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    job_config_id   INT          NOT NULL REFERENCES generic_job_configuration(id),
    mapping_type    VARCHAR(30)  NOT NULL,
    -- 'INVOICE_HEADER' | 'INVOICE_LINE' | 'FEED_RESPONSE' | 'FEED_REQUEST'
    source_field    VARCHAR(200) NOT NULL,
    target_field    VARCHAR(200) NOT NULL,
    data_type       VARCHAR(50)  NOT NULL DEFAULT 'VARCHAR',
    transform_rule  NVARCHAR(500) NULL,
    is_required     BIT          NOT NULL DEFAULT 0,
    sort_order      INT          NOT NULL DEFAULT 0,
    is_active       BIT          NOT NULL DEFAULT 1
);
```

---

## 6. InvitedClubPlugin Design

### 6.1 Class Structure

```
IPS.AutoPost.Plugins/InvitedClub/
  InvitedClubPlugin.cs           -- IClientPlugin entry point
  InvitedClubPostStrategy.cs     -- 3-step Oracle Fusion post
  InvitedClubFeedStrategy.cs     -- Supplier/Address/Site/COA download
  InvitedClubRetryService.cs     -- RetryPostImages (pre-post housekeeping)
  Models/
    InvitedClubConfig.cs         -- client_config_json deserialization
    InvoiceRequest.cs            -- InvoiceRequest, InvoiceLine, InvoiceDistribution, InvoiceDff
    AttachmentRequest.cs
    InvoiceCalculateTaxRequest.cs
    InvoiceResponse.cs           -- InvoiceResponse, AttachmentResponse, InvoiceCalculateTaxResponse
    SupplierResponse.cs          -- SupplierResponse, SupplierData
    SupplierAddressResponse.cs
    SupplierSiteResponse.cs
    COAResponse.cs
    FailedImagesData.cs
    PostHistory.cs               -- InvitedClub-specific history model
    EmailConfig.cs
    APIResponseType.cs
  Constants/
    InvitedClubConstants.cs
```

### 6.2 InvitedClubConfig (client_config_json)

```csharp
// Deserialized from generic_job_configuration.client_config_json
public class InvitedClubConfig
{
    public int ImagePostRetryLimit { get; set; } = 3;
    public int EdenredFailQueueId { get; set; }
    public int InvitedFailQueueId { get; set; }
    public string FeedDownloadTime { get; set; } = "07:00";  // HH:mm
    public DateTime LastSupplierDownloadTime { get; set; }
}
```

### 6.3 InvitedClubPlugin

```csharp
public class InvitedClubPlugin : IClientPlugin
{
    public string ClientType => "INVITEDCLUB";

    private readonly InvitedClubPostStrategy _postStrategy;
    private readonly InvitedClubFeedStrategy _feedStrategy;
    private readonly InvitedClubRetryService _retryService;
    private readonly ILogger<InvitedClubPlugin> _logger;

    public async Task OnBeforePostAsync(GenericJobConfig config, CancellationToken ct)
    {
        // Run RetryPostImages before main post on every scheduled run
        var clientConfig = config.GetClientConfig<InvitedClubConfig>();
        await _retryService.RetryPostImagesAsync(config, clientConfig, ct);
    }

    public async Task<PostBatchResult> ExecutePostAsync(
        GenericJobConfig config, PostContext context, CancellationToken ct)
        => await _postStrategy.ExecuteAsync(config, context, ct);

    public async Task<FeedResult> ExecuteFeedDownloadAsync(
        GenericJobConfig config, FeedContext context, CancellationToken ct)
        => await _feedStrategy.ExecuteAsync(config, context, ct);
}
```

### 6.4 InvitedClubPostStrategy — Key Method Signatures

```csharp
public class InvitedClubPostStrategy
{
    // Main entry point — processes all workitems
    public async Task<PostBatchResult> ExecuteAsync(
        GenericJobConfig config, PostContext context, CancellationToken ct);

    // Step 1: POST invoice to Oracle Fusion
    // Returns InvoiceResponse with InvoiceId on HTTP 201
    private async Task<InvoiceResponse> PostInvoiceAsync(
        string invoiceRequestJson, GenericJobConfig config, CancellationToken ct);

    // Step 2: POST attachment to Oracle Fusion
    // URL: {PostServiceURL}/{invoiceId}/child/attachments
    // Content-Type: application/vnd.oracle.adf.resourceitem+json
    private async Task<AttachmentResponse> PostInvoiceAttachmentAsync(
        GenericJobConfig config, string invoiceId,
        string attachmentRequestJson, CancellationToken ct);

    // Step 3: POST calculateTax (only when UseTax = "YES")
    // URL: {PostServiceURL}/action/calculateTax
    // Content-Type: application/vnd.oracle.adf.action+json
    // Body: AddJsonBody (not AddParameter)
    private async Task<InvoiceCalculateTaxResponse> PostCalculateTaxAsync(
        string calcTaxRequestJson, GenericJobConfig config, CancellationToken ct);

    // Build InvoiceRequest JSON from header + detail DataSet
    // Applies UseTax=NO logic (removes ShipToLocation from all lines)
    private string BuildInvoiceRequestJson(DataRow drHeader, DataTable dtDetail);

    // Get image: S3 (non-legacy) or local file system (legacy)
    private async Task<(string base64, string fileName, bool failed)> GetImageAsync(
        string imgPath, GenericJobConfig config, EdenredApiUrlConfig s3Config, CancellationToken ct);

    // Write to post_to_invitedclub_history
    // NOTE: History is written ONLY when at least one API call was attempted
    // (not for image-not-found or RequesterId-empty early exits)
    private async Task SaveHistoryAsync(
        PostHistory history, GenericJobConfig config, CancellationToken ct);
}
```

### 6.5 InvitedClubFeedStrategy — Key Method Signatures

```csharp
public class InvitedClubFeedStrategy
{
    public async Task<FeedResult> ExecuteAsync(
        GenericJobConfig config, FeedContext context, CancellationToken ct);

    // Paginated GET: /suppliers?onlyData=true&q=InactiveDate is null&limit=500&offset=N
    // All suppliers downloaded (no filter on Status)
    // Truncate-then-insert into InvitedClubSupplier
    private async Task<List<SupplierResponse>> LoadSupplierAsync(
        GenericJobConfig config, CancellationToken ct);

    // Paginated GET per supplier: /suppliers/{id}/child/addresses?onlyData=true&limit=500&offset=N
    // Initial call: all supplier IDs
    // Incremental: suppliers where LastUpdateDate >= LastSupplierDownloadTime - 2 days
    // IMPORTANT: inject SupplierId into each item after deserialization
    private async Task<List<SupplierAddressResponse>> LoadSupplierAddressAsync(
        GenericJobConfig config, List<string> supplierIds, CancellationToken ct);

    // Same pattern as address but for sites
    // After insert: call InvitedClub_UpdateSupplierSiteInSupplierAddress SP
    private async Task<List<SupplierSiteResponse>> LoadSupplierSiteAsync(
        GenericJobConfig config, List<string> supplierIds, CancellationToken ct);

    // Paginated GET: /accountCombinationsLOV?onlyData=true&q=_CHART_OF_ACCOUNTS_ID=5237;EnabledFlag='Y';AccountType!='O'
    // Truncate-then-insert into InvitedClubCOA
    // After insert: check missing CodeCombinationIds vs InvitedClubsCOAFullFeed
    // If missing: export to Excel, send email (uses EmailTemplate + EmailTo fields)
    private async Task<List<COAResponse>> LoadCOAAsync(
        GenericJobConfig config, CancellationToken ct);

    // Determines initial vs incremental call by checking SELECT COUNT(*) FROM {tableName}
    private async Task<bool> IsInitialCallAsync(string tableName, CancellationToken ct);

    // Bulk insert: truncate-then-insert (ImportFeedDataInDB)
    // Incremental: DELETE WHERE SupplierId IN (...) then bulk insert (ImportDataInDB)
    private async Task BulkInsertAsync(
        GenericJobConfig config, DataTable dt, string tableName,
        bool isInitialCall, CancellationToken ct);
}
```

### 6.6 InvitedClubRetryService

```csharp
public class InvitedClubRetryService
{
    // Called by OnBeforePostAsync before every scheduled post run
    // SP: InvitedClub_GetFailedImagesData(@HeaderTable, @ImagePostRetryLimit, @InvitedFailPostQueueId)
    // Returns records with InvoiceId but no AttachedDocumentId, retry count < limit
    public async Task RetryPostImagesAsync(
        GenericJobConfig config, InvitedClubConfig clientConfig, CancellationToken ct);

    // On success: update AttachedDocumentId, route to SuccessQueueId
    // Always: increment ImagePostRetryCount
    // Always uses config.DefaultUserId (never manual userId)
    // Always uses operationType = "Automatic Route:"
    private async Task RetryOneImageAsync(
        FailedImagesData item, GenericJobConfig config,
        InvitedClubConfig clientConfig, CancellationToken ct);
}
```

### 6.7 InvitedClubConstants

```csharp
public static class InvitedClubConstants
{
    // API URIs
    public const string SupplierUri = "suppliers?onlyData=true&q=InactiveDate is null";
    public const string SupplierAddressUriPre = "suppliers/";
    public const string SupplierAddressUriPost = "/child/addresses?onlyData=true";
    public const string SupplierSiteUriPre = "suppliers/";
    public const string SupplierSiteUriPost = "/child/sites?onlyData=true";
    public const string CoaUri = "accountCombinationsLOV?onlyData=true&q=_CHART_OF_ACCOUNTS_ID=5237;EnabledFlag='Y';AccountType!='O'";
    public const string AttachmentUri = "/child/attachments";
    public const string CalculateTaxUri = "/action/calculateTax";

    // Content-Types (Oracle Fusion specific — must be exact)
    public const string InvoiceContentType = "application/json";
    public const string AttachmentContentType = "application/vnd.oracle.adf.resourceitem+json";
    public const string CalculateTaxContentType = "application/vnd.oracle.adf.action+json";

    // Table Names
    public const string SupplierTableName = "InvitedClubSupplier";
    public const string SupplierAddressTableName = "InvitedClubSupplierAddress";
    public const string SupplierSiteTableName = "InvitedClubSupplierSite";
    public const string CoaTableName = "InvitedClubCOA";

    // Stored Procedures
    public const string GetConfigSp = "get_invitedclub_configuration";
    public const string GetHeaderDetailSp = "InvitedClub_GetHeaderAndDetailData";
    public const string GetFailedImagesSp = "InvitedClub_GetFailedImagesData";
    public const string GetSupplierExportSp = "InvitedClub_GetSupplierDataToExport";
    public const string UpdateSupplierSiteSp = "InvitedClub_UpdateSupplierSiteInSupplierAddress";
    public const string GetExecutionScheduleSp = "GetExecutionSchedule";
    public const string GetEmailConfigSp = "GetInvitedClubsEmailConfigPerJob";
    public const string WorkitemRouteSp = "WORKITEM_ROUTE";
    public const string GeneralLogInsertSp = "GENERALLOG_INSERT";

    // Misc
    public const string TempExcelFileName = "InvitedClubsMissingCOAInMaster.xlsx";
    public const string MissingImagesTablePlaceholder = "#MissingImagesTable#";
    public const string GeneralLogOperationType = "Post To InvitedClubs";
    public const string GeneralLogSourceObject = "Contents";
    public const string ImageFailureReason = "Image is not available.";
    public const string RequesterIdFailureReason = "RequesterId not found in HR Feed";
}
```

---

## 7. SevitaPlugin Design

### 7.1 Class Structure

```
IPS.AutoPost.Plugins/Sevita/
  SevitaPlugin.cs              -- IClientPlugin entry point
  SevitaPostStrategy.cs        -- OAuth2 + validation + line grouping + post
  SevitaTokenService.cs        -- OAuth2 client_credentials token caching
  SevitaValidationService.cs   -- PO/Non-PO validation rules
  Models/
    SevitaConfig.cs            -- client_config_json deserialization
    InvoiceRequest.cs          -- InvoiceRequest, InvoiceLine, AttachmentRequest
    InvoiceResponse.cs         -- InvoiceResponse, InvoicePostResponse
    ValidIds.cs                -- HashSet<string> VendorIds, EmployeeIds
    PostHistory.cs             -- Sevita-specific history (with Comment field)
    PostFailedRecord.cs
  Constants/
    SevitaConstants.cs
```

### 7.2 SevitaConfig (client_config_json)

```csharp
public class SevitaConfig
{
    public bool IsPORecord { get; set; }           // PO vs Non-PO validation rules
    public string PostJsonPath { get; set; } = string.Empty;  // S3 path for audit JSON
    public string TDriveLocation { get; set; } = string.Empty;
    public string NewUiTDriveLocation { get; set; } = string.Empty;
    public string RemotePath { get; set; } = string.Empty;
    public string ApiAccessTokenUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public int TokenExpirationMin { get; set; } = 60;
}
```

### 7.3 SevitaPlugin

```csharp
public class SevitaPlugin : IClientPlugin
{
    public string ClientType => "SEVITA";

    private readonly SevitaPostStrategy _postStrategy;
    private ValidIds? _validIds;  // Loaded once per batch in OnBeforePostAsync

    // Load ValidIds ONCE before the workitem loop
    public async Task OnBeforePostAsync(GenericJobConfig config, CancellationToken ct)
    {
        _validIds = await LoadValidIdsAsync(config, ct);
    }

    public async Task<PostBatchResult> ExecutePostAsync(
        GenericJobConfig config, PostContext context, CancellationToken ct)
        => await _postStrategy.ExecuteAsync(config, context, _validIds!, ct);

    // No feed download for Sevita
    // ExecuteFeedDownloadAsync uses default: returns FeedResult.NotApplicable()

    // Override ClearPostInProcessAsync to use Sevita SP
    public async Task ClearPostInProcessAsync(
        long itemId, GenericJobConfig config,
        IRoutingRepository routingRepo, CancellationToken ct)
        => await routingRepo.ExecuteSpAsync(
            "UpdateSevitaHeaderPostFields",
            new SqlParameter("@UID", itemId), ct);

    // SELECT Supplier FROM Sevita_Supplier_SiteInformation_Feed
    // SELECT EmployeeID FROM Sevita_Employee_Feed
    private async Task<ValidIds> LoadValidIdsAsync(
        GenericJobConfig config, CancellationToken ct);
}
```

### 7.4 SevitaTokenService

```csharp
public class SevitaTokenService
{
    private string? _cachedToken;
    private DateTime _tokenExpiration = DateTime.MinValue;

    // POST to api_access_token_url with grant_type=client_credentials
    // Content-Type: application/x-www-form-urlencoded
    // Caches token until TokenExpirationMin minutes have elapsed
    public async Task<string> GetAuthTokenAsync(
        SevitaConfig clientConfig, CancellationToken ct)
    {
        if (_cachedToken != null && _tokenExpiration > DateTime.UtcNow)
            return _cachedToken;

        // POST to token endpoint
        var response = await PostTokenRequestAsync(clientConfig, ct);
        _cachedToken = response.AccessToken;
        _tokenExpiration = DateTime.UtcNow.AddMinutes(clientConfig.TokenExpirationMin);
        return _cachedToken;
    }
}
```

### 7.5 SevitaValidationService

```csharp
public class SevitaValidationService
{
    // Validates line sum == header invoice amount
    public (bool isValid, string? error) ValidateLineSum(
        decimal headerAmount, List<InvoiceLine> lineItems);

    // PO validation: vendorId, invoiceDate, invoiceNumber, checkMemo required
    // vendorId must be in ValidIds.VendorIds
    // checkMemo defaults to "PO#" if empty
    public (bool isMissingField, bool isRecordValid, string? error) ValidatePO(
        InvoiceRequest request, ValidIds validIds);

    // Non-PO validation: vendorId, employeeId, invoiceDate, invoiceNumber,
    //                    checkMemo, expensePeriod required
    // vendorId in VendorIds AND employeeId in EmployeeIds
    // If any line naturalAccountNumber = "174098" -> cerfTrackingNumber required
    public (bool isMissingField, bool isRecordValid, string? error) ValidateNonPO(
        InvoiceRequest request, ValidIds validIds);

    // Validates attachment required fields: fileName, fileBase, fileUrl, docid
    public bool ValidateAttachments(List<AttachmentRequest> attachments);
}
```

### 7.6 SevitaPostStrategy — Key Method Signatures

```csharp
public class SevitaPostStrategy
{
    public async Task<PostBatchResult> ExecuteAsync(
        GenericJobConfig config, PostContext context,
        ValidIds validIds, CancellationToken ct);

    // Groups detail rows by (alias + naturalAccountNumber), sums LineAmount per group
    // Builds lineItems with edenredLineItemId = edenredInvoiceId + "_" + lineItemCount
    private List<InvoiceLine> BuildLineItems(DataTable dtDetail, string edenredInvoiceId);

    // Serializes payload as JSON array: "[{...}]"
    private string SerializePayload(InvoiceRequest request);

    // If post_json_path configured: upload JSON to S3 at {path}/{itemId}_{timestamp}.json
    private async Task UploadAuditJsonAsync(
        long itemId, string json, SevitaConfig clientConfig, CancellationToken ct);

    // POST to InvoicePostURL with Authorization: Bearer {token}
    // IMPORTANT: Uses request.AddParameter("application/json", invoiceRequestJson, ParameterType.RequestBody)
    //            NOT AddJsonBody — this is different from InvitedClub's calculateTax endpoint
    // Success: HTTP 201, extract InvoiceId from invoiceIds object first property name
    // HTTP 500: special error message "Internal Server error occurred while posting invoice."
    // Other non-201: extract recordErrors, message, invoiceIds, failedRecords
    private async Task<InvoiceResponse> PostInvoiceAsync(
        string payload, GenericJobConfig config,
        SevitaConfig clientConfig, CancellationToken ct);

    // Save history with fileBase = null on all attachments
    // IMPORTANT: Parse invoiceRequestJson as JArray (not JObject) because payload is "[{...}]"
    //            Use JArray.Parse(invoiceRequestJson) then iterate items to null out fileBase
    // Table: sevita_posted_records_history
    // Columns: job_id, item_id, post_request, post_response, post_date, posted_by, manually_posted, Comment
    private async Task SaveHistoryAsync(
        string invoiceRequestJson, long itemId, InvoiceResponse response,
        bool processManually, int userId, string comment,
        GenericJobConfig config, CancellationToken ct);

    // Send failure notification email after batch
    // To: FailedPostConfiguration.EmailTo (split by ';')
    // Body: HTML table from failed records (IsSendNotification=true, excluding IsSendNotification column)
    // IMPORTANT: Replace [[AppendTable]] placeholder (NOT #MissingImagesTable# which is InvitedClub's)
    // Uses GenerateHtmlTable() extension method
    private async Task SendNotificationEmailAsync(
        GenericJobConfig config, SevitaConfig clientConfig,
        List<PostFailedRecord> failedRecords, CancellationToken ct);
}
```

### 7.7 SevitaPlugin — OnBeforePostAsync (ValidIds Loading)

```csharp
// IMPORTANT: Uses direct SqlConnection + SqlDataReader.NextResult() — NOT SqlHelper
// SqlHelper.ExecuteDatasetAsync does not support multi-statement queries with NextResult()
// Two result sets in one round trip:
//   Result 1: SELECT Supplier FROM Sevita_Supplier_SiteInformation_Feed
//   Result 2: SELECT EmployeeID FROM Sevita_Employee_Feed
public override async Task OnBeforePostAsync(GenericJobConfig config, CancellationToken ct)
{
    await using var cn = new SqlConnection(config.DbConnectionString);
    await cn.OpenAsync(ct);
    await using var cmd = new SqlCommand(
        "SELECT Supplier FROM Sevita_Supplier_SiteInformation_Feed; " +
        "SELECT EmployeeID FROM Sevita_Employee_Feed", cn);
    await using var reader = await cmd.ExecuteReaderAsync(ct);

    _validIds = new ValidIds();
    while (await reader.ReadAsync(ct))
        _validIds.VendorIds.Add(reader.GetString(0));

    await reader.NextResultAsync(ct);
    while (await reader.ReadAsync(ct))
        _validIds.EmployeeIds.Add(reader.GetString(0));
}

// IMPORTANT: SqlHelper.ConnectionString is set ONCE at startup — NOT per-configuration
// Unlike InvitedClub which reassigns SqlHelper.ConnectionString per-configuration,
// Sevita uses a single connection string for all operations.
// The per-configuration assignment is commented out in the existing Sevita code.
```

### 7.8 SevitaConfig — DBErrorEmailConfiguration

```csharp
// Loaded from get_sevita_configurations SP result
// Used for database error email notifications (separate from post failure emails)
public class DBErrorEmailConfiguration
{
    public string ToEmailAddress { get; set; } = string.Empty;   // db_error_to_email_address
    public string CcEmailAddress { get; set; } = string.Empty;   // db_error_cc_email_address
    public string EmailSubject { get; set; } = string.Empty;     // db_error_email_subject
    public string EmailTemplate { get; set; } = string.Empty;    // db_error_email_template
}
```

---

## 8. Shared Infrastructure — SqlHelper and Services

### 8.1 SqlHelper (.NET Core 10, async, Microsoft.Data.SqlClient)

```csharp
// IPS.AutoPost.Core/DataAccess/SqlHelper.cs
using Microsoft.Data.SqlClient;

public static class SqlHelper
{
    // Connection string set per-configuration in the processing loop
    public static string ConnectionString { get; set; } = string.Empty;

    public static async Task<DataSet> ExecuteDatasetAsync(
        CommandType commandType, string commandText,
        params SqlParameter[] parameters)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(commandText, cn)
        {
            CommandType = commandType,
            CommandTimeout = 0
        };
        if (parameters?.Length > 0)
            cmd.Parameters.AddRange(parameters);

        var da = new SqlDataAdapter(cmd);
        var ds = new DataSet();
        da.Fill(ds);
        return ds;
    }

    public static async Task<int> ExecuteNonQueryAsync(
        CommandType commandType, string commandText,
        params SqlParameter[] parameters)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(commandText, cn)
        {
            CommandType = commandType,
            CommandTimeout = 0
        };
        if (parameters?.Length > 0)
            cmd.Parameters.AddRange(parameters);
        return await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<object?> ExecuteScalarAsync(
        CommandType commandType, string commandText,
        params SqlParameter[] parameters)
    {
        await using var cn = new SqlConnection(ConnectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(commandText, cn)
        {
            CommandType = commandType,
            CommandTimeout = 0
        };
        if (parameters?.Length > 0)
            cmd.Parameters.AddRange(parameters);
        return await cmd.ExecuteScalarAsync();
    }

    // Factory method for creating SqlParameters
    public static SqlParameter Param(
        string name, SqlDbType type, object? value,
        int size = 0, ParameterDirection direction = ParameterDirection.Input)
    {
        var p = new SqlParameter(name, type) { Direction = direction };
        if (size > 0) p.Size = size;
        p.Value = value ?? DBNull.Value;
        return p;
    }

    // Bulk copy for feed data inserts
    public static async Task BulkCopyAsync(
        string connectionString, DataTable dt, string destinationTable,
        int timeoutSeconds = 600)
    {
        using var bulkCopy = new SqlBulkCopy(
            connectionString, SqlBulkCopyOptions.KeepIdentity)
        {
            BulkCopyTimeout = timeoutSeconds,
            DestinationTableName = destinationTable
        };
        foreach (DataColumn col in dt.Columns)
            bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
        await bulkCopy.WriteToServerAsync(dt);
    }
}
```

### 8.2 ConfigurationService (Secrets Manager)

```csharp
// IPS.AutoPost.Core/Services/ConfigurationService.cs
public class ConfigurationService
{
    private readonly IAmazonSecretsManager _secretsManager;
    private readonly Dictionary<string, string> _cache = new();

    // Secret path: /InvoiceSystem/Common/{env}/Database/Workflow
    // Returns connection string from Secrets Manager
    // Falls back to appsettings.json if Secrets Manager unavailable
    public async Task<string> GetConnectionStringAsync(
        string secretPath, CancellationToken ct);

    // Generic secret retrieval with in-memory caching
    public async Task<Dictionary<string, string>> GetSecretAsync(
        string secretName, CancellationToken ct);
}
```

### 8.3 S3ImageService

```csharp
// IPS.AutoPost.Core/Services/S3ImageService.cs
public class S3ImageService
{
    private readonly S3Utility _s3Utility;

    // Wraps S3Utility.GetBase64ImageFromS3Async
    // Handles case-insensitive file extension fallback (tries .pdf then .PDF)
    // Returns empty string if not found (caller checks for empty)
    public async Task<string> GetBase64ImageAsync(
        string s3Key, string bucketName, CancellationToken ct);

    // Wraps S3Utility.UploadFileToS3Async
    // Used by Sevita for audit JSON upload
    public async Task UploadFileAsync(
        string s3Path, string localPath, string bucketName, CancellationToken ct);
}
```

### 8.4 DataTableExtensions

```csharp
// IPS.AutoPost.Core/Extensions/DataTableExtensions.cs
public static class DataTableExtensions
{
    // Converts DataTable to HTML <table> string
    // Used for image failure email body (#MissingImagesTable# placeholder)
    // and Sevita failure notification email
    public static string GenerateHtmlTable(this DataTable dt)
    {
        var sb = new StringBuilder();
        sb.Append("<table border='1' cellpadding='4' cellspacing='0'><thead><tr>");
        foreach (DataColumn col in dt.Columns)
            sb.Append($"<th>{col.ColumnName}</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (DataRow row in dt.Rows)
        {
            sb.Append("<tr>");
            foreach (DataColumn col in dt.Columns)
                sb.Append($"<td>{row[col]}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    // Converts List<T> to DataTable using PropertyDescriptor
    public static DataTable ToDataTable<T>(this List<T> data);

    // Converts DataTable rows to List<T> using property name matching
    public static List<T> ConvertDataTable<T>(this DataTable dt) where T : new();
}
```

### 8.5 FeedWorker and PostWorker (BackgroundService with MediatR + Scoped DI)

```csharp
// IPS.AutoPost.Host.PostWorker/PostWorker.cs
// Pattern: MaxNumberOfMessages=10, new DI scope per message, MediatR command dispatch
public class PostWorker : BackgroundService
{
    private readonly IAmazonSQS _sqs;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _queueUrl;  // from env var SQS_QUEUE_URL

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = _queueUrl,
                MaxNumberOfMessages = 10,   // Process up to 10 at once (not 1)
                WaitTimeSeconds = 20        // Long polling — reduces empty responses
            }, stoppingToken);

            foreach (var message in response.Messages)
            {
                try
                {
                    // NEW SCOPE per message — prevents state leakage between messages
                    using var scope = _serviceProvider.CreateScope();
                    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                    var correlationService = scope.ServiceProvider.GetRequiredService<ICorrelationIdService>();

                    using (correlationService.SetCorrelationId(Guid.NewGuid().ToString()))
                    {
                        var command = JsonSerializer.Deserialize<ExecutePostCommand>(message.Body)!;
                        await mediator.Send(command, stoppingToken);
                    }

                    // Delete ONLY after successful processing
                    await _sqs.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                }
                catch (Exception ex)
                {
                    // Log but do NOT delete — SQS will retry up to maxReceiveCount
                    _logger.LogError(ex, "Failed to process message {MessageId}", message.MessageId);
                }
            }
        }
    }
}

// SQS message contract
public class SqsMessagePayload
{
    public int JobId { get; set; }
    public string ClientType { get; set; } = string.Empty;
    public string Pipeline { get; set; } = string.Empty;  // "Post" | "Feed"
    public string TriggerType { get; set; } = "Scheduled";
    public string? Mode { get; set; }  // Optional: "DryRun", "UAT", etc.
}
```

### 8.6 SecretsManagerConfigurationProvider (Infrastructure/ folder)

```csharp
// IPS.AutoPost.Core/Infrastructure/SecretsManagerConfigurationProvider.cs
// Config-path pattern: values starting with "/" in appsettings.json are fetched from Secrets Manager
// Called once at startup: await builder.Configuration.AddSecretsManagerAsync()

public static class SecretsManagerExtensions
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static async Task AddSecretsManagerAsync(
        this IConfigurationBuilder builder, TimeSpan? timeout = null)
    {
        var secretsManager = CreateSecretsManagerClient();
        var currentConfig = builder.Build();
        var secretMappings = FindSecretMappings(currentConfig);

        if (secretMappings.Count == 0) return;

        var secrets = await FetchSecretsAsync(secretsManager, secretMappings, timeout ?? DefaultTimeout);
        builder.AddInMemoryCollection(secrets);  // Highest priority — overrides appsettings.json
    }

    private static Dictionary<string, string> FindSecretMappings(IConfiguration config)
    {
        var mappings = new Dictionary<string, string>();

        // Scan ConnectionStrings section
        foreach (var item in config.GetSection("ConnectionStrings").GetChildren())
            if (item.Value?.StartsWith("/") == true)
                mappings[$"ConnectionStrings:{item.Key}"] = item.Value;

        // Scan Email:SmtpPassword
        var smtpPassword = config["Email:SmtpPassword"];
        if (smtpPassword?.StartsWith("/") == true)
            mappings["Email:SmtpPassword"] = smtpPassword;

        // Scan ApiKey:Value
        var apiKeyValue = config["ApiKey:Value"];
        if (apiKeyValue?.StartsWith("/") == true)
            mappings["ApiKey:Value"] = apiKeyValue;

        return mappings;
    }

    private static async Task<string> GetSecretValueAsync(
        IAmazonSecretsManager client, string secretId, CancellationToken ct)
    {
        var response = await client.GetSecretValueAsync(
            new GetSecretValueRequest { SecretId = secretId }, ct);

        // Handle JSON secrets (e.g., RDS-managed secrets with AppConnectionString key)
        if (response.SecretString.TrimStart().StartsWith("{"))
        {
            using var doc = JsonDocument.Parse(response.SecretString);
            if (doc.RootElement.TryGetProperty("AppConnectionString", out var connStr))
                return connStr.GetString()!;
        }
        return response.SecretString;
    }
}

// appsettings.json pattern:
// {
//   "ConnectionStrings": { "Workflow": "/IPS/Common/production/Database/Workflow" },
//   "Email": { "SmtpPassword": "/IPS/Common/production/Smtp" },
//   "ApiKey": { "Value": "/IPS/Common/production/ApiKey" }
// }
// Plugin-specific credentials (/IPS/InvitedClub/{env}/PostAuth, /IPS/Sevita/{env}/PostAuth)
// are fetched on-demand by each plugin via ConfigurationService.GetSecretAsync()
```

### 8.7 CorrelationIdService (AsyncLocal)

```csharp
// IPS.AutoPost.Core/Services/CorrelationIdService.cs
public class CorrelationIdService : ICorrelationIdService
{
    private static readonly AsyncLocal<string> _correlationId = new();

    public string GetOrCreateCorrelationId()
        => _correlationId.Value ??= Guid.NewGuid().ToString();

    public IDisposable SetCorrelationId(string correlationId)
    {
        _correlationId.Value = correlationId;
        return LogContext.PushProperty("CorrelationId", correlationId);
        // All Serilog log entries for this message now include [{CorrelationId}]
        // Searchable in CloudWatch Logs Insights: filter @message like /abc-123-def/
    }
}
```

### 8.8 CloudWatchMetricsService

```csharp
// IPS.AutoPost.Core/Services/CloudWatchMetricsService.cs
public class CloudWatchMetricsService : ICloudWatchMetricsService
{
    private readonly IAmazonCloudWatch _cloudWatch;
    private readonly string _namespace;  // "IPS/AutoPost/{env}"

    // Publishes metric dimensioned by ClientType + JobId
    private async Task PublishAsync(string metricName, double value,
        string clientType, int jobId, StandardUnit unit = StandardUnit.Count)
    {
        await _cloudWatch.PutMetricDataAsync(new PutMetricDataRequest
        {
            Namespace = _namespace,
            MetricData = new List<MetricDatum>
            {
                new MetricDatum
                {
                    MetricName = metricName,
                    Value = value,
                    Unit = unit,
                    Timestamp = DateTime.UtcNow,
                    Dimensions = new List<Dimension>
                    {
                        new Dimension { Name = "ClientType", Value = clientType },
                        new Dimension { Name = "JobId", Value = jobId.ToString() }
                    }
                }
            }
        });
    }

    // 12 metric methods: PostStarted, PostCompleted, PostFailed,
    // PostSuccessCount, PostFailedCount, PostDurationSeconds,
    // FeedStarted, FeedCompleted, FeedRecordsDownloaded, FeedDurationSeconds,
    // ImageRetryAttempted, ImageRetrySucceeded
}
```

### 8.9 PostController (Manual Trigger)

```csharp
// IPS.AutoPost.Api/Controllers/PostController.cs
[ApiController]
[Route("api")]
public class PostController : ControllerBase
{
    private readonly AutoPostOrchestrator _orchestrator;

    // Manual post — calls Fargate DIRECTLY (not through SQS)
    [HttpPost("post/{jobId}/items/{itemIds}")]
    public async Task<IActionResult> PostItems(
        string jobId, string itemIds,
        [FromQuery] int userId = 0,
        CancellationToken ct = default)
    {
        var result = await _orchestrator.RunManualPostAsync(itemIds, userId, ct);
        return Ok(new
        {
            Status = result.RecordsFailed == 0 ? "Success" : "PartialSuccess",
            result.RecordsProcessed,
            result.RecordsSuccess,
            result.RecordsFailed,
            result.ItemResults
        });
    }

    [HttpPost("post/{jobId}")]
    public async Task<IActionResult> PostAll(string jobId, CancellationToken ct)
        => await PostItems(jobId, string.Empty, 0, ct);

    [HttpGet("status/{executionId}")]
    public async Task<IActionResult> GetStatus(long executionId, CancellationToken ct)
    {
        var history = await _historyRepo.GetExecutionHistoryAsync(executionId, ct);
        return history == null ? NotFound() : Ok(history);
    }
}
```

---

## 9. AWS Architecture — Three CloudFormation Stacks

### 9.1 Stack 1: infrastructure.yaml

Key resources and values:

```yaml
# Stack name: ips-autopost-infra-{env}
# Parameters: Environment (uat|production), VpcId, DatabaseSecurityGroupId,
#             ECSTaskCpu (1024), ECSTaskMemory (2048),
#             PublicSubnetCidr, PrivateSubnet1Cidr, PrivateSubnet2Cidr

Resources:
  # Networking
  PublicSubnet1:       # MapPublicIpOnLaunch: true, AZ[0]
  PrivateSubnet1:      # AZ[0]
  PrivateSubnet2:      # AZ[1]
  EIPForNAT:           # Elastic IP for NAT Gateway
  NATGateway:          # In PublicSubnet1
  PrivateRouteTable:   # Route 0.0.0.0/0 -> NATGateway
  PrivateSubnet1RouteTableAssociation:
  PrivateSubnet2RouteTableAssociation:

  # Security Groups
  ECSSecurityGroup:
    # Egress ONLY:
    #   TCP 1433 -> 0.0.0.0/0  (SQL Server)
    #   TCP 443  -> 0.0.0.0/0  (HTTPS for AWS services + ERP APIs)
    #   TCP 80   -> 0.0.0.0/0  (HTTP)
    # No inbound rules

  DatabaseSecurityGroupRule:
    # Type: AWS::EC2::SecurityGroupIngress
    # Adds TCP 1433 from ECSSecurityGroup to existing RDS security group
    # Does NOT modify the existing security group directly

  # ECR
  ECRRepository:
    # Name: ecr-ips-autopost-{env}
    # ScanOnPush: true
    # Lifecycle: delete untagged images after 7 days

  # ECS Cluster
  ECSCluster:
    # Name: ips-autopost-{env}
    # containerInsights: enabled

  # SQS Queues
  FeedDLQ:             # ips-feed-dlq-{env}, retention 1209600s (14 days)
  PostDLQ:             # ips-post-dlq-{env}, retention 1209600s (14 days)
  FeedQueue:
    # Name: ips-feed-queue-{env}
    # VisibilityTimeout: 7200 (2 hours)
    # MessageRetentionPeriod: 1209600 (14 days)
    # RedrivePolicy: maxReceiveCount=3, DLQ=FeedDLQ
  PostQueue:
    # Name: ips-post-queue-{env}
    # Same settings as FeedQueue

  # CloudWatch Log Groups
  FeedWorkerLogGroup:  # /ips/autopost/feed/{env}, RetentionInDays: 90
  PostWorkerLogGroup:  # /ips/autopost/post/{env}, RetentionInDays: 90

  # S3 Deployment Bucket
  DeploymentBucket:
    # Name: ips-autopost-deployments-{env}
    # Versioning: Enabled
    # PublicAccessBlock: all true

Outputs:
  PrivateSubnets, ECSSecurityGroupId, ECSClusterName,
  FeedQueueURL, PostQueueURL, FeedDLQArn, PostDLQArn,
  ECRRepositoryURI, FeedWorkerLogGroupName, PostWorkerLogGroupName
```

### 9.2 Stack 2: application.yaml

```yaml
# Stack name: ips-autopost-app-{env}
# Parameters: Environment, ImageURI, FeedQueueURL, PostQueueURL,
#             ECSTaskCpu, ECSTaskMemory, DeploymentId (= github.run_number)

Resources:
  # IAM — TWO separate roles
  ECSTaskExecutionRole:
    # Name: ips-autopost-ecs-execution-role-{env}
    # ManagedPolicies: AmazonECSTaskExecutionRolePolicy
    # (pull images from ECR, write logs to CloudWatch)

  ECSTaskRole:
    # Name: ips-autopost-ecs-task-role-{env}
    # Policies:
    #   SQSPolicy: ReceiveMessage, DeleteMessage, GetQueueAttributes, GetQueueUrl
    #              on ips-feed-queue-{env} and ips-post-queue-{env}
    #   CloudWatchPolicy: PutMetricData on *
    #   S3Policy: GetObject, PutObject on *
    #   SecretsManagerPolicy: GetSecretValue on arn:...:secret:*

  # Feed Worker Task Definition
  FeedWorkerTaskDefinition:
    # Family: ips-autopost-feed-{env}-{DeploymentId}
    # NetworkMode: awsvpc, RequiresCompatibilities: FARGATE
    # Cpu: 1024, Memory: 2048
    # ExecutionRoleArn: ECSTaskExecutionRole
    # TaskRoleArn: ECSTaskRole
    # Container:
    #   Name: ips-autopost-feed-container-{env}
    #   Image: {ImageURI}
    #   LogDriver: awslogs -> /ips/autopost/feed/{env}
    #   Environment:
    #     SQS_QUEUE_URL: {FeedQueueURL}
    #     ASPNETCORE_ENVIRONMENT: {env}
    #     AWS_DEFAULT_REGION: {AWS::Region}

  # Post Worker Task Definition
  PostWorkerTaskDefinition:
    # Family: ips-autopost-post-{env}-{DeploymentId}
    # Same as Feed but SQS_QUEUE_URL = {PostQueueURL}

  # ECS Services
  FeedWorkerService:
    # Name: ips-autopost-feed-service-{env}
    # DesiredCount: 1  (always 1 task running — eliminates cold start for manual post triggers)
    # DeploymentConfiguration: MaximumPercent=200, MinimumHealthyPercent=100

  PostWorkerService:
    # Name: ips-autopost-post-service-{env}
    # DesiredCount: 1  (always 1 task running — ~$15-20/month, price of instant responsiveness)

  # Auto Scaling — Feed Worker
  FeedWorkerScalingTarget:
    # MinCapacity: 1  (NEVER scale to zero — manual post triggers require instant response)
    # MaxCapacity: 5
    # ScalableDimension: ecs:service:DesiredCount

  FeedSQSScaleOutPolicy:
    # StepScaling, Cooldown: 120s
    # 1-10 messages -> +1 task
    # >10 messages  -> +2 tasks

  FeedSQSScaleInPolicy:
    # StepScaling, Cooldown: 600s
    # 0 messages (for 10 min) -> -1 task

  FeedCPUScaleOutPolicy:   # CPU > 80%, Cooldown 300s -> +1 task
  FeedCPUScaleInPolicy:    # CPU < 20%, Cooldown 300s -> -1 task
  FeedMemoryScaleOutPolicy: # Memory > 80%, Cooldown 300s -> +1 task
  FeedMemoryScaleInPolicy:  # Memory < 20%, Cooldown 300s -> -1 task

  # CloudWatch Alarms for scaling triggers
  FeedSQSMessagesHigh:
    # ApproximateNumberOfMessagesVisible >= 1 for 2 periods of 60s
    # -> triggers FeedSQSScaleOutPolicy

  FeedSQSMessagesLow:
    # ApproximateNumberOfMessagesVisible <= 0 for 2 periods of 300s
    # -> triggers FeedSQSScaleInPolicy

  FeedCPUHigh:    # CPUUtilization > 80%, 2x300s -> FeedCPUScaleOutPolicy
  FeedCPULow:     # CPUUtilization < 20%, 2x300s -> FeedCPUScaleInPolicy
  FeedMemoryHigh: # MemoryUtilization > 80%, 2x300s -> FeedMemoryScaleOutPolicy
  FeedMemoryLow:  # MemoryUtilization < 20%, 2x300s -> FeedMemoryScaleInPolicy

  # Same scaling resources for PostWorkerService
  PostWorkerScalingTarget: # MinCapacity: 1 (never to zero), MaxCapacity: 5
  # ... (same policies as Feed Worker)
```

### 9.3 Stack 3: monitoring.yaml

```yaml
# Stack name: ips-autopost-monitoring-{env}

Resources:
  CloudWatchDashboard:
    # Name: IPS-AutoPost-Operations-{env}
    # Widgets:
    #   - ECS CPU Utilization (Feed + Post workers)
    #   - ECS Memory Utilization (Feed + Post workers)
    #   - SQS Queue Metrics (Sent, Received, Deleted for feed + post queues)
    #   - Application Metrics (PostSuccessCount, PostFailedCount, FeedSuccessCount)
    # Namespace: IPS/AutoPost/{env}

  ApplicationErrorAlarm:
    # Metric: Errors in namespace IPS/AutoPost/{env}
    # Threshold: > 5 errors
    # Period: 300s, EvaluationPeriods: 2
    # TreatMissingData: notBreaching

  FeedDLQAlarm:
    # ApproximateNumberOfVisibleMessages >= 1 in ips-feed-dlq-{env}
    # Period: 300s, EvaluationPeriods: 1

  PostDLQAlarm:
    # ApproximateNumberOfVisibleMessages >= 1 in ips-post-dlq-{env}
    # Period: 300s, EvaluationPeriods: 1
```

---

## 10. Dockerfile — Multi-Stage Build

```dockerfile
# infra/docker/Dockerfile.PostWorker
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/IPS.AutoPost.Core/IPS.AutoPost.Core.csproj", "src/IPS.AutoPost.Core/"]
COPY ["src/IPS.AutoPost.Plugins/IPS.AutoPost.Plugins.csproj", "src/IPS.AutoPost.Plugins/"]
COPY ["src/IPS.AutoPost.Host.PostWorker/IPS.AutoPost.Host.PostWorker.csproj", "src/IPS.AutoPost.Host.PostWorker/"]

RUN dotnet restore "src/IPS.AutoPost.Host.PostWorker/IPS.AutoPost.Host.PostWorker.csproj"

COPY . .
WORKDIR "/src/src/IPS.AutoPost.Host.PostWorker"
RUN dotnet build "IPS.AutoPost.Host.PostWorker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "IPS.AutoPost.Host.PostWorker.csproj" -c Release -o /app/publish \
    --self-contained false --runtime linux-x64

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "IPS.AutoPost.Host.PostWorker.dll"]
```

```dockerfile
# infra/docker/Dockerfile.FeedWorker
# Same pattern — replace PostWorker with FeedWorker throughout
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/IPS.AutoPost.Core/IPS.AutoPost.Core.csproj", "src/IPS.AutoPost.Core/"]
COPY ["src/IPS.AutoPost.Plugins/IPS.AutoPost.Plugins.csproj", "src/IPS.AutoPost.Plugins/"]
COPY ["src/IPS.AutoPost.Host.FeedWorker/IPS.AutoPost.Host.FeedWorker.csproj", "src/IPS.AutoPost.Host.FeedWorker/"]

RUN dotnet restore "src/IPS.AutoPost.Host.FeedWorker/IPS.AutoPost.Host.FeedWorker.csproj"

COPY . .
WORKDIR "/src/src/IPS.AutoPost.Host.FeedWorker"
RUN dotnet build "IPS.AutoPost.Host.FeedWorker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "IPS.AutoPost.Host.FeedWorker.csproj" -c Release -o /app/publish \
    --self-contained false --runtime linux-x64

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "IPS.AutoPost.Host.FeedWorker.dll"]
```

---

## 11. GitHub Actions CI/CD Pipeline

```yaml
# .github/workflows/deploy.yml
name: Deploy IPS.AutoPost Platform

on:
  workflow_dispatch:
    inputs:
      environment:
        description: 'Environment to deploy to'
        required: true
        type: choice
        options: [uat, production]

env:
  AWS_REGION: ${{ vars.AWS_REGION }}

jobs:
  # -----------------------------------------------------------------------
  # JOB 1: Deploy Infrastructure Stack
  # -----------------------------------------------------------------------
  infrastructure:
    name: Deploy Infrastructure
    runs-on: ubuntu-latest
    environment: ${{ github.event.inputs.environment }}
    outputs:
      feed-queue-url: ${{ steps.infra.outputs.feed-queue-url }}
      post-queue-url: ${{ steps.infra.outputs.post-queue-url }}
      ecr-repository-uri: ${{ steps.infra.outputs.ecr-repository-uri }}
    steps:
      - uses: actions/checkout@v4
      - uses: aws-actions/configure-aws-credentials@v4
        with:
          aws-access-key-id: ${{ secrets.AWS_ACCESS_KEY_ID }}
          aws-secret-access-key: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
          aws-region: ${{ env.AWS_REGION }}

      - name: Deploy Infrastructure Stack
        id: infra
        run: |
          aws cloudformation deploy \
            --template-file infra/cloudformation/infrastructure.yaml \
            --stack-name ips-autopost-infra-${{ github.event.inputs.environment }} \
            --parameter-overrides \
              Environment=${{ github.event.inputs.environment }} \
              VpcId=${{ vars.VPC_ID }} \
              DatabaseSecurityGroupId=${{ vars.DATABASE_SECURITY_GROUP_ID }} \
              ECSTaskCpu=${{ vars.ECS_TASK_CPU }} \
              ECSTaskMemory=${{ vars.ECS_TASK_MEMORY }} \
              PublicSubnetCidr=${{ vars.PUBLIC_SUBNET_CIDR }} \
              PrivateSubnet1Cidr=${{ vars.PRIVATE_SUBNET_1_CIDR }} \
              PrivateSubnet2Cidr=${{ vars.PRIVATE_SUBNET_2_CIDR }} \
            --capabilities CAPABILITY_NAMED_IAM \
            --no-fail-on-empty-changeset

          FEED_QUEUE_URL=$(aws cloudformation describe-stacks \
            --stack-name ips-autopost-infra-${{ github.event.inputs.environment }} \
            --query 'Stacks[0].Outputs[?OutputKey==`FeedQueueURL`].OutputValue' \
            --output text)
          POST_QUEUE_URL=$(aws cloudformation describe-stacks \
            --stack-name ips-autopost-infra-${{ github.event.inputs.environment }} \
            --query 'Stacks[0].Outputs[?OutputKey==`PostQueueURL`].OutputValue' \
            --output text)
          ECR_URI=$(aws cloudformation describe-stacks \
            --stack-name ips-autopost-infra-${{ github.event.inputs.environment }} \
            --query 'Stacks[0].Outputs[?OutputKey==`ECRRepositoryURI`].OutputValue' \
            --output text)

          echo "feed-queue-url=$FEED_QUEUE_URL" >> $GITHUB_OUTPUT
          echo "post-queue-url=$POST_QUEUE_URL" >> $GITHUB_OUTPUT
          echo "ecr-repository-uri=$ECR_URI" >> $GITHUB_OUTPUT

  # -----------------------------------------------------------------------
  # JOB 2: Build Docker Images and Deploy Application Stack
  # -----------------------------------------------------------------------
  application:
    name: Deploy Application
    runs-on: ubuntu-latest
    environment: ${{ github.event.inputs.environment }}
    needs: infrastructure
    steps:
      - uses: actions/checkout@v4
      - uses: aws-actions/configure-aws-credentials@v4
        with:
          aws-access-key-id: ${{ secrets.AWS_ACCESS_KEY_ID }}
          aws-secret-access-key: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
          aws-region: ${{ env.AWS_REGION }}

      - name: Login to ECR
        run: |
          aws ecr get-login-password --region ${{ env.AWS_REGION }} | \
          docker login --username AWS --password-stdin \
            ${{ needs.infrastructure.outputs.ecr-repository-uri }}

      - name: Build and Push Feed Worker Image
        run: |
          docker build -f infra/docker/Dockerfile.FeedWorker \
            -t ${{ needs.infrastructure.outputs.ecr-repository-uri }}:feed-${{ github.sha }} .
          docker push ${{ needs.infrastructure.outputs.ecr-repository-uri }}:feed-${{ github.sha }}

      - name: Build and Push Post Worker Image
        run: |
          docker build -f infra/docker/Dockerfile.PostWorker \
            -t ${{ needs.infrastructure.outputs.ecr-repository-uri }}:post-${{ github.sha }} .
          docker push ${{ needs.infrastructure.outputs.ecr-repository-uri }}:post-${{ github.sha }}

      - name: Deploy Application Stack
        run: |
          aws cloudformation deploy \
            --template-file infra/cloudformation/application.yaml \
            --stack-name ips-autopost-app-${{ github.event.inputs.environment }} \
            --parameter-overrides \
              Environment=${{ github.event.inputs.environment }} \
              FeedImageURI=${{ needs.infrastructure.outputs.ecr-repository-uri }}:feed-${{ github.sha }} \
              PostImageURI=${{ needs.infrastructure.outputs.ecr-repository-uri }}:post-${{ github.sha }} \
              FeedQueueURL=${{ needs.infrastructure.outputs.feed-queue-url }} \
              PostQueueURL=${{ needs.infrastructure.outputs.post-queue-url }} \
              ECSTaskCpu=${{ vars.ECS_TASK_CPU }} \
              ECSTaskMemory=${{ vars.ECS_TASK_MEMORY }} \
              DeploymentId=${{ github.run_number }} \
            --capabilities CAPABILITY_NAMED_IAM \
            --no-fail-on-empty-changeset

  # -----------------------------------------------------------------------
  # JOB 3: Deploy Monitoring Stack
  # -----------------------------------------------------------------------
  monitoring:
    name: Deploy Monitoring
    runs-on: ubuntu-latest
    needs: [infrastructure, application]
    steps:
      - uses: actions/checkout@v4
      - uses: aws-actions/configure-aws-credentials@v4
        with:
          aws-access-key-id: ${{ secrets.AWS_ACCESS_KEY_ID }}
          aws-secret-access-key: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
          aws-region: ${{ env.AWS_REGION }}

      - name: Deploy Monitoring Stack
        run: |
          aws cloudformation deploy \
            --template-file infra/cloudformation/monitoring.yaml \
            --stack-name ips-autopost-monitoring-${{ github.event.inputs.environment }} \
            --parameter-overrides \
              Environment=${{ github.event.inputs.environment }} \
            --no-fail-on-empty-changeset
```

---

## 12. Data Flow Diagrams

### 12.1 Scheduled Post Flow (e.g. InvitedClub at 8 AM)

```
EventBridge Scheduler
  Rule: cron(0 8 * * ? *)
  Target: ips-post-queue-{env}
  Input: {"JobId":371,"ClientType":"INVITEDCLUB","Pipeline":"Post","TriggerType":"Scheduled"}
        |
        v
SQS: ips-post-queue-{env}
  VisibilityTimeout: 7200s
  Message waits until PostWorker picks it up
        |
        v
ECS Fargate: PostWorker
  PostWorker.ExecuteAsync() polls SQS (long polling, 20s wait)
  Receives message -> calls AutoPostOrchestrator.RunScheduledPostAsync(371, "INVITEDCLUB")
        |
        v
AutoPostOrchestrator
  1. Load GenericJobConfig from generic_job_configuration WHERE job_id=371
  2. Load schedules from generic_execution_schedule
  3. Check IsExecuteFileCreation() -> within 30-min window? YES
  4. Check AllowAutoPost -> true
  5. Resolve InvitedClubPlugin from PluginRegistry
  6. Call plugin.OnBeforePostAsync() -> RetryPostImages runs
  7. Fetch workitems: SELECT w.ItemId, w.StatusId FROM Workitems w
                     JOIN WFInvitedClubsIndexHeader h ON w.ItemId=h.UID
                     WHERE JobId=371 AND StatusId IN (100)
                     AND ISNULL(h.PostInProcess,0)=0
        |
        v
InvitedClubPlugin.ExecutePostAsync()
  For each workitem:
    SET PostInProcess=1
    Get image from S3 (ips-invoice-images/)
    Build InvoiceRequest JSON
    Apply UseTax logic (strip ShipToLocation if NO)
        |
        v
Oracle Fusion Cloud API
  POST {PostServiceURL}                    -> HTTP 201 -> InvoiceId
  POST {PostServiceURL}/{InvoiceId}/child/attachments -> HTTP 201 -> AttachedDocumentId
  POST {PostServiceURL}/action/calculateTax (if UseTax=YES) -> HTTP 200
        |
        v
RDS SQL Server (Workflow DB)
  UPDATE WFInvitedClubsIndexHeader SET InvoiceId=... WHERE UID=@uid
  UPDATE WFInvitedClubsIndexHeader SET AttachedDocumentId=... WHERE UID=@uid
  EXEC WORKITEM_ROUTE @itemID, @Qid=200, @userId, @operationType='Automatic Route:'
  EXEC GENERALLOG_INSERT @operationType='Post To InvitedClubs', @sourceObject='Contents'
  INSERT INTO post_to_invitedclub_history (...)
  SET PostInProcess=0 (in finally block)
        |
        v
AutoPostOrchestrator
  UPDATE generic_job_configuration SET last_post_time=NOW() WHERE id=config.Id
  INSERT INTO generic_execution_history (status, records_processed, ...)
        |
        v
PostWorker
  DELETE SQS message (job complete)
  CloudWatch Metrics: PostSuccessCount, PostFailedCount, PostDurationSeconds
```

### 12.2 Manual Post Flow (User clicks "Post Now" in Workflow UI)

```
Workflow UI
  HTTP POST /api/post/371/items/4521
  Header: x-api-key: {key}
        |
        v
API Gateway
  Validates API key
  Routes to IPS.AutoPost.Api (direct HTTP — NOT through SQS)
        |
        v
PostController.PostItems(jobId="371", itemIds="4521", userId=123)
  Calls AutoPostOrchestrator.RunManualPostAsync("4521", 123)
        |
        v
AutoPostOrchestrator
  1. GetWorkitemsByItemIds("4521")
     -> SELECT W.* FROM Workitems W WHERE ItemId IN (SELECT * FROM dbo.split('4521', ', '))
  2. Read StatusId from first row -> e.g. 100
  3. Find config where SourceQueueId == 100 -> InvitedClub config
  4. Resolve InvitedClubPlugin
  5. Call plugin.OnBeforePostAsync() (RetryPostImages)
  6. Build PostContext { TriggerType="Manual", ItemIds="4521", UserId=123 }
        |
        v
InvitedClubPlugin.ExecutePostAsync()
  Processes only item 4521
  operationType = "Manual Route:" (because processManually=true)
  manually_posted = true in history
        |
        v
Oracle Fusion Cloud API
  (same 3-step flow as scheduled)
        |
        v
RDS SQL Server
  (same routing, history, log as scheduled)
        |
        v
PostController
  Returns HTTP 200:
  {
    "Status": "Success",
    "RecordsProcessed": 1,
    "RecordsSuccess": 1,
    "RecordsFailed": 0,
    "ItemResults": [{"ItemId":4521,"IsSuccess":true,"DestinationQueue":200}]
  }
```

### 12.3 Feed Download Flow (InvitedClub at 3 AM)

```
EventBridge Scheduler
  Rule: cron(0 3 * * ? *)
  Target: ips-feed-queue-{env}
  Input: {"JobId":371,"ClientType":"INVITEDCLUB","Pipeline":"Feed","TriggerType":"Scheduled"}
        |
        v
SQS: ips-feed-queue-{env}
        |
        v
ECS Fargate: FeedWorker
  FeedWorker.ExecuteAsync() polls ips-feed-queue
  Receives message -> calls AutoPostOrchestrator.RunScheduledFeedAsync(371, "INVITEDCLUB")
        |
        v
AutoPostOrchestrator
  Load config, check DownloadFeed=true, check ExecuteDownloadData()
  Resolve InvitedClubPlugin
  Call plugin.ExecuteFeedDownloadAsync()
        |
        v
InvitedClubFeedStrategy.ExecuteAsync()
  1. LoadSupplierAsync()
     GET {DownloadServiceURL}/suppliers?onlyData=true&q=InactiveDate is null&limit=500&offset=0
     Paginate until HasMore=false
     TRUNCATE InvitedClubSupplier; BulkCopy all suppliers
        |
  2. LoadSupplierAddressAsync() [initial or incremental]
     GET /suppliers/{id}/child/addresses?onlyData=true&limit=500&offset=0 per supplier
     Inject SupplierId into each item
     Initial: TRUNCATE + BulkCopy
     Incremental: DELETE WHERE SupplierId IN (...) + BulkCopy
        |
  3. LoadSupplierSiteAsync() [same pattern as address]
     After insert: EXEC InvitedClub_UpdateSupplierSiteInSupplierAddress
        |
  4. Export supplier CSV
     EXEC InvitedClub_GetSupplierDataToExport
     Write pipe-delimited CSV to {FeedDownloadPath}\Supplier\Supplier_{timestamp}.csv
     UPDATE post_to_invitedclub_configuration SET last_supplier_download_time=NOW()
        |
  5. LoadCOAAsync()
     GET /accountCombinationsLOV?onlyData=true&q=_CHART_OF_ACCOUNTS_ID=5237;...
     TRUNCATE InvitedClubCOA; BulkCopy
     Write COA CSV to {FeedDownloadPath}\COA\COA_{timestamp}.csv
     Check missing CodeCombinationIds vs InvitedClubsCOAFullFeed
     If missing: export Excel, send email
        |
        v
AutoPostOrchestrator
  UPDATE generic_job_configuration SET last_download_time=NOW()
  INSERT INTO generic_execution_history (execution_type='FEED_DOWNLOAD', ...)
        |
        v
FeedWorker
  DELETE SQS message
  CloudWatch Metrics: FeedSuccessCount, FeedDurationSeconds
```

---

## 13. Key Design Decisions

### 13.1 ECS Fargate over Lambda

**Decision:** All invoice posting and feed download runs on ECS Fargate, not Lambda.

**Rationale:**
- Lambda has a hard 15-minute timeout. InvitedClub with 10,000+ invoices (3 API calls each at ~2.5s = 75,000 seconds) would time out.
- Media feed download takes 30-40 minutes (SOAP, 6 months of data in 10-day chunks).
- MDS TIFF processing uses GDI+ which requires a full OS environment.
- Fargate has no execution time limit.

**Lambda is still used** for the Scheduler (EventBridge sync) because that job takes < 5 seconds and runs every 10 minutes — a perfect Lambda use case.

### 13.2 Scale-to-Zero (DesiredCount=0)

**Decision:** ECS services start at DesiredCount=0 and scale to zero when idle.

**Rationale:**
- Validated by the existing `GenericMissingInvoicesProcess` production deployment which uses the same pattern.
- Cost optimization: services only run when there are messages in the queue.
- Tradeoff: 30-60 second cold start when the first message arrives after idle period. Acceptable for scheduled jobs (8 AM start, 30s delay is negligible).
- For manual posts where instant response is needed, the API Gateway → Web API → direct Fargate call pattern bypasses SQS entirely, so cold start is not a concern for manual triggers.

### 13.3 Three-Stack CloudFormation

**Decision:** Infrastructure, Application, and Monitoring are separate CloudFormation stacks.

**Rationale:**
- Infrastructure changes (VPC, SQS, ECR) are rare and risky — separate stack prevents accidental changes during app deployments.
- Application stack can be redeployed on every code push without touching networking.
- Monitoring stack can be updated independently (add new alarms, update dashboard).
- Cross-stack references via `Fn::ImportValue` ensure correct dependency ordering.
- Validated by `GenericMissingInvoicesProcess` production pattern.

### 13.4 Plugin Pattern

**Decision:** Client-specific logic is isolated in plugins implementing `IClientPlugin`.

**Rationale:**
- InvitedClub and Sevita have fundamentally different post flows (3-step vs single-step, Basic Auth vs OAuth2, feed download vs none).
- The generic core handles everything common: schedule checking, workitem fetching, PostInProcess flag, WORKITEM_ROUTE, GENERALLOG_INSERT, history, CloudWatch metrics.
- Adding a new client = one plugin class + one DB row. Zero changes to core.
- `OnBeforePostAsync` hook allows batch-level pre-loading (Sevita ValidIds) without polluting the core engine.
- `ClearPostInProcessAsync` override allows Sevita to use its SP instead of direct SQL.

### 13.5 NAT Gateway over VPC Endpoints

**Decision:** Use NAT Gateway for outbound traffic, not VPC Endpoints.

**Rationale:**
- Validated by `GenericMissingInvoicesProcess` production pattern — IPS already uses NAT Gateway.
- VPC Endpoints add complexity (one endpoint per service, interface endpoints cost ~$7/month each).
- NAT Gateway is simpler to configure and already exists in the IPS VPC.
- VPC Endpoints can be added later as a cost optimization without changing application code.

### 13.6 SQS Visibility Timeout = 7200 seconds (2 hours)

**Decision:** 2-hour visibility timeout instead of the commonly suggested 15-30 minutes.

**Rationale:**
- InvitedClub with 10,000 invoices at 3 API calls each at 2.5s = ~20 hours worst case.
- Even at 1,000 invoices = ~2 hours.
- If visibility timeout expires while processing, SQS makes the message visible again and another task picks it up — causing duplicate processing.
- 2 hours provides a safe buffer for large batches.
- Validated by `GenericMissingInvoicesProcess` which uses 7200s.

### 13.7 PostInProcess as New Behavior

**Decision:** The new platform adds `PostInProcess=1` before processing and `AND ISNULL(h.PostInProcess,0)=0` to the workitem query.

**Rationale:**
- The existing Windows Services do NOT have this protection. Two service instances could theoretically pick up the same invoice.
- With ECS Fargate scaling to multiple tasks, this protection is essential.
- The `PostInProcess` column already exists on header tables — we're just using it correctly.
- Always cleared in `finally` block to prevent stuck records.

### 13.8 Direct Fargate Call for Manual Posts

**Decision:** Manual posts from the Workflow UI call the Post Worker directly via HTTP, bypassing SQS.

**Rationale:**
- SQS is asynchronous — the user would get "Queued" status and need to poll for results.
- Direct HTTP call is synchronous — user gets immediate success/failure response.
- The Web API (IPS.AutoPost.Api) runs as a separate ECS service that calls the Post Worker's internal HTTP endpoint.
- Scheduled posts still go through SQS for durability and retry.

---

## 14. Correctness Properties — Test Implementation

### 14.1 PostInProcess Invariant

**Property:** FOR ALL workitems processed, `PostInProcess = 0` after completion regardless of outcome.

**Test approach:**
```csharp
[Property]
public Property PostInProcess_AlwaysClearedAfterProcessing()
{
    return Prop.ForAll(
        Arb.From(Gen.Choose(0, 3).Select(n => (FailureMode)n)),
        failureMode =>
        {
            // Arrange: set PostInProcess=1, simulate failure mode
            // Act: run orchestrator with simulated failure
            // Assert: PostInProcess=0 in DB after run
            var headerRow = GetHeaderRow(testItemId);
            return headerRow.PostInProcess == 0;
        });
}
// FailureMode: 0=Success, 1=ImageNotFound, 2=APIFail, 3=Exception
```

### 14.2 No-Duplicate Routing Invariant

**Property:** Each workitem ends in exactly one queue after processing.

**Test approach:**
```csharp
[Property]
public Property WorkitemRoutedToExactlyOneQueue()
{
    return Prop.ForAll(
        Gen.Elements(successScenario, imageFailScenario, apiFailScenario, validationFailScenario),
        scenario =>
        {
            var initialQueue = GetWorkitemQueue(testItemId);
            RunPostForScenario(scenario);
            var finalQueue = GetWorkitemQueue(testItemId);
            // Must have moved from source queue to exactly one destination
            return finalQueue != initialQueue && finalQueue != 0;
        });
}
```

### 14.3 History Completeness Invariant

**Property:** Count of history rows == count of workitems where API was attempted.

**Test approach:**
```csharp
[Property]
public Property HistoryWrittenForAllApiAttempts()
{
    return Prop.ForAll(
        Gen.ListOf(Gen.Choose(1, 100)).Select(ids => ids.Distinct().ToList()),
        itemIds =>
        {
            var apiAttemptCount = RunBatchAndCountApiAttempts(itemIds);
            var historyCount = CountHistoryRows(itemIds);
            return historyCount == apiAttemptCount;
            // Note: image-not-found and RequesterId-empty do NOT count as API attempts
        });
}
```

### 14.4 UseTax Round-Trip Property

**Property:** UseTax=NO → no ShipToLocation in payload. UseTax=YES → ShipToLocation present.

**Test approach:**
```csharp
[Property]
public Property UseTaxTransformationIsCorrect()
{
    return Prop.ForAll(
        Arb.From(Gen.Elements("YES", "NO")),
        useTax =>
        {
            var payload = BuildInvoiceRequestJson(CreateHeaderWithUseTax(useTax), CreateDetailRows());
            var json = JObject.Parse(payload);
            var lines = (JArray)json["invoiceLines"]!;

            if (useTax == "NO")
                return lines.All(l => l["ShipToLocation"] == null);
            else
                return lines.All(l => l["ShipToLocation"] != null);
        });
}
```

### 14.5 Feed Idempotence Property

**Property:** Running feed download twice produces same row count as running once.

**Test approach:**
```csharp
[Property]
public Property FeedDownloadIsIdempotent()
{
    return Prop.ForAll(
        Gen.Choose(100, 10000).Select(n => n),
        supplierCount =>
        {
            // Mock Oracle Fusion API to return supplierCount suppliers
            RunFeedDownload();
            var countAfterFirst = GetTableRowCount("InvitedClubSupplier");
            RunFeedDownload();  // Run again
            var countAfterSecond = GetTableRowCount("InvitedClubSupplier");
            return countAfterFirst == countAfterSecond;
        });
}
```

### 14.6 Incremental Feed Subset Property (Metamorphic)

**Property:** Incremental supplier IDs fetched ⊆ full supplier ID list.

**Test approach:**
```csharp
[Property]
public Property IncrementalFeedIsSubsetOfFull()
{
    return Prop.ForAll(
        Gen.Choose(1, 30).Select(daysBack => daysBack),
        daysBack =>
        {
            var allSupplierIds = GetAllSupplierIds();
            var incrementalIds = GetIncrementalSupplierIds(daysBack);
            return incrementalIds.IsSubsetOf(allSupplierIds) &&
                   incrementalIds.Count <= allSupplierIds.Count;
        });
}
```

### 14.7 Pagination Completeness Property

**Property:** Total records inserted == sum of all items across all pages.

**Test approach:**
```csharp
[Property]
public Property PaginationReturnsAllRecords()
{
    return Prop.ForAll(
        Gen.Choose(1, 5000).Select(total => total),
        totalRecords =>
        {
            // Mock API to return totalRecords across ceil(totalRecords/500) pages
            var inserted = RunFeedDownloadAndCountInserted();
            return inserted == totalRecords;
        });
}
```

### 14.8 Error Condition Routing Property

**Property:** Image-not-found → EdenredFailPostQueueId, no API call. RequesterId-empty → InvitedFailPostQueueId, no API call.

**Test approach:**
```csharp
[Property]
public Property ErrorConditionsRouteCorrectlyWithoutApiCall()
{
    return Prop.ForAll(
        Gen.Elements(ErrorCondition.ImageNotFound, ErrorCondition.RequesterIdEmpty),
        condition =>
        {
            var apiCallCount = 0;
            MockOracleFusionApi(onCall: () => apiCallCount++);
            var result = RunPostWithCondition(condition);
            var expectedQueue = condition == ErrorCondition.ImageNotFound
                ? EdenredFailPostQueueId : InvitedFailPostQueueId;
            return apiCallCount == 0 && result.DestinationQueue == expectedQueue;
        });
}
```

### 14.9 Retry Idempotence Property

**Property:** ImagePostRetryCount increments by exactly 1 per attempt, stops at limit.

**Test approach:**
```csharp
[Property]
public Property RetryCountIncrementsExactlyOnce()
{
    return Prop.ForAll(
        Gen.Choose(1, 10).Select(limit => limit),
        retryLimit =>
        {
            SetImagePostRetryLimit(retryLimit);
            var initialCount = GetImagePostRetryCount(testItemId);
            RunRetryPostImages();
            var finalCount = GetImagePostRetryCount(testItemId);
            return finalCount == initialCount + 1 && finalCount <= retryLimit;
        });
}
```

### 14.10 SQS Message Delivery Guarantee

**Property:** Every SQS message is either processed (deleted) or moved to DLQ after exactly 3 failures.

**Test approach:**
```csharp
[Property]
public Property SqsMessageNeverSilentlyDiscarded()
{
    return Prop.ForAll(
        Gen.Choose(1, 3).Select(failCount => failCount),
        failCount =>
        {
            // Simulate failCount failures then success (or 3 failures -> DLQ)
            var dlqCount = GetDLQMessageCount();
            var processedCount = GetProcessedMessageCount();
            RunWithSimulatedFailures(failCount);

            if (failCount < 3)
                return GetProcessedMessageCount() == processedCount + 1;
            else
                return GetDLQMessageCount() == dlqCount + 1;
        });
}
```

---

*Design Document Version: 1.0*
*Feature: generic-autopost-platform*
*Target: .NET Core 10 | AWS ECS Fargate | SQL Server RDS | GitHub Actions*
*Date: April 29, 2026*
