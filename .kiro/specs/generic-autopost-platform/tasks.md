# Implementation Plan: IPS.AutoPost Platform

> **Based on:** requirements.md + design.md
> **Target:** .NET Core 10 | AWS ECS Fargate | SQL Server RDS

---

## Phase 1: Solution Scaffold and Core Infrastructure

- [x] 1. Create solution and project structure
  - [x] 1.1 Create IPS.AutoPost.Platform.sln
  - [x] 1.2 Create IPS.AutoPost.Core.csproj (.NET 10, packages: AWSSDK.SQS, AWSSDK.S3, AWSSDK.SecretsManager, AWSSDK.CloudWatch, AWSSDK.CloudWatchLogs, Microsoft.Data.SqlClient, MediatR, FluentValidation, Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.SqlServer, Microsoft.EntityFrameworkCore.Tools, Microsoft.Extensions.Hosting, Serilog, Serilog.Sinks.AmazonCloudWatch)
  - [x] 1.3 Create IPS.AutoPost.Plugins.csproj (.NET 10, references Core, packages: RestSharp, Newtonsoft.Json, ClosedXML)
  - [x] 1.4 Create IPS.AutoPost.Host.FeedWorker.csproj (.NET 10 Worker Service, references Core + Plugins, packages: AWSSDK.SQS)
  - [x] 1.5 Create IPS.AutoPost.Host.PostWorker.csproj (.NET 10 Worker Service, references Core + Plugins, packages: AWSSDK.SQS)
  - [x] 1.6 Create IPS.AutoPost.Api.csproj (ASP.NET Core 10, references Core + Plugins)
  - [x] 1.7 Create IPS.AutoPost.Scheduler.csproj (AWS Lambda .NET 10, references Core)
  - [x] 1.8 Create IPS.AutoPost.Core.Tests.csproj (xUnit v3 — xunit.v3 package, FsCheck, Moq, FluentAssertions, Microsoft.EntityFrameworkCore.InMemory, coverlet.collector)
  - [x] 1.9 Create IPS.AutoPost.Plugins.Tests.csproj (xUnit v3, FsCheck, Moq, FluentAssertions, WireMock.Net, Microsoft.EntityFrameworkCore.InMemory, coverlet.collector)
  - [x] 1.10 Create Directory.Packages.props (centralized NuGet version management with all packages pinned)
  - [x] 1.11 Create Directory.Build.props (shared MSBuild properties: LangVersion=latest, Nullable=enable, ImplicitUsings=enable)
  - [x] 1.12 Create global.json (pins .NET SDK to 10.0.100)
  - [x] 1.13 Create all folder structures per SOLUTION_STRUCTURE.md (Commands/, Handlers/, Behaviors/, Infrastructure/, Migrations/ in Core)

- [x] 2. Implement Core Data Access
  - [x] 2.1 Implement SqlHelper.cs (async ExecuteDatasetAsync, ExecuteNonQueryAsync, ExecuteScalarAsync, BulkCopyAsync, Param factory)
  - [x] 2.2 Implement DataTableExtensions.cs (GenerateHtmlTable, ToDataTable<T>, ConvertDataTable<T>)
  - [x] 2.3 Write unit tests for SqlHelper parameter factory and BulkCopy

- [x] 3. Implement Core Interfaces and Models
  - [x] 3.1 Implement IClientPlugin interface (ExecutePostAsync, ExecuteFeedDownloadAsync default, OnBeforePostAsync default, ClearPostInProcessAsync default)
  - [x] 3.2 Implement GenericJobConfig model with all fields and GetClientConfig<T>()
  - [x] 3.3 Implement PostContext, PostBatchResult, PostItemResult models
  - [x] 3.4 Implement FeedResult (NotApplicable, Succeeded, Failed factory methods) and FeedContext
  - [x] 3.5 Implement ScheduleConfig, EdenredApiUrlConfig, GenericPostHistory, GenericExecutionHistory models
  - [x] 3.6 Implement PluginRegistry with PluginNotFoundException
  - [x] 3.7 Implement DynamicRecord model (Dictionary<string, object?> Fields, GetValue<T>(string fieldName) method — schema-agnostic data model for GenericRestPlugin)
  - [x] 3.8 Implement ICloudWatchMetricsService interface (12 metric methods)
  - [x] 3.9 Implement ICorrelationIdService interface (GetOrCreateCorrelationId, SetCorrelationId returning IDisposable)

- [x] 3A. Implement MediatR Commands, Handlers, and Pipeline Behaviors
  - [x] 3A.1 Implement ExecutePostCommand (IRequest<PostBatchResult>: JobId, ClientType, TriggerType, ItemIds, UserId, Mode?)
  - [x] 3A.2 Implement ExecuteFeedCommand (IRequest<FeedResult>: JobId, ClientType, TriggerType, Mode?)
  - [x] 3A.3 Implement ExecutePostHandler (IRequestHandler<ExecutePostCommand, PostBatchResult> — delegates to AutoPostOrchestrator)
  - [x] 3A.4 Implement ExecuteFeedHandler (IRequestHandler<ExecuteFeedCommand, FeedResult> — delegates to AutoPostOrchestrator)
  - [x] 3A.5 Implement LoggingBehavior<TRequest, TResponse> (IPipelineBehavior — logs command start/end with CorrelationId from ICorrelationIdService)
  - [x] 3A.6 Implement ValidationBehavior<TRequest, TResponse> (IPipelineBehavior — runs IValidator<TRequest> via FluentValidation, throws ValidationException on failure)
  - [x] 3A.7 Register MediatR in ServiceCollectionExtensions: services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...))
  - [x] 3A.8 Register pipeline behaviors: services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>)) and ValidationBehavior
  - [x] 3A.9 Write unit tests for ExecutePostHandler (command dispatched, orchestrator called, result returned)
  - [x] 3A.10 Write unit tests for LoggingBehavior (log entries written before and after handler)
  - [x] 3A.11 Write unit tests for ValidationBehavior (valid command passes through, invalid command throws ValidationException)

