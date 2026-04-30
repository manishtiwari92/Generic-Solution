# Requirements Document

## Introduction

The IPS.AutoPost Platform is a cloud-native .NET Core 10 solution that replaces 15+ separate Windows Service projects with a single unified invoice posting and feed download platform hosted on AWS. The platform uses a plugin architecture so that client-specific logic (such as Oracle Fusion integration for InvitedClub) is isolated in plugins, while all common operations â€” schedule checking, workitem fetching, queue routing, history recording, and logging â€” are handled by a generic core engine driven entirely by database configuration.

The first client plugin to be implemented is **InvitedClub**, which integrates with Oracle Fusion Cloud Payables. InvitedClub must behave exactly as the current Windows Service implementation does today. All other 15+ clients will follow the same plugin pattern after InvitedClub is proven in production.

The platform is deployed to AWS using ECS Fargate workers (not Lambda) to eliminate the 15-minute execution time limit. New clients are onboarded by inserting rows into the `generic_job_configuration` table and writing one plugin class â€” no new projects, no new deployments, no new infrastructure.

---

## Glossary

- **MediatR**: The platform uses MediatR Command/Handler pattern. SQS consumers send `ExecutePostCommand` or `ExecuteFeedCommand` via `mediator.Send()`. Handlers contain all business logic. Pipeline behaviors (LoggingBehavior, ValidationBehavior) wrap every command automatically.
- **CorrelationId**: Each SQS message gets a unique correlation ID via `CorrelationIdService` (AsyncLocal). All log entries for a job run include `[{CorrelationId}]` automatically via Serilog LogContext.
- **CloudWatchMetrics**: Granular per-client metrics published via `ICloudWatchMetricsService` after each execution (PostStarted, PostCompleted, PostSuccessCount, PostFailedCount, PostDurationSeconds, FeedStarted, FeedCompleted, FeedRecordsDownloaded, FeedDurationSeconds, ImageRetryAttempted, ImageRetrySucceeded).
- **DynamicRecord**: Schema-agnostic data model (`Dictionary<string, object?>`) used by `GenericRestPlugin` for building payloads from `generic_field_mapping` rows without knowing column names at compile time.
- **EF Core Migrations**: The 10 new generic tables are managed by EF Core migrations (auto-applied at startup). Existing Workflow tables use SqlHelper only.
- **SecretsManagerConfigurationProvider**: Config-path pattern â€” values starting with `/` in `ConnectionStrings` are fetched from Secrets Manager at startup. No special code needed in services.
- **SQS Consumer**: `MaxNumberOfMessages = 10` (not 1). Each message processed in its own DI scope (`_serviceProvider.CreateScope()`). Message deleted only after successful processing.
- **Mode Override**: SQS message can carry optional `Mode` field to override runtime behavior without redeployment.
- **Core_Engine**: The generic orchestration layer (`IPS.AutoPost.Core`) that handles all operations common to every client: schedule checking, workitem fetching, routing, history, and logging.
- **Plugin**: A client-specific class implementing `IClientPlugin` that contains all business logic unique to one ERP integration (e.g., `InvitedClubPlugin`).
- **Plugin_Registry**: The component that maps a `client_type` string (e.g., `"INVITEDCLUB"`) to the correct `IClientPlugin` implementation at runtime.
- **Feed_Worker**: The ECS Fargate service (`IPS.AutoPost.Host.FeedWorker`) that polls `ips-feed-queue` and executes feed download jobs.
- **Post_Worker**: The ECS Fargate service (`IPS.AutoPost.Host.PostWorker`) that polls `ips-post-queue` and executes invoice posting jobs.
- **Scheduler_Lambda**: The AWS Lambda function that runs every 10 minutes, reads `generic_execution_schedule` from RDS, and synchronizes EventBridge Scheduler rules. It never touches invoices.
- **EventBridge**: Amazon EventBridge Scheduler â€” the cloud alarm clock that fires cron-based triggers per job and drops messages into SQS queues.
- **SQS**: Amazon Simple Queue Service â€” the message queue that decouples EventBridge triggers from Fargate workers and provides built-in retry and dead-letter queue (DLQ) support.
- **DLQ**: Dead Letter Queue â€” an SQS queue that receives messages after 3 failed processing attempts, triggering an alert.
- **RDS**: Amazon RDS SQL Server â€” the existing Workflow database (`ips-rds-database-1.cmrmduasa2gk.us-east-1.rds.amazonaws.com`, database `Workflow`). No schema changes are permitted to existing tables or stored procedures.
- **S3**: Amazon S3 â€” object storage for invoice images (PDFs/TIFFs), feed archives, and output files.
- **Secrets_Manager**: AWS Secrets Manager â€” the secure vault for all credentials (database passwords, API keys, SMTP passwords). No credentials are stored in config files or source code.
- **CloudWatch**: Amazon CloudWatch â€” the central logging, metrics, and alerting service for the platform.
- **Workitem**: A single invoice record in the `Workitems` table, identified by `ItemId`, associated with a `JobId` and a `StatusId` (queue position).
- **PostInProcess**: A flag column on the index header table (e.g., `WFInvitedClubsIndexHeader.PostInProcess`) that is set to `1` before processing and cleared to `0` in a `finally` block, preventing concurrent duplicate posting.
- **InvitedClub**: The first client plugin â€” integrates with Oracle Fusion Cloud Payables via REST API using Basic Authentication.
- **Oracle_Fusion**: The Oracle Fusion Cloud ERP system used by the InvitedClub client.
- **InvoiceId**: The Oracle Fusion invoice identifier returned by the invoice POST API (HTTP 201). Stored in `WFInvitedClubsIndexHeader.InvoiceId`.
- **AttachedDocumentId**: The Oracle Fusion attachment identifier returned by the attachment POST API (HTTP 201). Stored in `WFInvitedClubsIndexHeader.AttachedDocumentId`.
- **UseTax**: A flag on `WFInvitedClubsIndexHeader.UseTax` (`YES`/`NO`) that controls whether `ShipToLocation` is included in invoice lines and whether the `calculateTax` API step is executed.
- **COA**: Chart of Accounts â€” the Oracle Fusion GL account combinations downloaded and stored in `InvitedClubCOA`.
- **generic_job_configuration**: The new generic table that replaces all 15 `post_to_xxx_configuration` tables. One row per client job.
- **generic_execution_schedule**: The new generic table that stores cron expressions and HH:mm execution times per job.
- **WORKITEM_ROUTE**: The existing stored procedure that routes a workitem to a target queue by updating its `StatusId`.
- **GENERALLOG_INSERT**: The existing stored procedure that inserts an audit log entry.
- **Manual_Post**: A post triggered directly by a user from the Workflow UI via API Gateway, bypassing SQS, calling the Post_Worker directly for an immediate synchronous response.
- **Scheduled_Post**: A post triggered by EventBridge on a cron schedule, routed through SQS to the Post_Worker asynchronously.
- **api_response_configuration**: An existing RDS table that maps response type keys (`POST_SUCCESS`, `RECORD_NOT_POSTED`) to numeric response codes and human-readable messages, scoped per `job_id`.
- **EdenredApiUrlConfig**: An existing RDS table that stores S3 credentials (`BucketName`, `S3AccessKey`, `S3SecretKey`, `S3Region`) and the asset API URL used to initialize the `S3Utility` at startup.
- **APIResponseType**: A model class with fields `ResponseType`, `ResponseCode`, and `ResponseMessage` populated from `api_response_configuration`.
- **Orphaned_Invoice**: An invoice that has been successfully created in Oracle Fusion (has an `InvoiceId`) but whose attachment POST failed, leaving it without an `AttachedDocumentId`. Recovery is exclusively via `RetryPostImages`.
- **SevitaPlugin**: The second client plugin â€” integrates with the Sevita AP system via REST API using OAuth2 client_credentials authentication.
- **ValidIds**: A runtime-loaded set of valid `VendorIds` (from `Sevita_Supplier_SiteInformation_Feed`) and `EmployeeIds` (from `Sevita_Employee_Feed`) used by SevitaPlugin to validate invoice records before posting.
- **OnBeforePostAsync**: An `IClientPlugin` lifecycle hook called once per batch before the workitem loop, used for batch-level pre-loading (e.g., loading `ValidIds` for Sevita).
- **FeedResult**: A result type returned by `IClientPlugin.ExecuteFeedDownload`. `FeedResult.NotApplicable()` signals that the plugin has no feed download step; the Core_Engine skips feed processing when this is returned.

---

## Requirements

### Requirement 1: Generic Core Engine

**User Story:** As a platform engineer, I want a single generic core engine that handles all common operations for every client, so that adding a new client never requires changes to the core codebase.

#### Acceptance Criteria

1. THE Core_Engine SHALL load job configuration from the `generic_job_configuration` table using the `job_id` parameter without reading any client-specific configuration table.
2. THE Core_Engine SHALL resolve the correct Plugin from the Plugin_Registry using the `client_type` value stored in `generic_job_configuration`.
3. THE Core_Engine SHALL fetch pending workitems by executing the query `SELECT w.ItemId, w.StatusId FROM Workitems w JOIN {header_table} h ON w.ItemId = h.UID WHERE JobId = @JobId AND StatusId IN ({source_queue_id})` using table names and queue IDs from configuration.
4. WHEN a workitem is selected for processing, THE Core_Engine SHALL set `PostInProcess = 1` on the header table row before calling the Plugin.
5. WHEN a workitem finishes processing (success or failure), THE Core_Engine SHALL set `PostInProcess = 0` on the header table row in a `finally` block, ensuring the flag is always cleared regardless of exceptions.
6. THE Core_Engine SHALL call `WORKITEM_ROUTE` stored procedure with `@itemID`, `@Qid`, `@userId`, `@operationType`, and `@comment` parameters to route each workitem to its target queue.
7. THE Core_Engine SHALL call `GENERALLOG_INSERT` stored procedure with `@operationType`, `@sourceObject`, `@userID`, `@comments`, and `@itemID` parameters to record audit log entries.
8. THE Core_Engine SHALL insert a row into `generic_post_history` for every workitem processed, capturing `client_type`, `job_id`, `item_id`, `step_name`, `post_request`, `post_response`, `post_date`, `posted_by`, and `manually_posted`.
9. THE Core_Engine SHALL insert a row into `generic_execution_history` after each execution run, capturing `execution_type`, `trigger_type`, `status`, `records_processed`, `records_success`, `records_failed`, `start_time`, `end_time`, and `duration_seconds`.
10. THE Core_Engine SHALL retrieve all credentials exclusively from Secrets_Manager using the path `/IPS/{client_type}/{env}/PostAuth` and SHALL NOT read credentials from configuration files or environment variables directly.
11. WHEN a new client is added by inserting rows into `generic_job_configuration`, THE Core_Engine SHALL process that client without any code changes to the core assembly.

---

### Requirement 2: Plugin Architecture

**User Story:** As a platform engineer, I want a well-defined plugin interface so that client-specific logic is fully isolated and new plugins can be added without modifying the core engine.