- [x] 4. Implement Core Repository Interfaces and Implementations
  - [x] 4.1 Define IConfigurationRepository (GetByJobIdAsync, GetBySourceQueueIdAsync, GetEdenredApiUrlConfigAsync, UpdateLastPostTimeAsync, UpdateLastDownloadTimeAsync)
  - [x] 4.2 Define IWorkitemRepository (GetWorkitemsAsync with PostInProcess=0 filter, GetWorkitemsByItemIdsAsync)
  - [x] 4.3 Define IRoutingRepository (RouteWorkitemAsync, SetPostInProcessAsync, ClearPostInProcessAsync, ExecuteSpAsync)
  - [x] 4.4 Define IAuditRepository (AddGeneralLogAsync, SavePostHistoryAsync, SaveExecutionHistoryAsync, GetExecutionHistoryAsync)
  - [x] 4.5 Define IScheduleRepository (GetSchedulesAsync)
  - [x] 4.6 Implement ConfigurationRepository using SqlHelper
  - [x] 4.7 Implement WorkitemRepository using SqlHelper (query includes AND ISNULL(h.PostInProcess,0)=0)
  - [x] 4.8 Implement RoutingRepository using SqlHelper (WORKITEM_ROUTE SP, direct SQL UPDATE)
  - [x] 4.9 Implement AuditRepository using SqlHelper (GENERALLOG_INSERT SP, generic_post_history INSERT, generic_execution_history INSERT)
  - [x] 4.10 Implement ScheduleRepository using SqlHelper (GetExecutionSchedule SP with @file_creation_config_id + @job_id)

- [x] 5. Implement Core Services
  - [x] 5.1 Implement SecretsManagerConfigurationProvider in Infrastructure/ folder (scan `ConnectionStrings`, `Email:SmtpPassword`, `ApiKey:Value` for "/" prefixed values; fetch from Secrets Manager in parallel with 30s timeout; handle JSON secrets with `AppConnectionString` key; inject into IConfiguration via AddInMemoryCollection — called via `await builder.Configuration.AddSecretsManagerAsync()` in Program.cs)
  - [x] 5.2 Implement CorrelationIdService (AsyncLocal<string> storage, SetCorrelationId returns IDisposable that calls LogContext.PushProperty("CorrelationId", id))
  - [x] 5.3 Implement ICloudWatchMetricsService interface (12 methods: PostStarted, PostCompleted, PostFailed, PostSuccessCount, PostFailedCount, PostDurationSeconds, FeedStarted, FeedCompleted, FeedRecordsDownloaded, FeedDurationSeconds, ImageRetryAttempted, ImageRetrySucceeded)
  - [x] 5.4 Implement CloudWatchMetricsService (PutMetricDataAsync to namespace IPS/AutoPost/{env}, dimensioned by ClientType + JobId)
  - [x] 5.5 Implement ConfigurationService (Secrets Manager GetSecretAsync with in-memory cache, fallback to appsettings)
  - [x] 5.6 Implement S3ImageService (GetBase64ImageAsync wrapping S3Utility, UploadFileAsync)
  - [x] 5.7 Implement SchedulerService (IsExecuteFileCreation: LastPostTime > 30 min ago AND current time within 30-min window of scheduled time)
  - [x] 5.8 Implement EmailService (SMTP send with To/CC/BCC arrays, HTML body, optional attachment)
  - [x] 5.9 Write unit tests for CorrelationIdService (AsyncLocal isolation between concurrent tasks, LogContext property set)
  - [x] 5.10 Write unit tests for CloudWatchMetricsService (correct namespace, correct dimensions)

- [x] 6. Implement AutoPostOrchestrator
  - [x] 6.1 Implement RunScheduledPostAsync (load config, check schedule, OnBeforePostAsync, fetch workitems, ExecutePostAsync, UpdateLastPostTime, write execution history)
  - [x] 6.2 Implement RunManualPostAsync (GetWorkitemsByItemIds, resolve config by StatusId, OnBeforePostAsync, ExecutePostAsync)
  - [x] 6.3 Implement RunScheduledFeedAsync (load config, check DownloadFeed flag, ExecuteFeedDownloadAsync, UpdateLastDownloadTime)
  - [x] 6.4 Implement ExecutePostBatchAsync (shared by scheduled + manual: set PostInProcess=1, call plugin, ClearPostInProcessAsync in finally, write execution history)
  - [x] 6.5 Write unit tests for RunScheduledPostAsync (schedule window check, AllowAutoPost=false skip, no workitems path)
  - [x] 6.6 Write unit tests for RunManualPostAsync (StatusId resolution, Missing Configuration response)
  - [x] 6.7 Write unit tests for SchedulerService.IsExecuteFileCreation (boundary conditions: exactly at window, before window, after window)


## Phase 2: Database Migration — EF Core + Seed Scripts

- [x] 7. Create EF Core DatabaseContext and migrations (generic tables only)
  - [x] 7.1 Implement AutoPostDatabaseContext (EF Core DbContext with DbSet for all 10 generic tables — NO DbSet for existing Workflow tables)
  - [x] 7.2 Configure entity mappings in OnModelCreating (all 10 generic tables with correct column types, constraints, indexes)
  - [x] 7.3 Create initial EF Core migration: `dotnet ef migrations add InitialGenericTables --project src/IPS.AutoPost.Core`
  - [x] 7.4 Verify generated migration SQL matches design.md section 5 schema exactly
  - [x] 7.5 Add `context.Database.Migrate()` call in FeedWorker and PostWorker Program.cs (auto-apply at startup)
  - [x] 7.6 Write integration test for EF Core migration using InMemory database (all 10 tables created, constraints enforced)

- [x] 8. Create data seed scripts (existing client configurations)
  - [x] 8.1 Create INSERT script: populate generic_job_configuration from post_to_invitedclub_configuration (client_type='INVITEDCLUB')
  - [x] 8.2 Create INSERT script: populate generic_execution_schedule from GetExecutionSchedule data for InvitedClub
  - [x] 8.3 Create INSERT script: populate generic_job_configuration from post_to_sevita_configuration (client_type='SEVITA')
  - [x] 8.4 Create INSERT script: populate generic_execution_schedule for Sevita
  - [x] 8.5 Verify all existing stored procedures still callable with unchanged parameters (get_invitedclub_configuration, GetExecutionSchedule, InvitedClub_GetHeaderAndDetailData, InvitedClub_GetFailedImagesData, WORKITEM_ROUTE, GENERALLOG_INSERT, get_sevita_configurations, GetSevitaHeaderAndDetailDataByItem, UpdateSevitaHeaderPostFields)

---

## Phase 3: InvitedClub Plugin