#### Acceptance Criteria

1. THE Platform SHALL define an `IClientPlugin` interface with at minimum the following methods:
   - `ExecutePost(GenericJobConfig config, IEnumerable<WorkitemData> workitems, PostContext context)` â€” processes all workitems in a batch.
   - `ExecuteFeedDownload(GenericJobConfig config, FeedContext context)` â€” downloads feed data.
   - `OnBeforePostAsync(GenericJobConfig config, CancellationToken ct)` â€” called ONCE before the workitem loop to allow batch-level pre-loading (e.g., Sevita loads `ValidIds` from `Sevita_Supplier_SiteInformation_Feed` and `Sevita_Employee_Feed` tables before processing any workitem). Default implementation returns `Task.CompletedTask` (no-op).
2. THE Plugin_Registry SHALL map each `client_type` string to exactly one `IClientPlugin` implementation at application startup.
3. WHEN the Plugin_Registry receives a `client_type` that has no registered plugin, THE Plugin_Registry SHALL throw a `PluginNotFoundException` with the unrecognized `client_type` value in the message.
4. THE Platform SHALL ship with the following plugins registered at launch: `InvitedClubPlugin` (client_type = `"INVITEDCLUB"`) and `SevitaPlugin` (client_type = `"SEVITA"`).
5. WHERE a client requires only a simple JSON REST post with no multi-step chaining, THE Platform SHALL support a `GenericRestPlugin` that builds the request payload dynamically from `generic_field_mapping` table rows without requiring a custom plugin class.
6. THE Platform SHALL allow a new plugin to be added by creating one class in the `IPS.AutoPost.Plugins` project and registering it in the Plugin_Registry, with no changes required to `IPS.AutoPost.Core`.
7. THE `IClientPlugin.ExecutePost` method SHALL receive the full header and detail DataSet for each workitem (not just the header row), allowing the plugin to perform client-specific transformations such as grouping detail rows by composite key and summing amounts (as required by Sevita's line item grouping by `alias + naturalAccountNumber`).
8. THE Platform SHALL allow plugins to perform pre-post validation on workitem data before calling the external ERP API. Validation failures SHALL route the workitem to the configured fail queue with a descriptive message in the `question` field, without calling the external API. Examples of plugin-level validation: Sevita validates that the sum of all line item amounts equals the header invoice amount; Sevita validates that `vendorId` exists in `Sevita_Supplier_SiteInformation_Feed` and `employeeId` exists in `Sevita_Employee_Feed`.
9. THE `IClientPlugin` interface SHALL include an optional `ClearPostInProcessAsync(long itemId, GenericJobConfig config)` method that the plugin can override to use a client-specific stored procedure (e.g., Sevita uses `UpdateSevitaHeaderPostFields(@UID)` instead of a direct SQL UPDATE). When not overridden, the Core_Engine SHALL execute `UPDATE {header_table} SET PostInProcess = 0 WHERE UID = @uid` directly.
10. THE `IClientPlugin` interface SHALL allow the plugin to control what is stored in the history record. Specifically, the plugin SHALL be responsible for preparing the `post_request` JSON before it is passed to `UpdateHistory`, allowing transformations such as Sevita's `SavePostHistoryWithNullFileBase` which sets `fileBase = null` on all attachments before saving to prevent storing large base64 strings in the database.
11. THE `IClientPlugin` interface SHALL provide a default implementation of `ExecuteFeedDownload` that returns `FeedResult.NotApplicable()`, so that plugins with no feed download (such as `SevitaPlugin`) do not need to implement this method. The Core_Engine SHALL skip feed download processing when `FeedResult.NotApplicable()` is returned.

---

### Requirement 3: Configuration-Driven Client Onboarding

**User Story:** As an operations engineer, I want to add new clients by inserting database rows only, so that no new deployments or infrastructure changes are needed for each new client.

#### Acceptance Criteria

1. THE Platform SHALL read all job configuration from the `generic_job_configuration` table, which SHALL contain at minimum: `id`, `client_type`, `job_id`, `job_name`, `is_active`, `source_queue_id`, `success_queue_id`, `primary_fail_queue_id`, `secondary_fail_queue_id`, `question_queue_id`, `header_table`, `detail_table`, `detail_uid_column`, `history_table`, `post_service_url`, `auth_type`, `auth_username`, `auth_password`, `allow_auto_post`, `download_feed`, `is_legacy_job`, `image_parent_path`, `new_ui_image_parent_path`, `feed_download_path`, and `client_config_json`. The `image_parent_path` field is used for legacy jobs; `new_ui_image_parent_path` is a separate field mapped from `post_to_invitedclub_configuration.new_ui_image_parent_path` for non-legacy jobs. The `auth_username` field stores the Basic Auth username or OAuth2 client_id for clients not using `client_config_json`; `auth_password` stores the corresponding Basic Auth password or OAuth2 client_secret; `detail_uid_column` stores the column name in the detail table that links to the header UID (e.g., used by Sevita as `detail_uid_column`). The `auth_type` field SHALL support the following values: `'BASIC'` (HTTP Basic Authentication â€” used by InvitedClub), `'OAUTH2'` (OAuth2 client_credentials Bearer token â€” used by Sevita), `'APIKEY'` (API key header), and `'NONE'` (no authentication). The `auth_type` value determines how the plugin authenticates to the external ERP API. For clients using `auth_type = 'OAUTH2'`, the following OAuth2-specific fields SHALL be stored in `client_config_json`: `api_access_token_url` (token endpoint URL), `client_id`, `client_secret`, and `token_expiration_min` (token cache duration in minutes). The plugin SHALL obtain a Bearer token using the `client_credentials` grant type and cache it until `token_expiration_min` minutes have elapsed.
2. THE `client_config_json` field in `generic_job_configuration` SHALL store all client-specific configuration that does not fit the generic columns. For Sevita, this includes: `is_PO_record` (boolean â€” controls PO vs Non-PO validation rules), `post_json_path` (S3 path for saving request JSON before posting), `t_drive_location`, `new_ui_t_drive_location`, and `remote_path`. The plugin SHALL deserialize `client_config_json` into a typed client-specific config object at runtime.
3. THE Platform SHALL read execution schedules from the `generic_execution_schedule` table, which SHALL support both `execution_time` (HH:mm format, 30-minute window logic) and `cron_expression` (EventBridge cron/rate format).
4. THE Platform SHALL read feed source configuration from the `generic_feed_configuration` table, which SHALL support `feed_source_type` values of `REST`, `FTP`, `SFTP`, `S3`, and `FILE`.
5. THE Platform SHALL read queue routing rules from the `generic_queue_routing_rules` table using `result_type` values of `SUCCESS`, `FAIL_POST`, `FAIL_IMAGE`, `DUPLICATE`, `QUESTION`, and `TERMINATED`.
6. THE Platform SHALL read email notification configuration from the `generic_email_configuration` table using `email_type` values of `POST_FAILURE`, `IMAGE_FAILURE`, `MISSING_COA`, and `FEED_FAILURE`.
7. WHEN `post_json_path` is configured in `client_config_json`, THE SevitaPlugin SHALL upload the serialized request JSON to S3 at the configured path before calling the external API. This is an optional audit/debug feature specific to Sevita. The generic platform SHALL provide `S3Utility.UploadFileToS3Async` for use by plugins.
8. WHEN `is_active = 0` in `generic_job_configuration`, THE Platform SHALL skip that job entirely without logging an error.
9. THE Scheduler_Lambda SHALL read `generic_execution_schedule` and create or update one EventBridge Scheduler rule per active job per schedule type (`POST`, `DOWNLOAD`) within 10 minutes of a configuration change.

---

### Requirement 4: AWS Infrastructure â€” Scheduler Lambda

**User Story:** As a platform engineer, I want a lightweight Lambda function that keeps EventBridge rules synchronized with the database schedule configuration, so that schedule changes take effect within 10 minutes without any manual AWS console work.

#### Acceptance Criteria

1. THE Scheduler_Lambda SHALL execute on a fixed `rate(10 minutes)` EventBridge rule.
2. THE Scheduler_Lambda SHALL read all active rows from `generic_execution_schedule` joined with `generic_job_configuration` where `is_active = 1`.
3. THE Scheduler_Lambda SHALL create an EventBridge Scheduler rule for each active schedule row that does not already have a corresponding rule.
4. THE Scheduler_Lambda SHALL update an existing EventBridge Scheduler rule when the `cron_expression` or `execution_time` for that schedule row has changed.
5. THE Scheduler_Lambda SHALL disable (not delete) an EventBridge Scheduler rule when the corresponding `generic_job_configuration` row has `is_active = 0`.
6. THE Scheduler_Lambda SHALL target `ips-feed-queue` for schedule rows with `schedule_type = 'DOWNLOAD'` and `ips-post-queue` for schedule rows with `schedule_type = 'POST'`.
7. THE Scheduler_Lambda SHALL complete its full synchronization pass in under 30 seconds under normal operating conditions.
8. THE Scheduler_Lambda SHALL never read workitems, never call external ERP APIs, and never post invoices.

---

### Requirement 5: AWS Infrastructure â€” SQS Queues

**User Story:** As a platform engineer, I want SQS queues between EventBridge and the Fargate workers so that job triggers are durable, retryable, and decoupled from compute availability.

#### Acceptance Criteria

1. THE Platform SHALL use two primary SQS queues: `ips-feed-queue` for feed download jobs and `ips-post-queue` for invoice posting jobs.
2. THE Platform SHALL configure each primary SQS queue with:
   - **Visibility timeout: 7200 seconds (2 hours)** â€” accommodates long-running invoice batches that may take over an hour to process.
   - **Message retention period: 1,209,600 seconds (14 days)** â€” provides a longer window for investigation and retry.
   - **Maximum receive count: 3** before moving to the DLQ.
3. THE Platform SHALL configure a Dead Letter Queue for each primary queue: `ips-feed-dlq` and `ips-post-dlq`.
4. WHEN a message in `ips-post-queue` or `ips-feed-queue` has been received 3 times without being deleted, THE Platform SHALL move that message to the corresponding DLQ.
5. WHEN a message arrives in `ips-post-dlq` or `ips-feed-dlq`, THE Platform SHALL publish a CloudWatch alarm that triggers an SNS notification to the configured alert email address.
6. THE SQS message body SHALL be a JSON object containing at minimum: `JobId` (int), `ClientType` (string), `Pipeline` (string: `"Post"` or `"Feed"`), and `TriggerType` (string: `"Scheduled"` or `"Manual"`).
7. WHEN a Manual_Post is triggered via API Gateway, THE Platform SHALL NOT route the request through SQS and SHALL call the Post_Worker directly via an internal HTTP endpoint.

---

### Requirement 6: AWS Infrastructure â€” ECS Fargate Workers

**User Story:** As a platform engineer, I want ECS Fargate workers with no execution time limit so that long-running jobs (Media SOAP downloads at 30â€“40 minutes, 10,000+ invoice batches) complete reliably without timeout errors.

#### Acceptance Criteria

1. THE Platform SHALL deploy two ECS Fargate services: `ips-feed-worker` and `ips-post-worker`, each running the same `IPS.AutoPost.Core` and `IPS.AutoPost.Plugins` assemblies.
2. THE Feed_Worker SHALL poll `ips-feed-queue` continuously and process one message at a time.
3. THE Post_Worker SHALL poll `ips-post-queue` continuously and process one message at a time.
4. THE Feed_Worker and Post_Worker ECS Services SHALL be configured with `DesiredCount: 1` and `MinCapacity: 1` â€” always keeping at least 1 task running at all times. This eliminates the 30â€“60 second cold start delay that would occur if tasks scaled to zero. The always-on task costs approximately $15â€“20/month per worker (~$35/month total for both), which is the price of instant responsiveness for manual post triggers from the Workflow UI. **Note: This platform has a manual post trigger where users wait for an immediate response â€” a 60-second cold start is unacceptable. Scale-to-zero is only appropriate for batch-only systems with no interactive triggers.**
5. THE Platform SHALL configure Step Scaling for SQS-based scale-out with two tiers: (1) 1â€“10 messages visible â†’ add 1 task (cooldown: 120 seconds); (2) >10 messages visible â†’ add 2 tasks. Scale-in: when queue depth = 0 for 10 minutes (2 evaluation periods of 5 minutes each) â†’ remove 1 task (cooldown: 600 seconds).
6. THE Platform SHALL configure additional scaling policies based on CPU and Memory utilization: scale out when CPU or Memory > 80% (2 evaluation periods of 5 minutes, cooldown 300 seconds); scale in when CPU or Memory < 20% (2 evaluation periods of 5 minutes, cooldown 300 seconds).
7. THE Fargate workers SHALL have no execution time limit and SHALL process batches of any size without chunking or pagination imposed by the platform.
8. THE Fargate workers SHALL run in private subnets within the IPS VPC and SHALL access RDS, S3, SQS, Secrets_Manager, and CloudWatch via VPC endpoints without traversing the public internet.
9. WHEN a Fargate task crashes mid-job, THE Platform SHALL rely on SQS visibility timeout expiry to make the message visible again, and the next available task SHALL resume processing only the remaining unprocessed workitems (those still in the source queue with `PostInProcess = 0`).
10. THE `AutoScalingTarget` SHALL have `MinCapacity: 1` and `MaxCapacity: 5` for both Feed_Worker and Post_Worker services. The minimum of 1 ensures at least one task is always running (no cold start). When the SQS queue drains to zero, the service scales back down to 1 (never to 0).
11. THE ECS container definition SHALL pass the following environment variables to the running container:
    - `SQS_QUEUE_URL`: The URL of the SQS queue this worker polls (`ips-feed-queue` or `ips-post-queue`).
    - `ASPNETCORE_ENVIRONMENT`: The deployment environment (`uat` or `production`).
    - `AWS_DEFAULT_REGION`: The AWS region.
    These are non-secret configuration values passed as plain environment variables, not via Secrets_Manager.

---

### Requirement 7: AWS Infrastructure â€” Supporting Services

**User Story:** As a platform engineer, I want S3, Secrets Manager, and CloudWatch properly integrated so that images are retrieved securely, credentials are never in code, and all operations are observable.

#### Acceptance Criteria

1. THE Platform SHALL store invoice images (PDFs and TIFFs) in the S3 bucket `ips-invoice-images/` and retrieve them using the `S3Utility` wrapper initialized with credentials read from the `EdenredApiUrlConfig` table (`BucketName`, `S3AccessKey`, `S3SecretKey`, `S3Region`). These credentials are NOT read from Secrets_Manager; they are read from RDS at startup via `SELECT AssetApiUrl, BucketName, S3AccessKey, S3SecretKey, S3Region FROM EdenredApiUrlConfig`.
2. THE Platform SHALL archive raw feed files (Advantage SOAP responses, Oracle Fusion REST responses) to the S3 bucket `ips-feed-archive/{client_type}/{date}/` after each successful feed download.
3. THE Platform SHALL write generated output files (CSV exports, Excel reports) to the S3 bucket `ips-output-files/{client_type}/` when `output_file_path` in configuration points to an S3 path.
4. THE Platform SHALL retrieve all secrets exclusively from Secrets_Manager using paths in the format `/IPS/{client_type}/{env}/{purpose}` (e.g., `/IPS/InvitedClub/prod/PostAuth`, `/IPS/Common/prod/Database/Workflow`).
5. THE Platform SHALL write structured log entries to CloudWatch Logs in the log group `/ips/{pipeline}/{client_type}` (e.g., `/ips/post/invitedclub`, `/ips/feed/invitedclub`).
6. THE Platform SHALL publish the following CloudWatch custom metrics after each execution: `PostSuccessCount`, `PostFailedCount`, `PostDurationSeconds`, `FeedSuccessCount`, `FeedFailedCount`, `FeedDurationSeconds`, all dimensioned by `ClientType` and `JobId`.
7. WHEN `PostFailedCount` exceeds 5 in a 5-minute period (2 evaluation periods of 300 seconds each), THE Platform SHALL publish a CloudWatch alarm that triggers an SNS notification.
8. WHEN a Fargate task stops unexpectedly (exit code != 0), THE Platform SHALL trigger a CloudWatch alarm via ECS task state change events.
9. THE Platform SHALL retain CloudWatch Logs for 90 days.

---

### Requirement 8: Database Compatibility â€” No Schema Changes

**User Story:** As a database administrator, I want the platform to work with the existing Workflow database tables and stored procedures exactly as they are today, so that no migration risk is introduced.

#### Acceptance Criteria

1. THE Platform SHALL call all existing stored procedures with the exact same parameter names, types, and order as the current Windows Service implementations.
2. THE Platform SHALL read from and write to all existing tables (`Workitems`, `WFInvitedClubsIndexHeader`, `WFInvitedClubsIndexDetails`, `post_to_invitedclub_configuration`, `post_to_invitedclub_history`, `InvitedClubSupplier`, `InvitedClubSupplierAddress`, `InvitedClubSupplierSite`, `InvitedClubCOA`, `InvitedClubsCOAFullFeed`, `api_response_configuration`, `EdenredApiUrlConfig`, `post_to_sevita_configuration`, `sevita_posted_records_history`, `sevita_response_configuration`, `Sevita_Supplier_SiteInformation_Feed`, `Sevita_Employee_Feed`) without altering their schema.
3. THE Platform SHALL add only new tables (`generic_job_configuration`, `generic_execution_schedule`, `generic_feed_configuration`, `generic_auth_configuration`, `generic_queue_routing_rules`, `generic_post_history`, `generic_email_configuration`, `generic_feed_download_history`, `generic_execution_history`, `generic_field_mapping`) alongside existing tables.
4. THE Platform SHALL use the existing `dbo.split` table-valued function for splitting comma-separated item ID lists in workitem queries.
5. THE Platform SHALL use a connection pool with `Max Pool Size = 2000` as specified in the connection string.
6. THE Platform SHALL use parameterized queries exclusively and SHALL NOT construct SQL strings by concatenating user-supplied values, except for table name substitution using values sourced from the trusted `generic_job_configuration` table.
7. THE Platform SHALL call `get_invitedclub_configuration` with parameter `@IsNewUI` (bit): `@IsNewUI = 1` WHEN `callingApplication == "NEWUI"` (case-insensitive, trimmed comparison: `callingApplication.Trim().ToUpper() == "NEWUI"`), and `@IsNewUI = 0` for all other calling applications including `"API"`. The `CallingApplication.NEWUI` constant value is the string `"NEWUI"` (uppercase). WHEN `callingApplication == "NEWUI"`, BOTH `@IsNewUI = 1` is passed to `get_invitedclub_configuration` AND `processManually = true` is set â€” the same as any non-`"API"` calling application. The NEWUI path skips the auto-post loop and goes directly to the manual post path.
8. THE InvitedClubPlugin SHALL assign `SqlHelper.ConnectionString = configuration.DBConnectionString` at the start of processing each configuration in the loop. Each configuration row can have its own database connection string, allowing different jobs to connect to different databases.
9. THE Platform SHALL read S3 credentials (`BucketName`, `S3AccessKey`, `S3SecretKey`, `S3Region`) from the `EdenredApiUrlConfig` table using `SELECT AssetApiUrl, BucketName, S3AccessKey, S3SecretKey, S3Region FROM EdenredApiUrlConfig`. The `S3Utility` SHALL be initialized with these values at startup.
10. THE InvitedClubPlugin SHALL call `GetAPIResponseTypes(configuration)` at the start of each `PostData` execution (both auto and manual) to load response type mappings from `api_response_configuration` using the query `SELECT response_type, response_code, response_message FROM api_response_configuration WHERE job_id=@job_id`. The returned `APIResponseType` list SHALL be used to populate `InvoicePostResponse.ResponseCode` and `InvoicePostResponse.ResponseMessage` for `POST_SUCCESS` and `RECORD_NOT_POSTED` response types.
11. THE Platform SHALL add `AND ISNULL(h.PostInProcess, 0) = 0` to the `GetWorkitemData` query as a new safety guard. The full query SHALL be: `SELECT w.ItemId, w.StatusId FROM Workitems w JOIN {HeaderTable} h ON w.ItemId = h.UID WHERE JobId = @JobId AND StatusId IN ({source_queue_id}) AND ISNULL(h.PostInProcess, 0) = 0`. **Note: The existing `GetWorkitemData` query does NOT include this filter â€” it is a deliberate improvement over the existing code.**
12. THE Platform SHALL call `dbo.split(@itemids, ', ')` with a comma followed by a space (`', '`) as the delimiter when splitting item ID lists in `GetWorkitemDataByItemId`. This matches the existing stored function contract.
13. WHEN performing incremental supplier address or supplier site updates, THE InvitedClubPlugin SHALL delete existing rows for the affected supplier IDs using `DELETE FROM {tableName} WHERE SupplierId IN ('{id1}','{id2}',...)` before bulk-inserting the new data. The supplier IDs are sourced from the trusted Oracle Fusion API response (not user input), so this string construction is acceptable. This is the existing behavior that must be preserved.

---

### Requirement 9: InvitedClub Plugin â€” Feed Download

**User Story:** As an InvitedClub operations user, I want the platform to download fresh supplier and COA data from Oracle Fusion daily, so that the Workflow system always has up-to-date vendor and GL account information for invoice indexing.

#### Acceptance Criteria

1. THE InvitedClubPlugin SHALL execute the feed download when `download_feed = 1` in configuration AND the current time is after `feed_download_time` AND `last_download_time` is before today's date (once per day). The feed download check (`DownloadFeed` flag + `ExecuteDownloadData`) runs INDEPENDENTLY of the posting schedule check. A configuration can trigger feed download even if the posting schedule window is not active, and vice versa.
2. THE InvitedClubPlugin SHALL download all active suppliers from Oracle Fusion by calling `GET {DownloadServiceURL}/suppliers?onlyData=true&q=InactiveDate is null&limit=500&offset={N}` with Basic Authentication, paginating until `HasMore = false`.
3. THE InvitedClubPlugin SHALL bulk-insert all downloaded suppliers into `InvitedClubSupplier` using a truncate-then-insert strategy (full refresh).
4. WHEN the `InvitedClubSupplierAddress` table is empty (initial call), THE InvitedClubPlugin SHALL download addresses for all supplier IDs.
5. WHEN the `InvitedClubSupplierAddress` table is not empty (incremental call), THE InvitedClubPlugin SHALL download addresses only for suppliers whose `LastUpdateDate` is on or after `last_supplier_download_time - 2 days`.
6. THE InvitedClubPlugin SHALL download supplier addresses by calling `GET {DownloadServiceURL}/suppliers/{SupplierId}/child/addresses?onlyData=true&limit=500&offset={N}` for each qualifying supplier ID.
7. THE InvitedClubPlugin SHALL apply the same initial/incremental logic for supplier sites, calling `GET {DownloadServiceURL}/suppliers/{SupplierId}/child/sites?onlyData=true&limit=500&offset={N}`.
8. AFTER inserting supplier sites, THE InvitedClubPlugin SHALL call the stored procedure `InvitedClub_UpdateSupplierSiteInSupplierAddress` to synchronize site names into the address table.
9. THE InvitedClubPlugin SHALL call the stored procedure `InvitedClub_GetSupplierDataToExport` and write the result as a pipe-delimited CSV file to `{FeedDownloadPath}\Supplier\Supplier_{timestamp}.csv`.
10. THE InvitedClubPlugin SHALL download COA data from Oracle Fusion by calling `GET {DownloadServiceURL}/accountCombinationsLOV?onlyData=true&q=_CHART_OF_ACCOUNTS_ID=5237;EnabledFlag='Y';AccountType!='O'&limit=500&offset={N}`, paginating until `HasMore = false`.
11. THE InvitedClubPlugin SHALL bulk-insert all downloaded COA records into `InvitedClubCOA` using a truncate-then-insert strategy.
12. THE InvitedClubPlugin SHALL write the COA data as a pipe-delimited CSV file to `{FeedDownloadPath}\COA\COA_{timestamp}.csv`.
13. AFTER downloading COA data, THE InvitedClubPlugin SHALL query for `CodeCombinationId` values present in `InvitedClubCOA` but absent from `InvitedClubsCOAFullFeed`.
14. WHEN missing `CodeCombinationId` values are found, THE InvitedClubPlugin SHALL export them to an Excel file and send an email notification using the `EmailTemplate` field (the general template, not a separate COA-specific template field) ONLY WHEN BOTH `emailConfig.EmailTemplate` is not null/whitespace AND `emailConfig.EmailTo` has a non-zero length. The email is sent to `emailConfig.EmailTo` recipients (split by semicolon).
15. AFTER a successful feed download, THE InvitedClubPlugin SHALL update `last_download_time` and `last_supplier_download_time` in `post_to_invitedclub_configuration`.
16. THE InvitedClubPlugin SHALL call `GetInvitedClubsEmailConfigPerJob(@ConfigId)` for each configuration ID inside the processing loop to load the email configuration specific to that job. Email configuration is per-job, not global.
17. AFTER processing all configurations in the loop, WHEN at least one configuration's feed download succeeded (`isUpdateLastDownloadTime = true`), THE InvitedClubPlugin SHALL call `UpdateLastDownloadTime` on the first configuration in the list (`lstConfigurations.FirstOrDefault()`). This behavior must be preserved exactly.
18. THE InvitedClubPlugin SHALL call `UpdateSupplierLastDownloadTime(configurations.Id)` after successfully completing the supplier, supplier address, and supplier site downloads (before COA download). This is a separate call from `UpdateLastDownloadTime`, which is called after the full feed (including COA) completes.
19. AFTER deserializing each page of supplier address or supplier site data from the Oracle Fusion API response, THE InvitedClubPlugin SHALL inject the parent `SupplierId` into each item (`item.SupplierId = supplierId`) before adding to the collection, because the Oracle Fusion API does not include `SupplierId` in address or site response items.
20. ALL HTTP REST client calls to Oracle Fusion for feed download (supplier, supplier address, supplier site, COA) SHALL use an infinite timeout (`Timeout = -1` in RestSharp). Oracle Fusion API calls can be slow and must not be interrupted by a client-side timeout.
21. WHEN the `DownloadFeed` method throws an unhandled exception, THE InvitedClubPlugin SHALL return `false` and `last_download_time` SHALL NOT be updated. The `isUpdateLastDownloadTime` flag is only set to `true` when `DownloadFeed` returns `true` (completes without exception). A failed feed download does not advance the download timestamp.

---

### Requirement 10: InvitedClub Plugin â€” Image Retry (Pre-Post Housekeeping)

**User Story:** As an InvitedClub operations user, I want the platform to automatically retry attaching invoice images that previously failed, so that invoices already created in Oracle Fusion get their PDF attachments without manual intervention.

#### Acceptance Criteria

1. THE InvitedClubPlugin SHALL call `RetryPostImages` before executing the main `PostData` on every scheduled execution run.
2. THE InvitedClubPlugin SHALL call the stored procedure `InvitedClub_GetFailedImagesData` with parameters `@HeaderTable`, `@ImagePostRetryLimit`, and `@InvitedFailPostQueueId` to retrieve records that have an `InvoiceId` but no `AttachedDocumentId` and whose `ImagePostRetryCount` is less than `ImagePostRetryLimit`.
3. FOR EACH failed image record, THE InvitedClubPlugin SHALL retrieve the invoice image from S3 (non-legacy jobs) or from the local file system (legacy jobs) and attempt to POST it to `{PostServiceURL}/{InvoiceId}/child/attachments`.
4. WHEN the attachment POST returns HTTP 201, THE InvitedClubPlugin SHALL update `AttachedDocumentId` on the header table row and route the workitem to `SuccessQueueId` via `WORKITEM_ROUTE`.
5. WHEN the attachment POST fails, THE InvitedClubPlugin SHALL log the failure without routing the workitem.
6. AFTER each retry attempt (success or failure), THE InvitedClubPlugin SHALL increment `ImagePostRetryCount` on the header table row by executing `UPDATE {HeaderTable} SET ImagePostRetryCount = COALESCE(ImagePostRetryCount, 0) + 1 WHERE UID = @uid`.
7. WHEN the attachment POST fails after a successful invoice POST, the invoice already exists in Oracle Fusion with an `InvoiceId` but no `AttachedDocumentId`, creating an Orphaned_Invoice. The `RetryPostImages` mechanism is the ONLY recovery path for Orphaned_Invoices. The main `PostData` flow SHALL NOT re-attempt the invoice POST for these records; it SHALL route the workitem to `EdenredFailPostQueueId` and rely on `RetryPostImages` to attach the image on the next scheduled run.
8. THE InvitedClubPlugin SHALL use `config.UserId` (the `default_user_id` from configuration) as the `userId` for all `WORKITEM_ROUTE` calls within `RetryPostImages`. The retry mechanism always runs in the context of the scheduled (auto) execution and never uses a manually-provided userId.
9. THE InvitedClubPlugin SHALL always use `operationType = "Automatic Route:"` when calling `WORKITEM_ROUTE` from within `RetryPostImages`, because retry image posting always runs as part of the scheduled (auto) execution, never as a manual trigger.

---

### Requirement 11: InvitedClub Plugin â€” Invoice Posting (3-Step Oracle Fusion Post)

**User Story:** As an InvitedClub operations user, I want the platform to post approved invoices to Oracle Fusion Payables with their PDF attachments and optional tax calculation, so that vendors can be paid through the ERP system.

#### Acceptance Criteria

1. THE InvitedClubPlugin SHALL fetch pending workitems from `Workitems` joined with `WFInvitedClubsIndexHeader` where `JobId = @JobId AND StatusId IN ({source_queue_id})`.
2. FOR EACH workitem, THE InvitedClubPlugin SHALL load the invoice header and all line items by calling the stored procedure `InvitedClub_GetHeaderAndDetailData` with parameter `@UID`.
3. WHEN `is_legacy_job = 1`, THE InvitedClubPlugin SHALL read the invoice image from the local file system path `{image_parent_path}{ImagePath}`.
4. WHEN `is_legacy_job = 0`, THE InvitedClubPlugin SHALL download the invoice image from S3 as a Base64-encoded string using the `S3Utility` wrapper.
5. WHEN the invoice image cannot be found (S3 returns null or the local file does not exist), THE InvitedClubPlugin SHALL route the workitem to `EdenredFailPostQueueId` via `WORKITEM_ROUTE`, update the `question` field on the header table with a descriptive message, call `UpdateInProcessFlagAfterPost` IMMEDIATELY after routing (not deferred to a finally block), add the record to the failed images list, and skip to the next workitem. The `PostInProcess` flag is cleared inline on the image-fail path.
6. WHEN `RequesterId` is null or empty on the invoice header, THE InvitedClubPlugin SHALL route the workitem to `InvitedFailPostQueueId` via `WORKITEM_ROUTE`, set the `question` field to `"RequesterId not found in HR Feed"`, and skip to the next workitem.
7. THE InvitedClubPlugin SHALL build an `InvoiceRequest` JSON payload containing: `InvoiceNumber`, `InvoiceCurrency`, `PaymentCurrency`, `InvoiceAmount`, `InvoiceDate`, `BusinessUnit`, `Supplier`, `SupplierSite`, `RequesterId`, `AccountingDate`, `Description`, `InvoiceType`, `LegalEntity`, `LegalEntityIdentifier`, `LiabilityDistribution`, `RoutingAttribute2`, `InvoiceSource`, `invoiceDff` (array with `payor`), and `invoiceLines` (array with `LineNumber`, `LineAmount`, `ShipToLocation`, `DistributionCombination`, and `invoiceDistributions`).
8. WHEN `UseTax = "NO"` on the invoice header, THE InvitedClubPlugin SHALL remove the `ShipToLocation` field from every invoice line in the payload before posting.
9. WHEN `UseTax = "YES"` on the invoice header, THE InvitedClubPlugin SHALL retain `ShipToLocation` on all invoice lines.
10. THE InvitedClubPlugin SHALL POST the invoice payload to `{PostServiceURL}` using HTTP Basic Authentication (`AuthUserName:AuthPassword` Base64-encoded) and expect HTTP 201 Created.
11. WHEN the invoice POST returns HTTP 201, THE InvitedClubPlugin SHALL extract `InvoiceId` from the response JSON and update `WFInvitedClubsIndexHeader.InvoiceId` for the workitem.
12. WHEN the invoice POST returns any status other than HTTP 201, THE InvitedClubPlugin SHALL clear `GLDate` to NULL on the header table row, route the workitem to `InvitedFailPostQueueId`, write a history record, and skip to the next workitem.
13. AFTER a successful invoice POST, THE InvitedClubPlugin SHALL POST the invoice image attachment to `{PostServiceURL}/{InvoiceId}/child/attachments` with body `{"Type":"File","FileName":"invoice.pdf","Title":"invoice.pdf","Category":"From Supplier","FileContents":"{base64}"}` and expect HTTP 201 Created.
14. WHEN the attachment POST returns HTTP 201, THE InvitedClubPlugin SHALL extract `AttachedDocumentId` from the response JSON and update `WFInvitedClubsIndexHeader.AttachedDocumentId`.
15. WHEN the attachment POST returns any status other than HTTP 201, THE InvitedClubPlugin SHALL route the workitem to `EdenredFailPostQueueId` (the image will be retried on the next run by `RetryPostImages`).
16. WHEN `UseTax = "YES"` AND the attachment POST succeeded, THE InvitedClubPlugin SHALL POST to `{PostServiceURL}/action/calculateTax` with body `{"InvoiceNumber":"{InvoiceNumber}","Supplier":"{Supplier}"}` and expect HTTP 200 OK.
17. WHEN the `calculateTax` POST returns HTTP 200, THE InvitedClubPlugin SHALL route the workitem to `SuccessQueueId`.
18. WHEN the `calculateTax` POST returns any status other than HTTP 200, THE InvitedClubPlugin SHALL route the workitem to `InvitedFailPostQueueId`.
19. WHEN `UseTax = "NO"` AND the attachment POST succeeded, THE InvitedClubPlugin SHALL route the workitem to `SuccessQueueId` without calling `calculateTax`.
20. THE InvitedClubPlugin SHALL insert a history record into `post_to_invitedclub_history` ONLY for workitems where at least one Oracle Fusion API call was attempted (i.e., the workitem passed both the image availability check and the `RequesterId` validation). Specifically: workitems routed to `EdenredFailPostQueueId` due to image-not-found BEFORE any API call do NOT receive a history record; workitems routed to `InvitedFailPostQueueId` due to `RequesterId` being null or empty BEFORE any API call do NOT receive a history record. THE InvitedClubPlugin SHALL write a history record for all workitems where at least one Oracle Fusion API call was attempted: invoice POST fail, attachment POST fail, calculateTax POST fail, and full success. The history record captures whatever was attempted, with empty strings for steps not reached.
21. AFTER processing all workitems in a batch, WHEN any workitems were routed to `EdenredFailPostQueueId` due to image failure, THE InvitedClubPlugin SHALL send an image failure email notification to `emailConfig.EmailToHelpDesk` (split by semicolon), NOT to `emailConfig.EmailTo`. The email uses `emailConfig.EmailTemplateImageFail` as the HTML template and `emailConfig.EmailSubjectImageFail` as the subject. The email is sent ONLY WHEN BOTH `emailConfig.EmailTemplateImageFail` is not null/whitespace AND `emailConfig.EmailToHelpDesk` has a non-zero length. THE InvitedClubPlugin SHALL build the email body by reading the HTML template from `emailConfig.EmailTemplateImageFail`, generating an HTML table from the failed records list using `GenerateHtmlTable()`, and replacing the `#MissingImagesTable#` placeholder in the template with the generated HTML table.
22. AFTER completing a posting run, THE InvitedClubPlugin SHALL NOT call `UpdateLastPostTime` internally. `UpdateLastPostTime` is called by the orchestrator (the Post_Worker) AFTER `PostData()` returns, not inside `PostData` itself. The InvitedClubPlugin's `PostData` method does NOT call `UpdateLastPostTime`.
23. WHEN processing in auto mode (`TriggerType = "Scheduled"`), THE InvitedClubPlugin SHALL pass `operationType = "Automatic Route:"` to `WORKITEM_ROUTE`. WHEN processing in manual mode (`TriggerType = "Manual"`), THE InvitedClubPlugin SHALL pass `operationType = "Manual Route:"` to `WORKITEM_ROUTE`. The `manually_posted` field in `post_to_invitedclub_history` SHALL be `false` for auto runs and `true` for manual runs.
24. WHEN processing in auto mode, THE InvitedClubPlugin SHALL use `configuration.UserId` (the `default_user_id` from configuration) as the `userId` for all `WORKITEM_ROUTE` and `GENERALLOG_INSERT` calls. WHEN processing in manual mode, THE InvitedClubPlugin SHALL use the `userId` passed in the manual post request.
25. THE InvitedClubPlugin SHALL call `GENERALLOG_INSERT` with `@operationType = "Post To InvitedClubs"` and `@sourceObject = "Contents"` on BOTH success and failure paths for each workitem. On success, it is called after invoice post success and after attachment success. On failure, it is called with the failure reason as `@comments`.
26. THE InvitedClubPlugin SHALL use the following exact Content-Type headers for Oracle Fusion API calls: Invoice POST: `Content-Type: application/json`; Attachment POST: `Content-Type: application/vnd.oracle.adf.resourceitem+json`; CalculateTax POST: `Content-Type: application/vnd.oracle.adf.action+json`. These are Oracle Fusion-specific requirements and must be set exactly as shown.
27. THE InvitedClubPlugin SHALL use `request.AddJsonBody(invoiceCalculateTaxRequestJson)` (not `AddParameter`) when posting to the `calculateTax` endpoint, as required by Oracle Fusion's action endpoint contract.
28. ALL HTTP REST client calls to Oracle Fusion for invoice posting (invoice POST, attachment POST, calculateTax POST) SHALL use an infinite timeout (`Timeout = -1` in RestSharp). Oracle Fusion API calls can be slow and must not be interrupted by a client-side timeout.
29. WHEN an unhandled exception occurs during per-workitem processing (inside the foreach loop), THE InvitedClubPlugin SHALL call `UpdateInProcessFlagAfterPost` to clear the `PostInProcess` flag, log the exception, add a failure response to the return list, and continue to the next workitem. No history record is written for exception-path workitems.
30. THE Platform SHALL implement a `GenerateHtmlTable()` extension method on `DataTable` that converts a DataTable to an HTML `<table>` string. This method is used to generate the image failure email body.
31. WHEN processing in auto mode (scheduled), THE InvitedClubPlugin does NOT call `GetAPIResponseTypes` before the workitem loop. The `apiResponseTypes` list is empty in auto mode. As a result, `InvoicePostResponse.ResponseCode` and `InvoicePostResponse.ResponseMessage` are NOT populated for auto-mode executions (the `responseType` lookup returns null). This is existing behavior that must be preserved. The new platform MAY improve this by always loading `apiResponseTypes` regardless of trigger type, but must not break the return contract.

---

### Requirement 12: Manual Post Trigger

**User Story:** As a Workflow UI user, I want to trigger an invoice post manually and receive an immediate result, so that I can confirm whether a specific invoice was posted successfully without waiting for the next scheduled run.

#### Acceptance Criteria

1. THE Platform SHALL expose an API Gateway endpoint `POST /api/post/{jobId}` and `POST /api/post/{jobId}/items/{itemIds}` for manual post triggers from the Workflow UI.
2. WHEN a manual post request is received, THE Platform SHALL call the Post_Worker Fargate service directly via an internal HTTP endpoint and SHALL NOT route the request through SQS.
3. THE Post_Worker SHALL process the manual post request synchronously and return a result to the calling Web API before the HTTP connection times out.
4. THE Platform SHALL return a JSON response to the Workflow UI containing: `ExecutionId`, `Status` (`"Success"` or `"Failed"`), `ItemId` (when a single item is posted), `RecordsProcessed`, `RecordsSuccess`, `RecordsFailed`, and `DurationSeconds`.
5. WHEN `itemIds` is provided in the request, THE Post_Worker SHALL process only those specific workitems and SHALL NOT process other pending workitems in the source queue.
6. THE API Gateway endpoint SHALL require an API key for authentication.
7. THE Platform SHALL also expose `POST /api/feed/{jobId}` for manual feed download triggers, following the same direct-call pattern.
8. THE Platform SHALL expose `GET /api/status/{executionId}` to retrieve the result of a completed execution from `generic_execution_history`.
9. WHEN a manual post is triggered with specific `itemIds`, THE Post_Worker SHALL call `GetWorkitemDataByItemId(itemIds)` to fetch the workitems, read the `StatusId` from the first returned row, and find the matching `InvitedClubConfig` where `SourceQueueId == StatusId`. WHEN no matching configuration is found for the `StatusId`, THE Post_Worker SHALL return `ResponseCode = -1` with message `"Missing Configuration."` without processing any invoices.
10. WHEN a manual post is triggered, THE Post_Worker SHALL call `GetAPIResponseTypes(configuration)` to load the response type mappings from `api_response_configuration` and use them in the `InvoicePostResponse` return values, identical to the auto-post behavior.

---

### Requirement 13: Schedule Execution Window Logic

**User Story:** As a platform engineer, I want the schedule checking logic to match the existing Windows Service behavior exactly, so that jobs run at the same times they do today during the migration period.

#### Acceptance Criteria

1. WHEN `execution_time` (HH:mm format) is used in `generic_execution_schedule`, THE Platform SHALL execute the job only when the current time is within the 30-minute window starting at the configured `execution_time`.
2. THE Platform SHALL check that `last_post_time` is in the past AND at least 30 minutes ago before executing a posting job, preventing duplicate runs within the same 30-minute window.
3. WHEN `cron_expression` is used in `generic_execution_schedule`, THE Platform SHALL delegate schedule enforcement to EventBridge Scheduler and SHALL NOT apply the 30-minute window check.
4. THE Platform SHALL check that `last_download_time` is before today's date before executing a feed download, ensuring feed downloads run at most once per calendar day.
5. WHEN `allow_auto_post = 0` in `generic_job_configuration`, THE Platform SHALL skip the posting step for that job even if the schedule window is active.
6. WHEN `GetExecutionSchedule` returns null or an empty list for a configuration, THE InvitedClubPlugin SHALL skip posting for that configuration entirely, even if `AllowAutoPost = true`. Posting only proceeds when at least one schedule entry exists.

---

### Requirement 14: No-Duplicate Posting Guarantee

**User Story:** As a finance operations user, I want a guarantee that no invoice is ever posted to Oracle Fusion more than once, so that duplicate payments are prevented.

#### Acceptance Criteria

1. THE Platform SHALL set `PostInProcess = 1` on the header table row before beginning any API call for that workitem. **Note: This is NEW behavior being added by the platform. The existing Windows Service code does NOT set `PostInProcess = 1` before processing. This is a deliberate improvement.**
2. THE Platform SHALL exclude workitems where `PostInProcess = 1` from the workitem fetch query by adding `AND ISNULL(h.PostInProcess, 0) = 0` to the `GetWorkitemData` query, preventing concurrent workers from picking up the same invoice. **Note: This filter is NEW behavior being added by the platform. The existing `GetWorkitemData` query does NOT include this filter.**
3. THE Platform SHALL clear `PostInProcess = 0` in a `finally` block after processing each workitem, ensuring the flag is never permanently stuck.
4. WHEN a Fargate task crashes and restarts, THE Platform SHALL only process workitems still in the source queue (not yet routed to success or failure queues), naturally skipping already-processed invoices.
5. THE Platform SHALL use the SQS visibility timeout of 7200 seconds (2 hours) to prevent two Fargate tasks from processing the same SQS message concurrently.

---

### Requirement 15: No-Invoice-Loss Guarantee

**User Story:** As a finance operations user, I want a guarantee that no invoice is ever silently lost due to a system failure, so that every invoice is either successfully posted or explicitly routed to a failure queue with a reason.

#### Acceptance Criteria

1. THE Platform SHALL route every workitem to exactly one queue (success, fail, image-fail, question, or terminated) after processing â€” no workitem SHALL remain in the source queue after a completed processing attempt.
2. WHEN an unhandled exception occurs during workitem processing, THE Platform SHALL catch the exception, log it to CloudWatch, route the workitem to `primary_fail_queue_id`, and continue processing the next workitem.
3. WHEN an SQS message fails processing 3 times, THE Platform SHALL move it to the DLQ and trigger an SNS alert, ensuring the failure is visible to the operations team.
4. THE Platform SHALL write a history record to `generic_post_history` for every workitem processed, regardless of outcome.
5. WHEN the `question` field is set on a header table row, THE Platform SHALL preserve the value so that operations staff can see the failure reason in the Workflow UI.

---

### Requirement 16: Security Requirements

**User Story:** As a security engineer, I want all credentials, network traffic, and access controls to follow AWS security best practices, so that the platform does not introduce security vulnerabilities.

#### Acceptance Criteria

1. THE Platform SHALL store all credentials (database passwords, Oracle Fusion API credentials, SMTP passwords, S3 access keys) exclusively in Secrets_Manager and SHALL NOT store them in source code, configuration files, environment variables, or database tables in plaintext.
2. THE Platform SHALL set `ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12` at startup to support Oracle Fusion's TLS requirements. All three TLS versions (1.0, 1.1, and 1.2) must be enabled for Oracle Fusion compatibility.
3. THE Platform SHALL define TWO separate IAM roles per ECS service:
   - **ECS Task Execution Role** (`ips-autopost-ecs-execution-role-{env}`): Grants ECS the ability to pull Docker images from ECR and write logs to CloudWatch. Uses the AWS managed policy `AmazonECSTaskExecutionRolePolicy`.
   - **ECS Task Role** (`ips-autopost-ecs-task-role-{env}`): Grants the running application code the permissions it needs: `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:GetQueueAttributes`, `sqs:GetQueueUrl` on the specific SQS queues; `cloudwatch:PutMetricData` on all resources; `s3:GetObject`, `s3:PutObject` on specific S3 buckets; `secretsmanager:GetSecretValue` on specific secret ARNs.
4. THE Platform SHALL run in private VPC subnets with no direct inbound internet access.
5. THE Platform SHALL use a **NAT Gateway** in the public subnet to route outbound traffic from private subnets to the internet (for external ERP API calls) and to AWS services (S3, SQS, Secrets_Manager, CloudWatch). VPC Endpoints are optional and may be added later for cost optimization. The NAT Gateway approach matches the existing production pattern in IPS infrastructure.
6. THE API Gateway endpoint SHALL require an API key and SHALL be protected by a usage plan with rate limiting.
7. THE Platform SHALL use parameterized SQL queries for all database operations and SHALL NOT construct SQL by string concatenation of user-supplied input.
8. THE ECS Security Group SHALL allow the following outbound (egress) traffic only:
   - TCP port 1433 to `0.0.0.0/0` (SQL Server / RDS access).
   - TCP port 443 to `0.0.0.0/0` (HTTPS for AWS services and external ERP APIs).
   - TCP port 80 to `0.0.0.0/0` (HTTP outbound).
   No inbound rules are needed â€” ECS Fargate tasks do not accept inbound connections.
9. THE Platform SHALL add an ingress rule to the existing RDS Security Group allowing TCP port 1433 from the ECS Security Group. This SHALL be implemented via an `AWS::EC2::SecurityGroupIngress` resource in CloudFormation, not by modifying the existing security group directly.

---

### Requirement 17: Performance and Scalability Requirements

**User Story:** As a platform engineer, I want the platform to handle large invoice batches and long-running feed downloads without degradation, so that it can replace all 15+ Windows Services including the most demanding workloads.

#### Acceptance Criteria

1. THE Post_Worker SHALL process a batch of 10,000 invoices for a single client without timeout, memory exhaustion, or data loss.
2. THE Feed_Worker SHALL complete a Media SOAP feed download that takes 30â€“40 minutes without timeout.
3. THE Platform SHALL process each invoice in a batch independently so that a failure on one invoice does not prevent processing of subsequent invoices.
4. THE Platform SHALL use `SqlBulkCopy` with a timeout of 600 seconds for all bulk feed data inserts into RDS.
5. WHEN multiple clients have jobs scheduled at the same time, THE Platform SHALL scale Fargate tasks up to handle concurrent jobs, with each task processing one SQS message at a time.
6. THE Platform SHALL not hold database connections open between workitem processing iterations; each database operation SHALL open and close its connection using the connection pool.
7. THE Platform SHALL support a database connection pool with `Max Pool Size = 2000` as configured in the RDS connection string.

---

### Requirement 18: Observability and Alerting

**User Story:** As an operations engineer, I want centralized logging, metrics, and alerts for all clients in one place, so that I can monitor the health of all 15+ integrations from a single CloudWatch dashboard without RDP-ing into individual servers.

#### Acceptance Criteria

1. THE Platform SHALL write all log entries to CloudWatch Logs in structured format including: timestamp, log level, client_type, job_id, item_id (when applicable), step_name, and message. THE Platform SHALL publish all custom CloudWatch metrics under the namespace `IPS/AutoPost/{Environment}` (e.g., `IPS/AutoPost/production`), dimensioned by `ClientType` and `JobId`. This follows the production pattern of `{AppName}/{Environment}` namespacing.
2. THE Platform SHALL create a CloudWatch dashboard named `IPS-AutoPost-Operations` displaying: jobs run today (all clients), success/fail rate per client, average post duration per client, and SQS queue depth.
3. THE Platform SHALL publish a CloudWatch alarm when `PostFailedCount` exceeds **5 errors** in a 5-minute period (2 evaluation periods of 300 seconds each) for any client. This matches the production-validated threshold from the existing IPS infrastructure.
4. THE Platform SHALL publish a CloudWatch alarm when any DLQ message count exceeds 0.
5. THE Platform SHALL publish a CloudWatch alarm when a Fargate task stops with a non-zero exit code.
6. THE Platform SHALL use AWS X-Ray tracing to record end-to-end execution spans including: RDS query time, S3 retrieval time, and external ERP API call time per workitem.
7. THE Platform SHALL retain all CloudWatch Logs for 90 days.

---

### Requirement 19: Correctness Properties for Property-Based Testing

**User Story:** As a quality engineer, I want formally defined correctness properties that can be validated through property-based testing, so that the platform's invariants are continuously verified across a wide range of inputs.

#### Acceptance Criteria

1. **PostInProcess Invariant**: FOR ALL workitems processed by the Post_Worker, THE Platform SHALL guarantee that `PostInProcess = 0` after the processing attempt completes, regardless of whether the attempt succeeded or failed. This is an invariant that must hold after every transformation.

2. **No-Duplicate Routing Invariant**: FOR ALL workitems in a batch, THE Platform SHALL guarantee that each workitem is routed to exactly one queue. A workitem that starts in `source_queue_id` SHALL end in exactly one of: `success_queue_id`, `primary_fail_queue_id`, `secondary_fail_queue_id`, `question_queue_id`, or `terminated_queue_id`. No workitem SHALL remain in `source_queue_id` after a completed processing attempt.

3. **History Completeness Invariant**: FOR ALL workitems processed, THE Platform SHALL guarantee that a corresponding row exists in `generic_post_history` (or `post_to_invitedclub_history` for InvitedClub). The count of history rows inserted SHALL equal the count of workitems processed in a batch.

4. **UseTax Round-Trip Property**: FOR ALL invoice payloads where `UseTax = "NO"`, THE Platform SHALL guarantee that no `ShipToLocation` field appears in any invoice line of the serialized JSON payload. FOR ALL invoice payloads where `UseTax = "YES"`, THE Platform SHALL guarantee that `ShipToLocation` is present on all invoice lines that have a non-null `ShipToLocation` in the source data. This is a round-trip property: the transformation of the payload based on `UseTax` must be verifiable by inspecting the serialized output.

5. **Feed Idempotence Property**: FOR ALL feed download operations, THE Platform SHALL guarantee that running the feed download twice in succession produces the same final state in the feed tables as running it once. Specifically: after a full supplier feed download, running the download again SHALL result in the same row count and data in `InvitedClubSupplier` as after the first run (truncate-then-insert is idempotent).

6. **Incremental Feed Subset Property (Metamorphic)**: FOR ALL incremental supplier address downloads, THE Platform SHALL guarantee that the set of supplier IDs fetched is a subset of the full supplier ID list. The count of supplier IDs fetched incrementally SHALL be less than or equal to the count fetched on an initial (full) call: `count(incremental_supplier_ids) <= count(all_supplier_ids)`.

7. **Pagination Completeness Property**: FOR ALL paginated Oracle Fusion API calls (Supplier, SupplierAddress, SupplierSite, COA), THE Platform SHALL guarantee that the total records inserted into the feed table equals the sum of all items returned across all pages. No page SHALL be skipped and no record SHALL be duplicated due to pagination logic.

8. **Error Condition Routing Property**: FOR ALL workitems where the invoice image is not found, THE Platform SHALL route the workitem to `EdenredFailPostQueueId` and SHALL NOT call the Oracle Fusion invoice POST API. FOR ALL workitems where `RequesterId` is null or empty, THE Platform SHALL route the workitem to `InvitedFailPostQueueId` and SHALL NOT call the Oracle Fusion invoice POST API. These error conditions must be validated with generated bad inputs.

9. **Retry Idempotence Property**: FOR ALL image retry operations, THE Platform SHALL guarantee that retrying the same failed image record multiple times does not create duplicate attachment records in Oracle Fusion. The `ImagePostRetryCount` SHALL increment by exactly 1 per retry attempt, and the retry SHALL stop when `ImagePostRetryCount >= ImagePostRetryLimit`.

10. **SQS Message Delivery Guarantee**: FOR ALL scheduled jobs triggered by EventBridge, THE Platform SHALL guarantee that the SQS message is either processed successfully (deleted from queue) or moved to the DLQ after exactly 3 failed attempts. No message SHALL be silently discarded.

---

### Requirement 20: Migration Compatibility

**User Story:** As a project manager, I want the platform to be deployable alongside the existing Windows Services during a migration period, so that we can validate InvitedClub behavior before decommissioning the old service.

#### Acceptance Criteria

1. THE Platform SHALL produce identical routing outcomes for InvitedClub invoices as the current Windows Service implementation, routing to the same queue IDs for the same input conditions. **Note: The current Windows Service does NOT set `PostInProcess = 1` before processing and does NOT filter `PostInProcess = 0` in the workitem query. These are new behaviors added by the platform, not behaviors being preserved from the old code.**
2. THE Platform SHALL write history records to `post_to_invitedclub_history` with the same columns and values as the current Windows Service implementation.
3. THE Platform SHALL call all stored procedures (`get_invitedclub_configuration`, `GetExecutionSchedule`, `InvitedClub_GetHeaderAndDetailData`, `InvitedClub_GetFailedImagesData`, `InvitedClub_GetSupplierDataToExport`, `InvitedClub_UpdateSupplierSiteInSupplierAddress`, `WORKITEM_ROUTE`, `GENERALLOG_INSERT`) with the exact same parameters as the current implementation. The `GetExecutionSchedule` stored procedure uses parameter `@file_creation_config_id` (not `@config_id` or `@id`) for the configuration ID, and `@job_id` for the job ID. Both are `SqlDbType.Int`.
4. THE Platform SHALL support a parallel-run mode where both the Windows Service and the Fargate worker are active but only one processes each workitem (controlled by `allow_auto_post` flag in the respective configuration tables).
5. WHEN the InvitedClub plugin is validated in production, THE Platform SHALL allow the Windows Service to be decommissioned without any code changes to the platform.

---

### Requirement 21: SevitaPlugin â€” Invoice Posting

**User Story:** As a Sevita operations user, I want the platform to post approved invoices to the Sevita AP system with their PDF attachments and line item data, so that vendors can be paid through the Sevita system.

#### Acceptance Criteria

1. THE SevitaPlugin SHALL use OAuth2 `client_credentials` grant to obtain a Bearer token from `api_access_token_url` using `client_id` and `client_secret` from `client_config_json`. The token SHALL be cached and reused until `token_expiration_min` minutes have elapsed.

2. BEFORE processing any workitems in a batch, THE SevitaPlugin SHALL load `ValidIds` by executing two queries: `SELECT Supplier FROM Sevita_Supplier_SiteInformation_Feed` (VendorIds) and `SELECT EmployeeID FROM Sevita_Employee_Feed` (EmployeeIds). These are loaded once per batch via `OnBeforePostAsync`.

3. FOR EACH workitem, THE SevitaPlugin SHALL load header and detail data by calling the stored procedure `GetSevitaHeaderAndDetailDataByItem` with parameter `@UID`.

4. THE SevitaPlugin SHALL always retrieve the invoice image from S3 (never from local file system â€” no legacy job path). WHEN the image cannot be found, THE SevitaPlugin SHALL route the workitem to `FailedPostsQueueId`, update the `question` field with `"Image is not available."`, write a history record with empty `InvoiceRequestJson=""` and `InvoiceResponseJson=""` (Sevita ALWAYS writes history even for image failures â€” this differs from InvitedClub which does NOT write history for image-fail early exits), call `UpdateSevitaHeaderPostFields(@UID)` to clear the post-in-process state, add the record to the failed records list, and continue to the next workitem.

5. THE SevitaPlugin SHALL group detail rows by composite key (`alias` + `naturalAccountNumber`), sum the `LineAmount` for each group, and build `lineItems` array entries with `alias`, `naturalAccountNumber`, `amount` (formatted to 2 decimal places), and `edenredLineItemId` (= `edenredInvoiceId + "_" + lineItemCount`).

6. THE SevitaPlugin SHALL validate the following BEFORE calling the Sevita API:
   - Line sum validation: `SUM(lineItem.amount) == header.InvoiceAmount`. If not equal, route to `FailedPostsQueueId` with message `"Line sum does not match invoice header."`.
   - For PO configs (`is_PO_record = true`): validate required fields (`vendorId`, `invoiceDate`, `invoiceNumber`, `checkMemo`), validate `vendorId` exists in `ValidIds.VendorIds`. Set `checkMemo = "PO#"` if empty.
   - For Non-PO configs (`is_PO_record = false`): validate required fields (`vendorId`, `employeeId`, `invoiceDate`, `invoiceNumber`, `checkMemo`, `expensePeriod`), validate BOTH `vendorId` in `ValidIds.VendorIds` AND `employeeId` in `ValidIds.EmployeeIds`. If any line has `naturalAccountNumber = "174098"`, `cerfTrackingNumber` is required.
   - Validate attachment required fields: `fileName`, `fileBase`, `fileUrl`, `docid`.
   - WHEN any validation fails, route to `FailedPostsQueueId` with the failure reason in `question` field. Do NOT call the Sevita API.

7. THE SevitaPlugin SHALL build the `InvoiceRequest` payload with: `vendorId`, `edenredInvoiceId` (= `documentId` trimmed), `employeeId`, `payAlone`, `invoiceRelatedToZycusPurchase`, `zycusInvoiceNumber` (only when `invoiceRelatedToZycusPurchase = true`), `invoiceNumber`, `invoiceDate`, `expensePeriod`, `checkMemo`, `cerfTrackingNumber`, `remittanceRequired`, `attachments[]` (with `fileName`, `fileBase`, `fileUrl`, `docid`), and `lineItems[]`.

8. THE SevitaPlugin SHALL serialize the payload as a JSON array `[{...}]` (wrapped in array brackets) before posting.

9. WHEN `post_json_path` is configured in `client_config_json`, THE SevitaPlugin SHALL upload the serialized request JSON to S3 at `{post_json_path}/{itemId}_{timestamp}.json` before calling the Sevita API.

10. THE SevitaPlugin SHALL POST the payload to `InvoicePostURL` using `Authorization: Bearer {token}` header with `Content-Type: application/json` and `Timeout = -1`.

11. WHEN the Sevita API returns HTTP 201 Created, THE SevitaPlugin SHALL extract `InvoiceId` from the first property name of the `invoiceIds` object in the response JSON and route the workitem to `SuccessQueueId`.

12. WHEN the Sevita API returns HTTP 500 Internal Server Error, THE SevitaPlugin SHALL set the error message to `"Internal Server error occurred while posting invoice."` and route to `FailedPostsQueueId`.

13. WHEN the Sevita API returns any other non-201 status, THE SevitaPlugin SHALL extract error details from `recordErrors`, `message`, `invoiceIds`, and `failedRecords` fields in the response JSON and route to `FailedPostsQueueId`.

14. THE SevitaPlugin SHALL save history by calling `UpdateHistory` with the request JSON modified to have `fileBase = null` on all attachments (to avoid storing large base64 strings). The history table is `sevita_posted_records_history` with columns: `job_id`, `item_id`, `post_request`, `post_response`, `post_date`, `posted_by`, `manually_posted`, `Comment`.

15. THE SevitaPlugin SHALL call `UpdateSevitaHeaderPostFields(@UID)` (stored procedure) to clear the post-in-process state after each workitem, instead of a direct SQL UPDATE.

16. AFTER processing all workitems in a batch, WHEN any workitems failed, THE SevitaPlugin SHALL send a failure notification email to `FailedPostConfiguration.EmailTo` (split by semicolon) using the `FailedPostConfiguration.EmailTemplate` HTML template with a `GenerateHtmlTable()` HTML table of failed records (excluding the `IsSendNotification` column). THE SevitaPlugin SHALL replace the `[[AppendTable]]` placeholder in the template with the generated HTML table. **Note: Sevita uses `[[AppendTable]]` as the placeholder â€” NOT `#MissingImagesTable#` which is InvitedClub's placeholder.**

17. THE SevitaPlugin SHALL call `get_sevita_configurations` stored procedure (with NO parameters) to load its configuration. This SP returns all Sevita configuration fields including email settings, OAuth2 credentials, queue IDs, and table names in a single result set.

18. THE SevitaPlugin SHALL NOT perform any feed download. `ExecuteFeedDownload` SHALL return `FeedResult.NotApplicable()`.

19. THE SevitaPlugin SHALL NOT perform any image retry (`RetryPostImages`). There is no orphaned invoice recovery mechanism for Sevita.

20. THE SevitaPlugin SHALL use `DefaultUserId` from configuration as `userId` in auto mode, and the passed-in `userId` in manual mode â€” identical to InvitedClub behavior.

21. THE SevitaPlugin SHALL call `GetAPIResponseTypes(configuration)` (from `api_response_configuration` table) at the start of each `PostData` execution for manual posts. The `sevita_response_configuration` table exists in the database but is NOT called in the current implementation â€” `GetSevitaPostResponseTypes` is defined but never invoked in `PostData`. The new platform SHALL preserve this existing behavior: only `api_response_configuration` is used for response type lookups.
22. THE SevitaPlugin SHALL set `SqlHelper.ConnectionString` ONCE at startup (not per-configuration in the processing loop). Unlike InvitedClub which reassigns `SqlHelper.ConnectionString` per-configuration, Sevita uses a single connection string for all operations. The per-configuration assignment is commented out in the existing Sevita code and SHALL NOT be implemented.

23. THE SevitaPlugin SHALL load `ValidIds` in `OnBeforePostAsync` using a direct `SqlConnection` with `SqlDataReader.NextResult()` to read both result sets in a single round trip: `SELECT Supplier FROM Sevita_Supplier_SiteInformation_Feed; SELECT EmployeeID FROM Sevita_Employee_Feed`. This uses raw ADO.NET (not SqlHelper) because SqlHelper's `ExecuteDatasetAsync` does not support multi-statement queries with `NextResult()`.

24. THE SevitaPlugin SHALL POST the invoice payload using `request.AddParameter("application/json", invoiceRequestJson, ParameterType.RequestBody)` — NOT `AddJsonBody`. This is different from InvitedClub's `calculateTax` endpoint which uses `AddJsonBody`. The `ParameterType.RequestBody` approach is required by the Sevita API contract.

25. THE SevitaPlugin SHALL save history by parsing the `invoiceRequestJson` as a `JArray` (not `JObject`) before nulling out `fileBase` on each attachment, because the Sevita payload is wrapped in a JSON array `[{...}]`. The implementation SHALL use `JArray.Parse(invoiceRequestJson)` then iterate items to set `fileBase = null` on each attachment before serializing back to string for storage.

26. THE SevitaPlugin SHALL load `DBErrorEmailConfiguration` from the `get_sevita_configurations` SP result, which includes fields: `db_error_to_email_address`, `db_error_cc_email_address`, `db_error_email_subject`, and `db_error_email_template`. This configuration is used for database error email notifications separate from the post failure notification email.

---

### Requirement 22: Infrastructure as Code â€” CloudFormation and CI/CD Pipeline

**User Story:** As a DevOps engineer, I want all AWS infrastructure defined as CloudFormation templates split into three separate stacks and deployed via a three-job CI/CD pipeline, so that infrastructure, application, and monitoring can be deployed and updated independently with full traceability.

#### Acceptance Criteria

1. THE Platform SHALL define infrastructure in three separate CloudFormation stacks deployed in sequence:
   - **Stack 1** (`ips-autopost-infra-{env}`): VPC subnets (1 public, 2 private across 2 AZs), NAT Gateway with Elastic IP, private route tables, ECS Security Group, ECR Repository, ECS Cluster, SQS queues (`ips-feed-queue`, `ips-post-queue`, `ips-feed-dlq`, `ips-post-dlq`), CloudWatch Log Groups, and S3 deployment bucket.
   - **Stack 2** (`ips-autopost-app-{env}`): ECS Task Execution Role, ECS Task Role, ECS Task Definitions (Feed Worker + Post Worker), ECS Services, Application Auto Scaling targets and policies.
   - **Stack 3** (`ips-autopost-monitoring-{env}`): CloudWatch Dashboard, application error alarms, DLQ alarms.

2. THE Stack 1 SHALL export outputs (VPC ID, private subnet IDs, security group ID, ECS cluster name, SQS queue URLs, ECR repository URI, log group names) that Stack 2 and Stack 3 import using `Fn::ImportValue`.

3. THE Platform SHALL use a `DeploymentId` parameter (set to the CI/CD run number) in the ECS Task Definition family name to force ECS to pick up new container images on every deployment: `Family: ips-autopost-{worker}-{env}-{DeploymentId}`.

4. THE ECR Repository SHALL be configured with `ScanOnPush: true` for vulnerability scanning and a lifecycle policy that deletes untagged images older than 7 days.

5. THE ECS Cluster SHALL have `containerInsights: enabled` for enhanced monitoring.

6. THE S3 deployment bucket SHALL have versioning enabled and all public access blocked.

7. THE CI/CD pipeline (GitHub Actions) SHALL deploy in three sequential jobs:
   - **infrastructure job**: Deploys Stack 1 (`infrastructure.yaml`), outputs SQS queue URL and ECR repository URI.
   - **application job** (depends on infrastructure): Builds Docker image, tags with git SHA, pushes to ECR, deploys Stack 2 (`application.yaml`) with the new image URI and deployment ID.
   - **monitoring job** (depends on infrastructure + application): Deploys Stack 3 (`monitoring.yaml`).

8. THE CI/CD pipeline SHALL be triggered manually (`workflow_dispatch`) with environment selection (`uat` or `production`). AWS credentials SHALL be stored as GitHub Actions secrets.


---

### Requirement 23: Architecture Patterns â€” MediatR, CorrelationId, Granular Metrics, EF Core

**User Story:** As a platform engineer, I want modern .NET architecture patterns (MediatR CQRS, Pipeline Behaviors, CorrelationId tracking, EF Core migrations, granular CloudWatch metrics) adopted from the production `GenericMissingInvoicesProcess` project, so that the platform is maintainable, testable, and observable at scale.

#### Acceptance Criteria

1. THE Platform SHALL use MediatR Command/Handler pattern. The SQS consumers (FeedWorker, PostWorker) SHALL send `ExecutePostCommand` or `ExecuteFeedCommand` via `IMediator.Send()`. Handlers (`ExecutePostHandler`, `ExecuteFeedHandler`) SHALL contain all business logic. The SQS consumer SHALL know nothing about business logic â€” it only deserializes messages and sends commands.

2. THE Platform SHALL register MediatR Pipeline Behaviors that wrap every command automatically:
   - `LoggingBehavior<TRequest, TResponse>` â€” logs command start/end with CorrelationId, no handler changes needed.
   - `ValidationBehavior<TRequest, TResponse>` â€” runs FluentValidation before handler executes, throws `ValidationException` on failure.

3. THE Platform SHALL implement `ICorrelationIdService` using `AsyncLocal<string>` to store a unique correlation ID per SQS message. The correlation ID SHALL flow through all async calls automatically. The service SHALL call `LogContext.PushProperty("CorrelationId", id)` to inject the ID into every Serilog log entry.

4. THE Serilog output template SHALL include `[{CorrelationId}]` so that all log entries for a job run are traceable in CloudWatch Logs Insights using `filter @message like /correlation-id-value/`.

5. THE Platform SHALL implement `ICloudWatchMetricsService` that publishes granular per-client metrics after each execution. The following metrics SHALL be published to namespace `IPS/AutoPost/{env}`, dimensioned by `ClientType` and `JobId`:
   - `PostStarted` â€” when PostWorker picks up SQS message
   - `PostCompleted` â€” when batch finishes successfully
   - `PostFailed` â€” when batch fails
   - `PostSuccessCount` â€” number of invoices routed to success queue
   - `PostFailedCount` â€” number of invoices routed to fail queue
   - `PostDurationSeconds` â€” total batch duration
   - `FeedStarted` â€” when FeedWorker picks up SQS message
   - `FeedCompleted` â€” when feed download finishes
   - `FeedRecordsDownloaded` â€” total records downloaded
   - `FeedDurationSeconds` â€” total feed duration
   - `ImageRetryAttempted` â€” when RetryPostImages runs
   - `ImageRetrySucceeded` â€” when image retry succeeds

6. THE Platform SHALL implement `DynamicRecord` model as a schema-agnostic data structure with `Dictionary<string, object?> Fields` and a `GetValue<T>(string fieldName)` method. This model SHALL be used by `GenericRestPlugin` to build JSON payloads from `generic_field_mapping` table rows without knowing column names at compile time.

7. THE Platform SHALL use Entity Framework Core to manage the 10 new generic tables (`generic_job_configuration`, `generic_execution_schedule`, `generic_feed_configuration`, `generic_auth_configuration`, `generic_queue_routing_rules`, `generic_post_history`, `generic_email_configuration`, `generic_feed_download_history`, `generic_execution_history`, `generic_field_mapping`). EF Core migrations SHALL be auto-applied at startup via `context.Database.Migrate()`. The existing Workflow tables (`Workitems`, `WFInvitedClubsIndexHeader`, etc.) SHALL NOT be managed by EF Core â€” they SHALL be accessed via SqlHelper only.

8. THE Platform SHALL implement `SecretsManagerConfigurationProvider` in the `Infrastructure/` folder that scans the following `appsettings.json` sections for values starting with `/`, fetches them from AWS Secrets Manager in parallel (30-second timeout), and injects the real values back into `IConfiguration`. All other code SHALL read `IConfiguration` normally without knowing about Secrets Manager. The following secret paths SHALL be supported:
   - `ConnectionStrings:Workflow` â†’ `/IPS/Common/{env}/Database/Workflow` â€” full connection string (or JSON with `AppConnectionString` key for RDS-managed secrets)
   - `Email:SmtpPassword` â†’ `/IPS/Common/{env}/Smtp` â€” SMTP password for email notifications
   - `ApiKey:Value` â†’ `/IPS/Common/{env}/ApiKey` â€” API key for the manual post endpoint
   - Oracle Fusion credentials â†’ `/IPS/InvitedClub/{env}/PostAuth` â€” Basic Auth credentials (fetched by InvitedClubPlugin at runtime, not at startup)
   - Sevita OAuth2 credentials â†’ `/IPS/Sevita/{env}/PostAuth` â€” client_id and client_secret (fetched by SevitaPlugin at runtime, not at startup)
   
   The startup scan covers `ConnectionStrings`, `Email`, and `ApiKey` sections. Plugin-specific credentials (`PostAuth`) are fetched on-demand by each plugin using `ConfigurationService.GetSecretAsync()`. The pattern: `"Workflow": "/IPS/Common/{env}/Database/Workflow"` in appsettings.json â†’ fetched at startup â†’ replaced with real connection string. The `SecretsManagerConfigurationProvider` uses the default AWS credential provider chain (IAM role for ECS tasks in production, environment variables for local development).

9. THE SQS consumers (FeedWorker, PostWorker) SHALL use `MaxNumberOfMessages = 10` (not 1) when polling SQS queues. This allows processing up to 10 messages per poll cycle, reducing API calls to SQS.

10. THE SQS consumers SHALL create a new DI scope (`_serviceProvider.CreateScope()`) for EACH message processed. This prevents state leakage between messages. Each scope SHALL have its own `IMediator`, `ICorrelationIdService`, and all scoped services.

11. THE `ExecutePostCommand` and `ExecuteFeedCommand` SHALL include an optional `Mode` field (string). The handler MAY check this field to override runtime behavior (e.g., `Mode = "DryRun"` skips actual posting, `Mode = "UAT"` overrides email API identifier). This allows behavior changes without redeployment â€” just send a different SQS message.

12. THE Platform SHALL use xUnit v3 (latest version, `xunit.v3` package) for all tests, matching the `GenericMissingInvoicesProcess` production pattern.

13. THE Platform SHALL use EF Core InMemory database provider (`Microsoft.EntityFrameworkCore.InMemory`) for integration tests. Tests SHALL create a fresh in-memory database per test using `UseInMemoryDatabase(Guid.NewGuid().ToString())`. This eliminates the need for a real database or Docker containers during testing.
2. **No-Duplicate Routing Invariant**: FOR ALL workitems processed, THE Platform SHALL guarantee that each workitem ends in exactly one destination queue after processing â€” it SHALL NOT remain in the source queue and SHALL NOT be routed to more than one queue.
3. **History Completeness Invariant**: FOR ALL batch executions, THE Platform SHALL guarantee that the count of history rows written equals the count of workitems where at least one external API call was attempted. Workitems that fail pre-API validation (image not found, RequesterId empty) SHALL NOT produce history rows.
4. **UseTax Round-Trip Property**: FOR ALL invoice payloads built by InvitedClubPlugin, WHEN `UseTax = "NO"`, no invoice line SHALL contain `ShipToLocation`; WHEN `UseTax = "YES"`, every invoice line SHALL contain `ShipToLocation`.
5. **Feed Idempotence Property**: FOR ALL feed download executions, running the feed download twice SHALL produce the same row count in the target tables as running it once (truncate-then-insert strategy).
6. **Incremental Feed Subset Property**: FOR ALL incremental feed runs, the set of supplier IDs fetched incrementally SHALL be a subset of the full supplier ID list, and the count SHALL be less than or equal to the full count.
7. **Pagination Completeness Property**: FOR ALL paginated API calls, the total records inserted into the database SHALL equal the sum of all items across all pages returned by the Oracle Fusion API.
8. **Error Condition Routing Property**: FOR ALL error conditions, the workitem SHALL be routed to the correct fail queue: image-not-found â†’ `EdenredFailPostQueueId` with zero API calls; RequesterId-empty â†’ `InvitedFailPostQueueId` with zero API calls; invoice POST fail â†’ `InvitedFailPostQueueId`; attachment POST fail â†’ `EdenredFailPostQueueId`; calculateTax fail â†’ `InvitedFailPostQueueId`.
9. **Retry Idempotence Property**: FOR ALL image retry executions, retrying an already-attached image SHALL NOT create a duplicate attachment in Oracle Fusion. The `ImagePostRetryCount` SHALL be incremented exactly once per retry attempt regardless of outcome.
10. **SQS Delivery Guarantee Property**: FOR ALL SQS messages, a message SHALL be deleted from the queue if and only if the corresponding job completed successfully. Failed jobs SHALL leave the message visible for retry up to `maxReceiveCount = 3` times before moving to the DLQ.