- [x] 9. Create InvitedClub models and constants
  - [x] 9.1 Create InvitedClubConfig.cs (ImagePostRetryLimit, EdenredFailQueueId, InvitedFailQueueId, FeedDownloadTime, LastSupplierDownloadTime)
  - [x] 9.2 Create InvoiceRequest.cs (InvoiceRequest, InvoiceLine, InvoiceDistribution, InvoiceDff)
  - [x] 9.3 Create AttachmentRequest.cs
  - [x] 9.4 Create InvoiceCalculateTaxRequest.cs
  - [x] 9.5 Create InvoiceResponse.cs (InvoiceResponse, AttachmentResponse, InvoiceCalculateTaxResponse)
  - [x] 9.6 Create SupplierResponse.cs (SupplierResponse, SupplierData with HasMore/Count/Limit/Offset)
  - [x] 9.7 Create SupplierAddressResponse.cs (SupplierAddressResponse, SupplierAddressData)
  - [x] 9.8 Create SupplierSiteResponse.cs (SupplierSiteResponse, SupplierSiteData)
  - [x] 9.9 Create COAResponse.cs (COAResponse with JsonProperty attributes for _CODE_COMBINATION_ID etc., COAData)
  - [x] 9.10 Create FailedImagesData.cs (InvoiceId, ItemId, ImagePostRetryCount, ImagePath)
  - [x] 9.11 Create PostHistory.cs (InvitedClub-specific: ItemId, InvoiceRequestJson, InvoiceResponseJson, AttachmentRequestJson, AttachmentResponseJson, CalculateTaxRequestJson, CalculateTaxResponseJson, ManuallyPosted, PostedBy)
  - [x] 9.12 Create EmailConfig.cs (SMTPServer, SMTPServerPort, Username, Password, EmailFrom, EmailFromUser, SMTPUseSSL, EmailTo, EmailCC, EmailBCC, EmailToArr, EmailCCArr, EmailBCCArr, EmailSubject, EmailTemplate, EmailToHelpDesk, EmailSubjectImageFail, EmailTemplateImageFail)
  - [x] 9.13 Create APIResponseType.cs (ResponseType, ResponseCode, ResponseMessage)
  - [x] 9.14 Create InvitedClubConstants.cs (all SP names, API URIs, Content-Types, table names, log strings)

- [x] 10. Implement InvitedClubFeedStrategy
  - [x] 10.1 Implement LoadSupplierAsync (paginated GET, Basic Auth, Timeout=-1, all suppliers including inactive)
  - [x] 10.2 Implement IsInitialCallAsync (SELECT COUNT(*) FROM {tableName})
  - [x] 10.3 Implement LoadSupplierAddressAsync (per-supplier paginated GET, inject SupplierId into each item after deserialization, initial vs incremental logic using LastSupplierDownloadTime-2days)
  - [x] 10.4 Implement LoadSupplierSiteAsync (same pattern as address, call InvitedClub_UpdateSupplierSiteInSupplierAddress SP after insert)
  - [x] 10.5 Implement BulkInsertAsync (truncate-then-insert for full refresh; DELETE WHERE SupplierId IN (...) then insert for incremental)
  - [x] 10.6 Implement LoadCOAAsync (paginated GET, truncate-then-insert, write pipe-delimited CSV, check missing CodeCombinationIds vs InvitedClubsCOAFullFeed, send email if missing)
  - [x] 10.7 Implement ExportSupplierCsvAsync (call InvitedClub_GetSupplierDataToExport SP, write pipe-delimited CSV to FeedDownloadPath\Supplier\Supplier_{timestamp}.csv; after success call UpdateSupplierLastDownloadTime(@configurations_id) SP to update last_supplier_download_time in post_to_invitedclub_configuration)
  - [x] 10.8 Implement ExecuteAsync (orchestrate all feed steps, update last_supplier_download_time after supplier steps, return FeedResult)
  - [x] 10.9 Write unit tests for LoadSupplierAddressAsync (initial call uses all IDs, incremental uses filtered IDs, SupplierId injection)
  - [x] 10.10 Write unit tests for LoadCOAAsync (missing COA detection, email trigger condition)

- [x] 11. Implement InvitedClubRetryService
  - [x] 11.1 Implement RetryPostImagesAsync (call InvitedClub_GetFailedImagesData SP with @HeaderTable, @ImagePostRetryLimit, @InvitedFailPostQueueId)
  - [x] 11.2 Implement RetryOneImageAsync (get image from S3 or local, POST attachment with Content-Type: application/vnd.oracle.adf.resourceitem+json, on HTTP 201 update AttachedDocumentId + route to SuccessQueueId, always increment ImagePostRetryCount, always use config.DefaultUserId, always use "Automatic Route:")
  - [x] 11.3 Write unit tests for RetryPostImagesAsync (no records path, success path, failure path, retry count increment)

- [x] 12. Implement InvitedClubPostStrategy
  - [x] 12.1 Implement GetImageAsync (S3 path for non-legacy, local file for legacy, return base64+fileName+failed flag)
  - [x] 12.2 Implement BuildInvoiceRequestJson (map header+detail DataSet to InvoiceRequest, apply UseTax=NO logic to strip ShipToLocation using JObject manipulation)
  - [x] 12.3 Implement PostInvoiceAsync (POST to PostServiceURL, Content-Type: application/json, Basic Auth, Timeout=-1, expect HTTP 201, extract InvoiceId from response JSON; on non-201: call UpdateGLDateValue to set GlDate=NULL on WFInvitedClubsIndexHeader, route to InvitedFailPostQueueId)
  - [x] 12.4 Implement PostInvoiceAttachmentAsync (POST to {PostServiceURL}/{invoiceId}/child/attachments, Content-Type: application/vnd.oracle.adf.resourceitem+json, Basic Auth, Timeout=-1, expect HTTP 201, extract AttachedDocumentId)
  - [x] 12.5 Implement PostCalculateTaxAsync (POST to {PostServiceURL}/action/calculateTax, Content-Type: application/vnd.oracle.adf.action+json, Basic Auth, Timeout=-1, use AddJsonBody not AddParameter, expect HTTP 200)
  - [x] 12.6 Implement SaveHistoryAsync (INSERT into post_to_invitedclub_history — only called when at least one API call was attempted, NOT for image-not-found or RequesterId-empty early exits)
  - [x] 12.7 Implement ExecuteAsync main loop (for each workitem: set PostInProcess=1, get image from S3 (non-legacy) or local path {image_parent_path}{ImagePath} (legacy) using DownloadServiceURL for feed and PostServiceURL for post, validate RequesterId, build payload, PostInvoice, PostAttachment, PostCalculateTax if UseTax=YES, route to success/fail queue, write history to WFInvitedClubsIndexHeader + post_to_invitedclub_history, clear PostInProcess in finally; call GENERALLOG_INSERT with operationType="Post To InvitedClubs" and sourceObject="Contents" on both success and failure paths; after loop: send image failure email if any failed)
  - [x] 12.8 Implement image failure email (to emailConfig.EmailToHelpDesk split by ';', use emailConfig.EmailTemplateImageFail as HTML template, replace #MissingImagesTable# placeholder with GenerateHtmlTable() output, use emailConfig.EmailSubjectImageFail as subject; send ONLY when EmailTemplateImageFail is not null/whitespace AND EmailToHelpDesk has non-zero length)
  - [x] 12.9 Write unit tests for BuildInvoiceRequestJson (UseTax=YES keeps ShipToLocation, UseTax=NO removes ShipToLocation from all lines)
  - [x] 12.10 Write unit tests for ExecuteAsync (image not found -> EdenredFailPostQueueId, no API call; RequesterId empty -> InvitedFailPostQueueId, no API call; invoice POST fail -> clear GLDate, route to InvitedFailPostQueueId; attachment fail -> route to EdenredFailPostQueueId; calculateTax fail -> route to InvitedFailPostQueueId; full success -> route to SuccessQueueId)
  - [x] 12.11 Write unit tests for PostInProcess flag (set before API call, cleared in finally even on exception)

- [x] 13. Implement InvitedClubPlugin and register
  - [x] 13.1 Implement InvitedClubPlugin.OnBeforePostAsync (calls RetryService.RetryPostImagesAsync)
  - [x] 13.2 Implement InvitedClubPlugin.ExecutePostAsync (delegates to PostStrategy)
  - [x] 13.3 Implement InvitedClubPlugin.ExecuteFeedDownloadAsync (delegates to FeedStrategy)
  - [x] 13.4 Register InvitedClubPlugin in PluginRegistration.cs
  - [x] 13.5 Write integration test: full InvitedClub scheduled post flow (mock Oracle Fusion API, real test DB, verify routing + history + PostInProcess cleared)
  - [x] 13.6 Write integration test: full InvitedClub feed download flow (mock Oracle Fusion API, verify supplier/address/site/COA tables populated)

---

## Phase 4: Sevita Plugin

- [x] 14. Create Sevita models and constants
  - [x] 14.1 Create SevitaConfig.cs (IsPORecord, PostJsonPath, TDriveLocation, NewUiTDriveLocation, RemotePath, ApiAccessTokenUrl, ClientId, ClientSecret, TokenExpirationMin)
  - [x] 14.2 Create InvoiceRequest.cs (InvoiceRequest with vendorId/employeeId/payAlone/invoiceRelatedToZycusPurchase/zycusInvoiceNumber/invoiceNumber/invoiceDate/expensePeriod/checkMemo/cerfTrackingNumber/remittanceRequired/edenredInvoiceId, InvoiceLine with alias/amount/naturalAccountNumber/edenredLineItemId, AttachmentRequest with fileName/fileBase/fileUrl/docid)
  - [x] 14.3 Create InvoiceResponse.cs (InvoiceResponse with Status/InvoiceId/Result/ErrorMsg, InvoicePostResponse)
  - [x] 14.4 Create ValidIds.cs (HashSet<string> VendorIds, HashSet<string> EmployeeIds)
  - [x] 14.5 Create PostHistory.cs (Sevita-specific: ItemId, InvoiceRequestJson, InvoiceResponseJson, ManuallyPosted, PostedBy, Comment)
  - [x] 14.6 Create PostFailedRecord.cs (SupplierName, ApproverName, InvoiceDate, DocumentId, IsSendNotification, FailureReason)
  - [x] 14.7 Create SevitaConstants.cs

- [x] 15. Implement Sevita services
  - [x] 15.1 Implement SevitaTokenService.GetAuthTokenAsync (POST to api_access_token_url with grant_type=client_credentials, client_id, client_secret; Content-Type: application/x-www-form-urlencoded; cache token until TokenExpirationMin elapsed)
  - [x] 15.2 Implement SevitaValidationService.ValidateLineSum (SUM(lineItem.amount) == header.InvoiceAmount)
  - [x] 15.3 Implement SevitaValidationService.ValidatePO (required fields: vendorId/invoiceDate/invoiceNumber/checkMemo; vendorId in VendorIds; default checkMemo="PO#" if empty)
  - [x] 15.4 Implement SevitaValidationService.ValidateNonPO (required fields: vendorId/employeeId/invoiceDate/invoiceNumber/checkMemo/expensePeriod; both IDs in ValidIds; cerfTrackingNumber required if any line naturalAccountNumber="174098")
  - [x] 15.5 Implement SevitaValidationService.ValidateAttachments (fileName/fileBase/fileUrl/docid all required)
  - [x] 15.6 Write unit tests for SevitaValidationService (PO missing fields, Non-PO missing fields, line sum mismatch, cerfTrackingNumber rule, attachment validation)
  - [x] 15.7 Write unit tests for SevitaTokenService (token cached on second call, token refreshed after expiry)

- [x] 16. Implement SevitaPostStrategy
  - [x] 16.1 Implement BuildLineItems (group detail rows by alias+naturalAccountNumber, sum LineAmount per group, format amount to 2 decimal places, edenredLineItemId = edenredInvoiceId + "_" + lineItemCount)
  - [x] 16.2 Implement SerializePayload (serialize InvoiceRequest then wrap in JSON array "[{...}]")
  - [x] 16.3 Implement UploadAuditJsonAsync (if post_json_path configured: write JSON to temp file, upload to S3 at {post_json_path}/{itemId}_{timestamp}.json, delete temp file)
  - [x] 16.4 Implement PostInvoiceAsync (POST to InvoicePostURL, Authorization: Bearer {token}, Timeout=-1; use request.AddParameter("application/json", invoiceRequestJson, ParameterType.RequestBody) NOT AddJsonBody; HTTP 201: extract InvoiceId from invoiceIds first property name; HTTP 500: special error message "Internal Server error occurred while posting invoice."; other: extract recordErrors/message/invoiceIds/failedRecords; load api_response_configuration via GetAPIResponseTypes at start of PostData for manual posts)
  - [x] 16.5 Implement SaveHistoryAsync (INSERT into sevita_posted_records_history with fileBase=null on all attachments — use JArray to null out fileBase before saving)
  - [x] 16.6 Implement SendNotificationEmailAsync (to FailedPostConfiguration.EmailTo split by ';', HTML table from failed records excluding IsSendNotification column, uses GenerateHtmlTable())
  - [x] 16.7 Implement ExecuteAsync main loop (for each workitem: get image from S3 always, validate, build payload, upload audit JSON, PostInvoice, route to success/fail, save history, call UpdateSevitaHeaderPostFields SP; after loop: send notification email if any failed)
  - [x] 16.8 Write unit tests for BuildLineItems (grouping by alias+naturalAccountNumber, amount summing, edenredLineItemId format)
  - [x] 16.9 Write unit tests for SerializePayload (output is valid JSON array, fileBase present in payload)
  - [x] 16.10 Write unit tests for SaveHistoryAsync (fileBase is null in stored JSON, other fields preserved)
  - [x] 16.11 Write unit tests for ExecuteAsync (image not found -> FailedPostsQueueId + history written; validation fail -> FailedPostsQueueId, no API call; HTTP 201 -> SuccessQueueId; HTTP 500 -> FailedPostsQueueId with special message)

- [x] 17. Implement SevitaPlugin and register
  - [x] 17.1 Implement SevitaPlugin.OnBeforePostAsync (load ValidIds from Sevita_Supplier_SiteInformation_Feed + Sevita_Employee_Feed)
  - [x] 17.2 Implement SevitaPlugin.ExecutePostAsync (delegates to PostStrategy, passes _validIds)
  - [x] 17.3 Implement SevitaPlugin.ClearPostInProcessAsync (override: call UpdateSevitaHeaderPostFields(@UID) SP)
  - [x] 17.4 Register SevitaPlugin in PluginRegistration.cs
  - [x] 17.5 Write integration test: full Sevita scheduled post flow (mock Sevita API + token endpoint, real test DB, verify routing + history with fileBase=null + UpdateSevitaHeaderPostFields called)

---

## Phase 5: Host Workers and API

- [ ] 18. Implement FeedWorker
  - [ ] 18.1 Implement FeedWorker.cs BackgroundService (SQS long polling 20s, MaxNumberOfMessages=10, create new DI scope per message, deserialize SqsMessagePayload, send ExecuteFeedCommand via IMediator, delete message on success, log error and do NOT delete on failure)
  - [ ] 18.2 Implement Program.cs for FeedWorker (DI: call AddSecretsManagerAsync() first, then register SqlHelper, all repositories, ConfigurationService, S3ImageService, EmailService, all plugins, PluginRegistry, AutoPostOrchestrator, IMediator, ICorrelationIdService, ICloudWatchMetricsService, IAmazonSQS, SQS_QUEUE_URL from env var)
  - [ ] 18.3 Write unit test for FeedWorker (message processed -> deleted; exception -> not deleted; new scope created per message)

- [ ] 19. Implement PostWorker
  - [ ] 19.1 Implement PostWorker.cs BackgroundService (same pattern as FeedWorker but polls ips-post-queue, sends ExecutePostCommand via IMediator, MaxNumberOfMessages=10, scoped DI per message)
  - [ ] 19.2 Implement Program.cs for PostWorker (same DI wiring as FeedWorker)
  - [ ] 19.3 Write unit test for PostWorker (message processed -> deleted; exception -> not deleted; Mode field passed through to command)

- [ ] 20. Implement API project
  - [ ] 20.1 Implement PostController (POST /api/post/{jobId}/items/{itemIds} and POST /api/post/{jobId} — both call orchestrator.RunManualPostAsync directly, return PostBatchResult as JSON)
  - [ ] 20.2 Implement FeedController (POST /api/feed/{jobId} — calls orchestrator.RunScheduledFeedAsync directly)
  - [ ] 20.3 Implement StatusController (GET /api/status/{executionId} — reads generic_execution_history)
  - [ ] 20.4 Implement Program.cs for Api (DI wiring, API key authentication middleware using x-api-key header)
  - [ ] 20.5 Write integration tests for PostController (manual post with itemIds, missing configuration response, successful post response shape)

---

## Phase 6: Scheduler Lambda

- [ ] 21. Implement Scheduler Lambda
  - [ ] 21.1 Implement SchedulerSyncService (read all active rows from generic_execution_schedule JOIN generic_job_configuration; for each row: create EventBridge rule if not exists, update if cron changed, disable if is_active=0; target ips-feed-queue for DOWNLOAD, ips-post-queue for POST)
  - [ ] 21.2 Implement Lambda Function.cs handler (triggered by rate(10 minutes) EventBridge rule, calls SchedulerSyncService)
  - [ ] 21.3 Write unit tests for SchedulerSyncService (new rule created, existing rule updated, inactive rule disabled)

---

## Phase 7: Docker and Infrastructure

- [ ] 22. Create Dockerfiles
  - [ ] 22.1 Create Dockerfile.FeedWorker (multi-stage: sdk:10.0 build, runtime:10.0 final, linux-x64, ENTRYPOINT dotnet IPS.AutoPost.Host.FeedWorker.dll)
  - [ ] 22.2 Create Dockerfile.PostWorker (same pattern for PostWorker)
  - [ ] 22.3 Test local Docker build for both images

- [ ] 23. Create CloudFormation Stack 1 — Infrastructure
  - [ ] 23.1 Create infrastructure.yaml: VPC subnets (1 public + 2 private across 2 AZs), NAT Gateway + EIP, private route tables
  - [ ] 23.2 Add ECS Security Group (egress: TCP 1433, 443, 80 to 0.0.0.0/0; no inbound)
  - [ ] 23.3 Add AWS::EC2::SecurityGroupIngress rule to existing RDS security group (TCP 1433 from ECS SG)
  - [ ] 23.4 Add ECR Repository (ecr-ips-autopost-{env}, ScanOnPush=true, lifecycle: delete untagged after 7 days)
  - [ ] 23.5 Add ECS Cluster (ips-autopost-{env}, containerInsights=enabled)
  - [ ] 23.6 Add SQS queues: ips-feed-queue-{env} + ips-post-queue-{env} (VisibilityTimeout=7200, MessageRetentionPeriod=1209600, maxReceiveCount=3)
  - [ ] 23.7 Add SQS DLQs: ips-feed-dlq-{env} + ips-post-dlq-{env} (MessageRetentionPeriod=1209600)
  - [ ] 23.8 Add CloudWatch Log Groups (/ips/autopost/feed/{env} + /ips/autopost/post/{env}, RetentionInDays=90)
  - [ ] 23.9 Add S3 deployment bucket (ips-autopost-deployments-{env}, versioning enabled, all public access blocked)
  - [ ] 23.10 Add Outputs (PrivateSubnets, ECSSecurityGroupId, ECSClusterName, FeedQueueURL, PostQueueURL, ECRRepositoryURI, log group names)

- [ ] 24. Create CloudFormation Stack 2 — Application
  - [ ] 24.1 Create application.yaml: ECSTaskExecutionRole (AmazonECSTaskExecutionRolePolicy)
  - [ ] 24.2 Add ECSTaskRole with policies: SQS (ReceiveMessage/DeleteMessage/GetQueueAttributes/GetQueueUrl), CloudWatch (PutMetricData), S3 (GetObject/PutObject), SecretsManager (GetSecretValue)
  - [ ] 24.3 Add FeedWorker ECS Task Definition (Family: ips-autopost-feed-{env}-{DeploymentId}, CPU=1024, Memory=2048, env vars: SQS_QUEUE_URL/ASPNETCORE_ENVIRONMENT/AWS_DEFAULT_REGION)
  - [ ] 24.4 Add PostWorker ECS Task Definition (same pattern, SQS_QUEUE_URL = PostQueueURL)
  - [ ] 24.5 Add FeedWorker ECS Service (DesiredCount=1, MinCapacity=1 — always 1 task running to eliminate cold start; DeploymentConfig: MaximumPercent=200, MinimumHealthyPercent=100)
  - [ ] 24.6 Add PostWorker ECS Service (DesiredCount=1, MinCapacity=1 — always 1 task running; same deployment config)
  - [ ] 24.7 Add AutoScalingTargets for both services (MinCapacity=1, MaxCapacity=5 — scale UP from 1 when busy, scale DOWN to 1 when idle, never to 0)
  - [ ] 24.8 Add SQS Step Scaling policies (scale-out: 1-10 msgs +1 task cooldown 120s, >10 msgs +2 tasks; scale-in: 0 msgs for 10 min -1 task cooldown 600s)
  - [ ] 24.9 Add CPU/Memory scaling policies (>80% scale out cooldown 300s, <20% scale in cooldown 300s)
  - [ ] 24.10 Add CloudWatch alarms for all scaling triggers (SQS high/low, CPU high/low, Memory high/low)

- [ ] 25. Create CloudFormation Stack 3 — Monitoring
  - [ ] 25.1 Create monitoring.yaml: CloudWatch Dashboard (IPS-AutoPost-Operations-{env}) with ECS CPU/Memory, SQS metrics, application metrics widgets
  - [ ] 25.2 Add Application Error Alarm (>5 errors in 5 min, 2 eval periods of 300s, namespace IPS/AutoPost/{env})
  - [ ] 25.3 Add FeedDLQ Alarm (>=1 message in ips-feed-dlq-{env}, 1 eval period of 300s)
  - [ ] 25.4 Add PostDLQ Alarm (>=1 message in ips-post-dlq-{env}, 1 eval period of 300s)

- [ ] 26. Create GitHub Actions CI/CD Pipeline
  - [ ] 26.1 Create deploy.yml with workflow_dispatch trigger (environment: uat | production)
  - [ ] 26.2 Add infrastructure job (deploy Stack 1, output FeedQueueURL/PostQueueURL/ECRRepositoryURI)
  - [ ] 26.3 Add application job (depends on infrastructure: ECR login, build+push FeedWorker image tagged with git SHA, build+push PostWorker image, deploy Stack 2 with DeploymentId=github.run_number)
  - [ ] 26.4 Add monitoring job (depends on infrastructure+application: deploy Stack 3)
  - [ ] 26.5 Test full pipeline deployment to UAT environment

---

## Phase 8: Property-Based Tests

- [ ] 27. Implement all 10 PBT correctness properties
  - [ ] 27.1 PostInProcess Invariant: FOR ALL failure modes (success/imageNotFound/apiFail/exception), PostInProcess=0 after processing
  - [ ] 27.2 No-Duplicate Routing Invariant: FOR ALL scenarios, workitem ends in exactly one queue (not source queue)
  - [ ] 27.3 History Completeness Invariant: count(history rows) == count(workitems where API was attempted)
  - [ ] 27.4 UseTax Round-Trip Property: UseTax=NO -> no ShipToLocation in any line; UseTax=YES -> ShipToLocation present in all lines
  - [ ] 27.5 Feed Idempotence Property: running feed download twice produces same row count as once
  - [ ] 27.6 Incremental Feed Subset Property: incremental supplier IDs is subset of full supplier IDs, count(incremental) <= count(full)
  - [ ] 27.7 Pagination Completeness Property: total records inserted == sum of all items across all pages
  - [ ] 27.8 Error Condition Routing Property: image-not-found -> EdenredFailPostQueueId with zero API calls; RequesterId-empty -> InvitedFailPostQueueId with zero API calls
  - [ ] 27.9 Retry Idempotence Property: ImagePostRetryCount increments by exactly 1 per attempt, stops at ImagePostRetryLimit
  - [ ] 27.10 SQS Message Delivery Guarantee: message either deleted (success) or in DLQ after exactly 3 failures

---

## Phase 9: UAT Parallel Run and Production Cutover

- [ ] 28. UAT parallel run validation
  - [ ] 28.1 Deploy all three CloudFormation stacks to UAT
  - [ ] 28.2 Run database migration scripts on UAT Workflow DB
  - [ ] 28.3 Enable InvitedClub parallel run (old Windows Service + new platform, both with allow_auto_post=true in respective config tables)
  - [ ] 28.4 Verify InvitedClub routing outcomes match between old and new (same queue IDs for same input conditions)
  - [ ] 28.5 Verify InvitedClub history records match (same columns and values in post_to_invitedclub_history)
  - [ ] 28.6 Enable Sevita parallel run and verify routing outcomes
  - [ ] 28.7 Validate CloudWatch dashboard shows correct PostSuccessCount/PostFailedCount metrics
  - [ ] 28.8 Validate DLQ alarms fire correctly by intentionally sending a bad message
  - [ ] 28.9 Validate manual post API returns correct response shape and routes correctly
  - [ ] 28.10 Validate InvitedClub feed download produces identical data to old service

- [ ] 29. Production deployment and cutover
  - [ ] 29.1 Run database migration scripts on production Workflow DB
  - [ ] 29.2 Deploy all three CloudFormation stacks to production
  - [ ] 29.3 Stop InvitedClub Windows Service
  - [ ] 29.4 Verify new platform handles InvitedClub independently for one full day
  - [ ] 29.5 Stop Sevita Windows Service
  - [ ] 29.6 Verify new platform handles Sevita independently for one full day
  - [ ] 29.7 Decommission old EC2 instances (after 1 week of stable production operation)
  - [ ] 29.8 Archive old Visual Studio projects (Invited_Club_API_Lib, Invited_Club_API_Service, Sevita_API_Lib, Sevita_API_Service)

---

## Additions and Corrections (from re-verification)

- [ ] 30. Missing tasks identified during re-verification
  - [ ] 30.1 Add WorkitemData model to IPS.AutoPost.Core/Models/ (ItemId, StatusId, ImagePath — returned by GetWorkitemData query)
  - [ ] 30.2 Add SqsMessagePayload model to IPS.AutoPost.Core/Models/ (JobId, ClientType, Pipeline, TriggerType — SQS message contract)
  - [ ] 30.3 Implement CloudWatch metrics publishing in AutoPostOrchestrator (PutMetricDataAsync after each batch: PostSuccessCount, PostFailedCount, PostDurationSeconds, FeedSuccessCount, FeedFailedCount, FeedDurationSeconds — namespace IPS/AutoPost/{env}, dimensions ClientType + JobId)
  - [ ] 30.4 Add IAmazonCloudWatch to DI in FeedWorker and PostWorker Program.cs; add cloudwatch:PutMetricData permission to ECSTaskRole (already in 24.2 but must be wired in code)
  - [ ] 30.5 Implement GetAPIResponseTypes loading in InvitedClubPostStrategy.ExecuteAsync (call at start of each PostData execution for both auto and manual: SELECT response_type, response_code, response_message FROM api_response_configuration WHERE job_id=@job_id; use POST_SUCCESS and RECORD_NOT_POSTED response types in PostItemResult)
  - [ ] 30.6 Implement EdenredApiUrlConfig startup loading in ConfigurationRepository (SELECT AssetApiUrl, BucketName, S3AccessKey, S3SecretKey, S3Region FROM EdenredApiUrlConfig — called once at startup, used to initialize S3Utility)
  - [ ] 30.7 Implement GetInvitedClubsEmailConfigPerJob SP call in InvitedClubFeedStrategy.ExecuteAsync (call GetInvitedClubsEmailConfigPerJob(@ConfigId) per configuration to load email config for COA missing notification)
  - [ ] 30.8 Set ServicePointManager.SecurityProtocol = Tls | Tls11 | Tls12 in InvitedClubPlugin startup (required for Oracle Fusion TLS compatibility — set once when plugin is initialized)
  - [ ] 30.9 Handle @IsNewUI parameter in InvitedClubPlugin config loading (when callingApplication.Trim().ToUpper() == "NEWUI": pass @IsNewUI=1 to get_invitedclub_configuration AND set processManually=true; for all other callers including "API": @IsNewUI=0)
  - [ ] 30.10 Create appsettings.json for FeedWorker (connection string fallback, AWS region, log level)
  - [ ] 30.11 Create appsettings.json for PostWorker (same as FeedWorker)
  - [ ] 30.12 Create appsettings.json for Api project (connection string fallback, API key config, log level)
  - [ ] 30.13 Implement dbo.split delimiter in WorkitemRepository.GetWorkitemsByItemIdsAsync (use ', ' comma-space as delimiter: SELECT W.* FROM Workitems W WHERE ItemId IN (SELECT * FROM dbo.split(@itemids, ', ')))
  - [ ] 30.14 Write unit test for InvitedClubFeedStrategy.ExecuteAsync (DownloadFeed independent of posting schedule — feed runs even when schedule window is not active)
  - [ ] 30.15 Write unit test for InvitedClubFeedStrategy.DownloadFeed returns false on exception (last_download_time NOT updated when exception thrown)
  - [ ] 30.16 Implement GetSevitaPostResponseTypes in SevitaPostStrategy (call at start of each PostData execution: SELECT * FROM sevita_response_configuration WHERE config_id=@config_id; use returned response types in PostItemResult — this is separate from api_response_configuration)
  - [ ] 30.17 Add sevita_response_configuration to Req 8 AC2 existing tables list (it is an existing table that must not be altered)

---

## Critical Corrections from Deep Re-Verification (Pass 4)

- [ ] 31. Fix critical gaps found in deep source re-analysis
  - [ ] 31.1 CORRECT Req 21 AC16 and Task 16.6: Sevita email template placeholder is [[AppendTable]] NOT #MissingImagesTable#. The EmailSender.SendPostNotificationMail does mailBody.Replace("[[AppendTable]]", tableBody). Update SevitaPostStrategy.SendNotificationEmailAsync to use [[AppendTable]] as the replacement placeholder.
  - [ ] 31.2 CORRECT Req 21 AC4 and Task 16.7: Sevita DOES write a history record for image-fail workitems (unlike InvitedClub which does NOT). When image is not found, Sevita creates PostHistory with empty InvoiceRequestJson="" and InvoiceResponseJson="" and calls UpdateHistory. This is existing behavior that must be preserved.
  - [ ] 31.3 REMOVE Task 30.16 and CORRECT Req 21 AC21: GetSevitaPostResponseTypes is defined in GetDataBal but is NEVER called in the actual PostData flow. The postResponseTypes class member is always empty. Sevita only uses api_response_configuration (GetAPIResponseTypes) — NOT sevita_response_configuration. Remove the requirement to call GetSevitaPostResponseTypes. The sevita_response_configuration table exists but is unused in the current implementation.
  - [ ] 31.4 CORRECT Task 17.1 and Req 21: Sevita sets SqlHelper.ConnectionString = connectionStr ONCE at startup (not per-configuration like InvitedClub). The per-configuration assignment is commented out in the Sevita code. SevitaPlugin must NOT reassign SqlHelper.ConnectionString per-configuration.
  - [ ] 31.5 CORRECT Task 9.1 and Req 11 AC24: InvitedClubConfig field is UserId (int), NOT DefaultUserId. The model property is named UserId in InvitedClubConfig. Update all references in InvitedClubPlugin and InvitedClubPostStrategy to use config.UserId (not config.DefaultUserId). Sevita correctly uses DefaultUserId.
  - [ ] 31.6 Add DBErrorEmailConfiguration to SevitaConfig model: get_sevita_configurations SP returns db_error_to_email_address, db_error_cc_email_address, db_error_email_subject, db_error_email_template. These are loaded into DBErrorEmailConfiguration. Add DBErrorEmailConfiguration class to Sevita models and populate it from the SP result.
  - [ ] 31.7 CORRECT Task 17.1: Sevita GetValidIds uses raw SqlConnection + SqlCommand directly (NOT SqlHelper) with a multi-statement query: SELECT Supplier FROM Sevita_Supplier_SiteInformation_Feed; SELECT EmployeeID FROM Sevita_Employee_Feed. Implement SevitaPlugin.OnBeforePostAsync using SqlConnection directly with SqlDataReader.NextResult() to read both result sets in one round trip.
  - [ ] 31.8 CORRECT Task 16.4: Sevita PostInvoice uses request.AddParameter("application/json", invoiceRequestJson, ParameterType.RequestBody) — NOT AddJsonBody. This is different from InvitedClub calculateTax which uses AddJsonBody. Implement SevitaPostStrategy.PostInvoiceAsync with AddParameter + ParameterType.RequestBody.
  - [ ] 31.9 CORRECT Task 16.5: SavePostHistoryWithNullFileBase parses invoiceRequestJson as JArray (not JObject) because the payload is wrapped in [{...}]. Use JArray.Parse(invoiceRequestJson) then iterate items to null out fileBase on each attachment.
  - [ ] 31.10 Add migration note for Task 8.3: post_to_sevita_configuration uses configuration_id as PK (not id). UpdateLastPostTime uses WHERE configuration_id=@configuration_id. Ensure migration script for generic_job_configuration maps from configuration_id correctly.
  - [ ] 31.11 Write unit test for Sevita image-fail path: verify that history IS written (with empty request/response JSON) when image is not found — this is opposite to InvitedClub behavior.
  - [ ] 31.12 Write unit test for Sevita email: verify [[AppendTable]] placeholder is replaced (not #MissingImagesTable#).

---

## Phase 10: Architecture Patterns from GenericMissingInvoicesProcess (Req 23)

- [ ] 32. Implement MediatR, CorrelationId, Metrics, EF Core, and Infrastructure patterns
  - [ ] 32.1 Implement DynamicRecord model (Dictionary<string, object?> Fields, GetValue<T> method) — used by GenericRestPlugin for schema-agnostic payload building
  - [ ] 32.2 Implement SecretsManagerConfigurationProvider in Infrastructure/ folder with the following behavior:
    - Static class `SecretsManagerExtensions` with `AddSecretsManagerAsync(this IConfigurationBuilder builder, TimeSpan? timeout = null)` extension method
    - Scan the following sections for values starting with `/`: `ConnectionStrings` (all children), `Email:SmtpPassword`, `ApiKey:Value`
    - Fetch all found secret paths in parallel using `Task.WhenAll` with a 30-second `CancellationTokenSource` timeout
    - Handle JSON secrets: if secret value starts with `{`, parse as JSON and extract `AppConnectionString` key (for RDS-managed connection string secrets)
    - Inject fetched values back into `IConfiguration` via `builder.AddInMemoryCollection(secrets)` (highest priority — overrides appsettings.json)
    - Use default AWS credential provider chain: `new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(region))` — works with IAM role (ECS production), env vars (local dev), and `~/.aws/credentials` (developer machines)
    - Read region from `AWS_DEFAULT_REGION` env var, fallback to `AWS_REGION`, fallback to `"us-east-1"`
    - Throw `InvalidOperationException` on timeout: `"Secrets Manager request timed out after 30s"`
    - Throw `InvalidOperationException` when secret not found: `"Secret '{secretId}' not found in Secrets Manager"`
    - appsettings.json pattern: `"ConnectionStrings": { "Workflow": "/IPS/Common/production/Database/Workflow" }`, `"Email": { "SmtpPassword": "/IPS/Common/production/Smtp" }`, `"ApiKey": { "Value": "/IPS/Common/production/ApiKey" }`
    - Plugin-specific credentials (`/IPS/InvitedClub/{env}/PostAuth`, `/IPS/Sevita/{env}/PostAuth`) are NOT scanned at startup — they are fetched on-demand by each plugin via `ConfigurationService.GetSecretAsync()`
  - [ ] 32.3 Add `await builder.Configuration.AddSecretsManagerAsync()` call in Program.cs for FeedWorker, PostWorker, and Api projects
  - [ ] 32.4 Update appsettings.json for all workers to use config-path pattern:
    ```json
    {
      "ConnectionStrings": { "Workflow": "/IPS/Common/{env}/Database/Workflow" },
      "Email": { "SmtpPassword": "/IPS/Common/{env}/Smtp" },
      "ApiKey": { "Value": "/IPS/Common/{env}/ApiKey" }
    }
    ```
    Non-secret values (AWS region, Serilog config, SQS queue URL) remain as plain values. The `SecretsManagerConfigurationProvider` replaces only the `/`-prefixed values at startup.
  - [ ] 32.5 Implement CorrelationIdService (AsyncLocal<string>, SetCorrelationId returns IDisposable that pushes to Serilog LogContext)
  - [ ] 32.6 Update Serilog output template to include [{CorrelationId}]: `"[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] [{ClientType}] [{JobId}] {Message:lj}{NewLine}{Exception}"`
  - [ ] 32.7 Implement ICloudWatchMetricsService interface with 12 metric methods
  - [ ] 32.8 Implement CloudWatchMetricsService (PutMetricDataAsync, namespace IPS/AutoPost/{env}, dimensions ClientType + JobId)
  - [ ] 32.9 Wire ICloudWatchMetricsService into AutoPostOrchestrator — publish metrics after each batch execution
  - [ ] 32.10 Implement AutoPostDatabaseContext (EF Core DbContext for 10 generic tables only)
  - [ ] 32.11 Create EF Core initial migration: `dotnet ef migrations add InitialGenericTables`
  - [ ] 32.12 Add `context.Database.Migrate()` in Program.cs for FeedWorker and PostWorker
  - [ ] 32.13 Implement ExecutePostCommand and ExecuteFeedCommand (IRequest<T> with Mode? field)
  - [ ] 32.14 Implement ExecutePostHandler and ExecuteFeedHandler (IRequestHandler delegates to AutoPostOrchestrator)
  - [ ] 32.15 Implement LoggingBehavior<TRequest, TResponse> (IPipelineBehavior — logs with CorrelationId)
  - [ ] 32.16 Implement ValidationBehavior<TRequest, TResponse> (IPipelineBehavior — FluentValidation)
  - [ ] 32.17 Register MediatR + behaviors in ServiceCollectionExtensions
  - [ ] 32.18 Update FeedWorker and PostWorker to use MaxNumberOfMessages=10 and create new DI scope per message
  - [ ] 32.19 Update FeedWorker and PostWorker to send commands via IMediator instead of calling orchestrator directly
  - [ ] 32.20 Write unit tests for CorrelationIdService (AsyncLocal isolation, LogContext property)
  - [ ] 32.21 Write unit tests for CloudWatchMetricsService (correct namespace, dimensions)
  - [ ] 32.22 Write unit tests for LoggingBehavior and ValidationBehavior
  - [ ] 32.23 Write integration tests using EF Core InMemory database (ExecutePostHandler with seeded config data)
  - [ ] 32.24 Write unit tests for SecretsManagerConfigurationProvider: (1) ConnectionStrings "/" value is replaced with fetched secret; (2) Email:SmtpPassword "/" value is replaced; (3) ApiKey:Value "/" value is replaced; (4) non-"/" values are unchanged; (5) JSON secret with AppConnectionString key is correctly extracted; (6) timeout throws InvalidOperationException; (7) missing secret throws InvalidOperationException
