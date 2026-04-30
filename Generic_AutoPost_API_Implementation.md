# Generic AutoPost API Platform - Complete Solution Design
### .NET Core 10 | AWS | Configuration-Driven | Multi-Client

> **Version 2.0** � Full rewrite based on deep analysis of all 15+ API libraries
> **Date**: April 23, 2026

---

## Table of Contents
1. [What We Are Solving](#1-what-we-are-solving)
2. [Deep Analysis - All 15+ API Libraries](#2-deep-analysis---all-15-api-libraries)
3. [What CAN Be Made Generic (Database-Driven)](#3-what-can-be-made-generic-database-driven)
4. [What CANNOT Be Made Generic - And Why](#4-what-cannot-be-made-generic---and-why)
5. [Complete New Solution Architecture](#5-complete-new-solution-architecture)
6. [Project Structure - .NET Core 10](#6-project-structure---net-core-10)
7. [Database Design - Generic Configuration Tables](#7-database-design---generic-configuration-tables)
8. [Client-Specific Plugin Folder Structure](#8-client-specific-plugin-folder-structure)
9. [Core Interfaces and Contracts](#9-core-interfaces-and-contracts)
10. [AWS Hosting Architecture](#10-aws-hosting-architecture)
11. [Implementation Roadmap](#11-implementation-roadmap)
12. [Conclusion - Plain English Summary](#12-conclusion---plain-english-summary)

---

## 1. What We Are Solving

### The Problem Today
You have **15+ separate API libraries** (and growing), each one is a copy-paste of the same skeleton with client-specific logic baked in. Every new client = a new Visual Studio project, new Windows Service, new deployment, new maintenance burden.

```
Today:
  Media_API_Lib          ? Windows Service ? EC2
  Invited_Club_API_Lib   ? Windows Service ? EC2
  Greenthal_Corporate_API_Lib ? Windows Service ? EC2
  MDS_API_Lib            ? Windows Service ? EC2
  MOB_API_Lib            ? Windows Service ? EC2
  Caliber_API_Lib        ? Windows Service ? EC2
  Akron_CH_API_Lib       ? Windows Service ? EC2
  Vantaca_API_Lib        ? Windows Service ? EC2
  Workday_API_Lib        ? Windows Service ? EC2
  Trump_API_Lib          ? Windows Service ? EC2
  Mincingespice_API_Lib  ? Windows Service ? EC2
  Michelman_API_Lib      ? Windows Service ? EC2
  Signature_API_Lib      ? Windows Service ? EC2
  Rent_Manager_Lib       ? Windows Service ? EC2
  ReactorNet_API_Lib     ? Windows Service ? EC2
  ... and more coming
```

**Each one has:**
- Its own `SqlHelper.cs` (identical in all)
- Its own `GetDataBAL.cs` (80% identical)
- Its own config model (`XxxConfigurations`)
- Its own config table (`post_to_xxx_configuration`)
- Its own config stored procedure (`wf_get_xxx_configurations`)
- Its own Windows Service shell (identical)
- Its own deployment pipeline

### The Goal
One platform. New client = add rows to database + write one plugin class. No new project, no new deployment, no new service.

```
Tomorrow:
  IPS.AutoPost.Platform  ? Single Service ? AWS (Lambda/ECS/Fargate)
    +-- Core (generic engine - never changes)
    +-- Plugins/
          +-- Media/          (client-specific logic)
          +-- InvitedClub/    (client-specific logic)
          +-- Greenthal/      (client-specific logic)
          +-- MDS/            (client-specific logic)
          +-- NewClient/      (add new folder = new client)
```

---

## 2. Deep Analysis - All 15+ API Libraries

### 2.1 Complete Library Inventory

| Library | Config Table | Config SP | Post Type | Feed Download | Special Logic |
|---|---|---|---|---|---|
| **Media_API_Lib** | `post_to_media_configuration` | `wf_get_media_configurations` | CSV ? SOAP (Advantage/MergeWorld) | Yes (Vendor, MediaOrder, GL, PO, Jobs) | 10-day chunking, HLK split, IsPayableJob mode |
| **Invited_Club_API_Lib** | `post_to_invitedclub_configuration` | `get_invitedclub_configuration` | JSON ? REST (Oracle Fusion) | Yes (Supplier, Address, Site, COA) | S3 image, 3-step post, RetryImages, CalculateTax |
| **Greenthal_Corporate_API_Lib** | `post_to_dynamic_configuration` | `wfmob_get_post_to_dynamic_configuration` | File generation (CSV/Excel/ZIP) | No | 20+ job types (Jonas, Canon, Giti, SCSPA, Diray, Cobalt, S2K, Quickbooks, ClubEssentials, Northstar, CommercialPlastics, Accountability, Sharp, Worldwide, DiningEdge, TMK...) |
| **MDS_API_Lib** | `post_to_rapidpay_configuration` | `wfmob_get_rapidpay_configurations` | ZIP+XML ? REST (RapidPay) | No | TIFF image resize, multi-page TIFF, company code grouping, per-schedule post time, credential table per company code length |
| **MOB_API_Lib** | `post_to_mob_configuration` | `wf_get_mob_configurations` | XML ? REST/SOAP | Yes | PDF manipulation, email approval, VMSXChange |
| **Caliber_API_Lib** | `post_to_caliber_configuration` | `wf_get_caliber_configurations` | JSON ? REST | Yes (Vendor Insurance) | Insurance policy validation, vendor insurance delete |
| **Akron_CH_API_Lib** | `post_to_akron_configuration` | `wf_get_akron_configurations` | JSON ? REST | No | Check request distribution, AOC post |
| **Vantaca_API_Lib** | `post_to_vantaca_configuration` | `wf_get_vantaca_configurations` | JSON ? REST | No | � |
| **Workday_API_Lib** | `post_to_workday_configuration` | `wf_get_workday_configurations` | XML ? REST | No | � |
| **Trump_API_Lib** | `post_to_trump_configuration` | `wf_get_trump_configurations` | JSON ? REST | No | Credential table per config |
| **Mincingespice_API_Lib** | `post_to_mincingespice_configuration` | `wf_get_mincingespice_configurations` | JSON ? REST | No | � |
| **Michelman_API_Lib** | `post_to_michelman_configuration` | `wf_get_michelman_configurations` | JSON ? REST | No | � |
| **Signature_API_Lib** | `post_to_signature_configuration` | `wf_get_signature_configurations` | JSON ? REST | No | � |
| **Rent_Manager_Lib** | `post_to_rentmanager_configuration` | `wf_get_rentmanager_configurations` | JSON ? REST | No | � |
| **ReactorNet_API_Lib** | `post_to_reactornet_configuration` | `wf_get_reactornet_configurations` | JSON ? REST | No | � |

### 2.2 What Every Single Library Has in Common

After reading all 15+ libraries, **every single one** has these identical pieces:

```
IDENTICAL ACROSS ALL LIBRARIES:
??????????????????????????????
1.  SqlHelper.cs                    ? 1,855 lines, byte-for-byte identical
2.  Constructor pattern             ? AWS Secrets Manager ? GetConfigurationData ? foreach config ? check schedule ? PostData
3.  GetWorkitemData()               ? SELECT W.* FROM Workitems W JOIN {HeaderTable} WHERE jobid=@JobId AND statusid IN(...)
4.  GetWorkitemDataByItemId()       ? SELECT W.* FROM Workitems W WHERE ItemId IN(dbo.split(@itemids,...))
5.  WORKITEM_ROUTE SP call          ? identical parameters in all libs
6.  GENERALLOG_INSERT SP call       ? identical parameters in all libs
7.  UpdateInProcessFlag             ? UPDATE {HeaderTable} SET PostInProcess=0/1 WHERE UID=@uid
8.  UpdateHistory()                 ? INSERT INTO {HistoryTable} (item_id, post_result, post_date, posted_by, manually_posted, job_id)
9.  UpdateQuestionValue()           ? UPDATE {HeaderTable} SET question=@question WHERE uid=@uid
10. GetAPIResponseTypes()           ? SELECT FROM api_response_configuration WHERE job_id=@job_id
11. GetEdenredApiUrlConfig()        ? SELECT FROM EdenredApiUrlConfig
12. WinService shell                ? Timer ? OnElapsedTime ? new XxxAPI("API", cnnstr)
13. WriteToFile logging             ? CommonMethods.WriteToFile(...)
14. processManually flag            ? callingApplication == "API" ? auto : manual
15. EdenredApiUrlConfig model       ? identical in all libs
```

### 2.3 Configuration Model Comparison - All Libraries

| Field | Media | InvitedClub | Greenthal | MDS | MOB | Others |
|---|---|---|---|---|---|---|
| Id | ? | ? | ? | ? | ? | ? |
| JobId | ? | ? | ? | ? | ? | ? |
| UserId | ? | ? | ? | ? | ? | ? |
| SourceQueueId | ? | ? | ? | ? | ? | ? |
| SuccessQueueId | ? | ? | ? | ? | ? | ? |
| FailedQueueId | ? | ? | ? | ? | ? | ? |
| HeaderTable | ? | ? | ? | ? | ? | ? |
| DetailTable | ? | ? | ? | ? | ? | ? |
| HistoryTable | ? | ? | ? | ? | ? | ? |
| DBConnectionString | ? | ? | ? | ? | ? | ? |
| AllowAutoPost | ? | ? | ? | ? | ? | ? |
| LastPostTime | ? | ? | ? | ? | ? | ? |
| PostServiceURL | ? | ? | ? | ? | ? | ? |
| AuthUsername | � | ? | � | ? | � | varies |
| AuthPassword | � | ? | � | ? | � | varies |
| DownloadFeed | ? | ? | � | � | ? | � |
| ImageParentPath | ? | ? | � | ? | ? | varies |
| IsLegacyJob | � | ? | ? | ? | � | varies |
| **Client-specific** | IsPayableJob, IsAdvantageClient, MediaTableName | EdenredFailQueue, ImageRetryLimit | JobType, FileSeparator, IsReportPerItem, IsFileMonthWise, CustomerCode | ComCodeColumnName, PostIntervalInMinutes, CredTable | EmailApproval, VMSXChange | varies |

---

## 3. What CAN Be Made Generic (Database-Driven)

These things are **identical or structurally the same** across all libraries. They go into the generic core engine and are driven by database configuration � no code change needed for new clients.

### 3.1 Generic Engine Components (Zero Client Code)

| Component | How It Becomes Generic | Database Config |
|---|---|---|
| **Windows Service / Lambda shell** | One host, reads `client_type` from config | `generic_job_configuration.client_type` |
| **Schedule execution check** | Same 30-min window logic everywhere | `generic_execution_schedule` table |
| **Workitem fetching** | Same SQL pattern, table name from config | `header_table`, `source_queue_id` |
| **PostInProcess flag** | Same UPDATE pattern, table from config | `header_table` |
| **WORKITEM_ROUTE** | Same SP, same params everywhere | `success_queue_id`, `failed_queue_id` |
| **GENERALLOG_INSERT** | Same SP, same params everywhere | � |
| **UpdateHistory** | Same INSERT pattern, table from config | `history_table` |
| **UpdateQuestionValue** | Same UPDATE pattern, table from config | `header_table` |
| **api_response_configuration** | Same table, same query everywhere | `job_id` |
| **EdenredApiUrlConfig** | Same table, same query everywhere | � |
| **SqlHelper** | One shared class | � |
| **AWS Secrets Manager** | One shared ConfigurationService | `aws_secret_path` |
| **Email sending (SMTP)** | Same SMTP config pattern | `generic_email_configuration` table |
| **BulkCopy to feed tables** | Same SqlBulkCopy pattern | `feed_table_name` |
| **Feed download history** | Same tracking pattern | `generic_feed_download_history` |
| **S3 image retrieval** | Same S3Utility call | `s3_bucket_name`, `is_legacy_job` |
| **Local image retrieval** | Same File.ReadAllBytes pattern | `image_parent_path` |
| **Auth type selection** | Basic / OAuth / API Key / SOAP | `auth_type`, `auth_username`, `auth_password` |
| **Post output path** | Same file write pattern | `output_file_path` |
| **Manual vs Auto flag** | Same `processManually` pattern | `calling_application` param |
| **Multi-queue routing** | Route to different queues by result type | `queue_routing_rules` table |

### 3.2 Generic Database Tables That Drive All This

```
generic_job_configuration       -> replaces all 15 post_to_xxx_configuration tables
generic_execution_schedule      -> replaces all schedule tables (HH:mm + cron support)
generic_feed_configuration      -> replaces all FeedTypeConfiguration tables (REST + FTP/SFTP + file format)
generic_queue_routing_rules     -> replaces hardcoded queue IDs per client
generic_email_configuration     -> replaces embedded email config per client
generic_field_mapping           -> drives dynamic payload building for simple REST clients (NEW)
generic_auth_configuration      -> replaces hardcoded auth per client (handles MDS multi-cred)
generic_post_history            -> replaces all xxx_posted_records_history tables (multi-step aware)
generic_feed_download_history   -> replaces UpdateMediaFeedDownloadHistory
generic_execution_history       -> tracks execution duration, record counts, trigger type (NEW)
```

---

## 4. What CANNOT Be Made Generic - And Why

This is the honest part. Some things are **fundamentally client-specific** and trying to force them into a generic config table would create a worse mess than what you have today. These go into **client plugin folders**.

### 4.1 Things That Cannot Be Generic

| What | Why It Cannot Be Generic | Client Folder |
|---|---|---|
| **Media SOAP proxy calls** | Advantage/MergeWorld SOAP WSDLs are unique. The `LoadVendors()`, `LoadMediaOrders()` etc. call generated proxy classes. No config table can describe a WSDL method signature. | `Plugins/Media/` |
| **Media 10-day date chunking** | The logic of splitting MediaOrders into 10-day windows and looping is specific to how Advantage API handles large date ranges. | `Plugins/Media/` |
| **Media CSV file format (ADVInvoice)** | The 40+ column CSV layout with specific column names (`VN_CODE`, `INV_NUMBER`, `OFFICE_CODE` etc.) is Advantage-specific. | `Plugins/Media/` |
| **InvitedClub 3-step post** | Invoice ? Attachment ? CalculateTax is Oracle Fusion-specific. The sequence, the JSON shapes, the `InvoiceId` chaining between steps � all Oracle-specific. | `Plugins/InvitedClub/` |
| **InvitedClub image retry** | The `RetryPostImages()` logic with `ImagePostRetryCount` and routing back to success queue is specific to Oracle Fusion's attachment API behavior. | `Plugins/InvitedClub/` |
| **InvitedClub COA validation** | Comparing `InvitedClubCOA` against `InvitedClubsCOAFullFeed` and emailing missing `CodeCombinationId`s is business logic specific to this client. | `Plugins/InvitedClub/` |
| **Greenthal 20+ job types** | Each job type (Jonas, Canon, Giti, SCSPA, Diray, Cobalt, S2K, Quickbooks, ClubEssentials, Northstar, CommercialPlastics, Accountability, Sharp, Worldwide, DiningEdge, TMK) has its own file format, column order, and transformation rules. A config table cannot describe `DataTableToFileForJonas()` vs `DataTableToFileForCanon()`. | `Plugins/Greenthal/` |
| **Greenthal DiningEdge validation** | `ValidateDiningEdgeRecords()`, image copy, ACK file generation, `DiningEdge_Feed` table management � all DiningEdge-specific business rules. | `Plugins/Greenthal/` |
| **Greenthal Worldwide cover page** | `GenerateAndMergeWorldwideCoverPage()` � PDF merge logic specific to Worldwide client. | `Plugins/Greenthal/` |
| **Greenthal Canon Wire API** | `WireRequestMergeAPI()` � Canon-specific wire request merge with its own API and `is_ready_for_wire_request_merge` flag. | `Plugins/Greenthal/` |
| **Greenthal duplicate check** | `CheckDuplicateForGolfJobs()` � Golf job-specific duplicate detection logic. | `Plugins/Greenthal/` |
| **MDS TIFF image processing** | `ResizeImage()`, `ResizeMultiplePageTiff()`, `GetCompressionType()` � TIFF manipulation with GDI+ is specific to MDS/RapidPay requirements. | `Plugins/MDS/` |
| **MDS company code grouping** | Grouping workitems by `ComCode` length, then by `DocumentType`, then by `DocumentCategoryName` before posting � RapidPay API-specific batching requirement. | `Plugins/MDS/` |
| **MDS credential table per company code** | `post_to_rapidpay_configuration_creds` � different API credentials per company code length. No other client has this. | `Plugins/MDS/` |
| **MDS ZIP+XML packaging** | Building ZIP archives containing XML files for RapidPay � format specific to RapidPay API contract. | `Plugins/MDS/` |
| **MOB PDF manipulation** | `PDFManipulation.cs` � PDF splitting, merging, stamping specific to MOB workflow. | `Plugins/MOB/` |
| **MOB email approval** | `EmailApproval.cs` � email-based approval workflow with token links. | `Plugins/MOB/` |
| **Caliber vendor insurance** | Insurance policy validation, expiry checks, `WFMOB_Vendor_Insurance_Policy_Validations` � Caliber-specific compliance logic. | `Plugins/Caliber/` |
| **Akron AOC/Check distribution** | `AOCPost`, `CheckRequestDistributionPost` � Akron Children's Hospital-specific AP models. | `Plugins/Akron/` |

### 4.2 The Rule for Deciding Generic vs Plugin

```
Ask: "Can this be described by adding a row to a database table?"

YES ? Generic core engine
  Examples:
  - "Post to this URL with Basic Auth" ? auth_type = 'BASIC', auth_url, auth_user, auth_pass
  - "Route to queue 500 on success" ? success_queue_id = 500
  - "Download feed from this REST endpoint every day at 8am" ? feed_url, feed_schedule

NO ? Client plugin folder
  Examples:
  - "Resize this TIFF image to fit within 18-inch ratio" ? code, not config
  - "Group invoices by company code length before posting" ? code, not config
  - "Generate a Jonas-format file with these 12 specific columns" ? code, not config
  - "Call SOAP method LoadMediaOrders with 10-day date chunks" ? code, not config
```

---

## 5. Complete New Solution Architecture

### 5.1 High-Level Architecture

```
+-----------------------------------------------------------------------------+
�                        IPS AutoPost Platform                                 �
�                         (.NET Core 10 on AWS)                                �
+-----------------------------------------------------------------------------+

TRIGGER LAYER
+------------------+  +------------------+  +------------------+
� EventBridge      �  �  API Gateway     �  �  SQS Queue       �
� (Scheduled jobs) �  �  (Manual trigger �  �  (Retry queue)   �
� cron per job     �  �   from Workflow) �  �                  �
+------------------+  +------------------+  +------------------+
         +---------------------+                      �
                             ?                         �
COMPUTE LAYER                                          �
+-----------------------------------------------------+----------------------+
�                    Lambda / ECS Fargate              �                      �
�                                                      �                      �
�  +----------------------------------------------+   �                      �
�  �           IPS.AutoPost.Core (Engine)          �?--+                      �
�  �                                              �                           �
�  �  1. Load config from DB (generic_job_config) �                           �
�  �  2. Check schedule                           �                           �
�  �  3. Fetch workitems (generic SQL)            �                           �
�  �  4. Resolve plugin by client_type            �                           �
�  �  5. Call plugin.ExecutePost()                �                           �
�  �  6. Route workitem (WORKITEM_ROUTE)          �                           �
�  �  7. Write history (generic_post_history)     �                           �
�  �  8. Call plugin.ExecuteFeedDownload()        �                           �
�  +----------------------------------------------+                           �
�                 � resolves plugin by client_type                             �
�                 ?                                                            �
�  +----------------------------------------------------------------------+   �
�  �                    Plugin Registry                                    �   �
�  �                                                                       �   �
�  �  "MEDIA"        ? MediaPlugin        (Plugins/Media/)                �   �
�  �  "INVITEDCLUB"  ? InvitedClubPlugin  (Plugins/InvitedClub/)          �   �
�  �  "GREENTHAL"    ? GreenthalPlugin    (Plugins/Greenthal/)            �   �
�  �  "MDS"          ? MDSPlugin          (Plugins/MDS/)                  �   �
�  �  "MOB"          ? MOBPlugin          (Plugins/MOB/)                  �   �
�  �  "CALIBER"      ? CaliberPlugin      (Plugins/Caliber/)              �   �
�  �  "NEWCLIENT"    ? NewClientPlugin    (Plugins/NewClient/)  ? add new �   �
�  +----------------------------------------------------------------------+   �
+-----------------------------------------------------------------------------+

DATA LAYER
+------------------+  +------------------+  +------------------+
�  RDS SQL Server  �  �   S3 Buckets     �  � Secrets Manager  �
�  (Workflow DB)   �  �  - Images        �  �  - DB creds      �
�  - Generic tables�  �  - Feed archives �  �  - API keys      �
�  - Client tables �  �  - Output files  �  �  - SMTP creds    �
+------------------+  +------------------+  +------------------+

OBSERVABILITY LAYER
+--------------------------------------------------------------------------+
�  CloudWatch Logs | CloudWatch Metrics | SNS Alerts | X-Ray Tracing       �
+--------------------------------------------------------------------------+
```

### 5.2 How a New Client Gets Added

```
Today (old way):
  1. Create new Visual Studio project (2-4 hours)
  2. Copy-paste SqlHelper, GetDataBAL, WinService (1 hour)
  3. Write client-specific logic (1-2 days)
  4. Create new config table in DB (1 hour)
  5. Create new Windows Service installer (1 hour)
  6. Deploy to new EC2 instance (2 hours)
  Total: 2-3 days + new EC2 cost

Tomorrow (new way):
  1. INSERT rows into generic_job_configuration (15 min)
  2. Create Plugins/NewClient/ folder with one class (2-4 hours for logic)
  3. Register plugin in PluginRegistry (5 min)
  4. Deploy (already running - zero new infrastructure)
  Total: 2-4 hours + zero new infrastructure cost
```

---

## 6. Project Structure - .NET Core 10

```
IPS.AutoPost.Platform/                          ? Solution root
�
+-- src/
�   �
�   +-- IPS.AutoPost.Core/                      ? Generic engine (never changes for new clients)
�   �   +-- IPS.AutoPost.Core.csproj
�   �   +-- Engine/
�   �   �   +-- AutoPostOrchestrator.cs         ? Main loop: load config ? check schedule ? run plugin
�   �   �   +-- SchedulerService.cs             ? IsExecuteFileCreation (shared logic)
�   �   �   +-- PluginRegistry.cs               ? Maps client_type string ? IClientPlugin
�   �   +-- Interfaces/
�   �   �   +-- IClientPlugin.cs                ? Contract every client plugin must implement
�   �   �   +-- IPostStrategy.cs                ? Post logic contract
�   �   �   +-- IFeedStrategy.cs                ? Feed download contract
�   �   �   +-- IImageProvider.cs               ? Image retrieval contract
�   �   +-- Services/
�   �   �   +-- WorkitemService.cs              ? GetWorkitemData, GetWorkitemDataByItemId
�   �   �   +-- RoutingService.cs               ? WORKITEM_ROUTE, UpdateInProcessFlag
�   �   �   +-- AuditService.cs                 ? GENERALLOG_INSERT, UpdateHistory
�   �   �   +-- BulkCopyService.cs              ? SqlBulkCopy wrapper
�   �   �   +-- EmailService.cs                 ? SMTP email (shared)
�   �   �   +-- S3ImageService.cs               ? S3Utility wrapper
�   �   �   +-- ConfigurationService.cs         ? Load generic_job_configuration from DB
�   �   +-- DataAccess/
�   �   �   +-- SqlHelper.cs                    ? ONE shared SqlHelper (replaces all 15 copies)
�   �   +-- Models/
�   �   �   +-- GenericJobConfig.cs             ? Unified config model (maps from DB)
�   �   �   +-- WorkitemData.cs
�   �   �   +-- PostResult.cs
�   �   �   +-- FeedResult.cs
�   �   �   +-- ScheduleConfig.cs
�   �   �   +-- EdenredApiUrlConfig.cs
�   �   +-- Utility/
�   �       +-- Parser.cs                       ? ConvertDataTable<T>, ToDataTable<T>
�   �       +-- CommonMethods.cs                ? WriteToFile, CheckForEmptyDataSet
�   �
�   +-- IPS.AutoPost.Plugins/                   ? All client-specific logic
�   �   +-- IPS.AutoPost.Plugins.csproj
�   �   �
�   �   +-- Media/                              ? Media API client plugin
�   �   �   +-- MediaPlugin.cs                  ? Implements IClientPlugin
�   �   �   +-- MediaPostStrategy.cs            ? CSV generation + SOAP post
�   �   �   +-- MediaFeedStrategy.cs            ? SOAP feed download (Vendor, MediaOrder, GL, PO, Jobs)
�   �   �   +-- SoapProviders/
�   �   �   �   +-- AdvantageApiClient.cs       ? Wraps Advantage SOAP proxy
�   �   �   �   +-- MergeWorldApiClient.cs      ? Wraps MergeWorld SOAP proxy
�   �   �   +-- Models/
�   �   �       +-- ADVInvoice.cs               ? Media-specific CSV model
�   �   �
�   �   +-- InvitedClub/                        ? InvitedClub API client plugin
�   �   �   +-- InvitedClubPlugin.cs            ? Implements IClientPlugin
�   �   �   +-- InvitedClubPostStrategy.cs      ? 3-step REST post (Invoice?Attachment?Tax)
�   �   �   +-- InvitedClubFeedStrategy.cs      ? REST feed (Supplier, Address, Site, COA)
�   �   �   +-- InvitedClubRetryService.cs      ? RetryPostImages logic
�   �   �   +-- Models/
�   �   �       +-- InvoiceRequest.cs
�   �   �       +-- InvoiceResponse.cs
�   �   �       +-- SupplierResponse.cs
�   �   �       +-- COAResponse.cs
�   �   �
�   �   +-- Greenthal/                          ? Greenthal Corporate client plugin
�   �   �   +-- GreenthalPlugin.cs              ? Implements IClientPlugin
�   �   �   +-- GreenthalPostStrategy.cs        ? File generation orchestrator
�   �   �   +-- JobTypes/                       ? One class per job type
�   �   �   �   +-- JonasFileGenerator.cs
�   �   �   �   +-- CanonFileGenerator.cs
�   �   �   �   +-- GitiFileGenerator.cs
�   �   �   �   +-- SCSPAFileGenerator.cs
�   �   �   �   +-- DirayFileGenerator.cs
�   �   �   �   +-- CobaltFileGenerator.cs
�   �   �   �   +-- S2KFileGenerator.cs
�   �   �   �   +-- QuickbooksFileGenerator.cs
�   �   �   �   +-- ClubEssentialsFileGenerator.cs
�   �   �   �   +-- NorthstarFileGenerator.cs
�   �   �   �   +-- CommercialPlasticsFileGenerator.cs
�   �   �   �   +-- AccountabilityFileGenerator.cs
�   �   �   �   +-- SharpFileGenerator.cs
�   �   �   �   +-- WorldwideCoverPageGenerator.cs
�   �   �   �   +-- DiningEdgeProcessor.cs
�   �   �   �   +-- TMKFileGenerator.cs
�   �   �   +-- Models/
�   �   �       +-- GreenthalModels.cs
�   �   �
�   �   +-- MDS/                                ? MDS/RapidPay client plugin
�   �   �   +-- MDSPlugin.cs                    ? Implements IClientPlugin
�   �   �   +-- MDSPostStrategy.cs              ? ZIP+XML post with company code grouping
�   �   �   +-- TiffImageProcessor.cs           ? TIFF resize/multi-page logic
�   �   �   +-- Models/
�   �   �       +-- MDSModels.cs
�   �   �
�   �   +-- MOB/                                ? MOB API client plugin
�   �   �   +-- MOBPlugin.cs
�   �   �   +-- MOBPostStrategy.cs
�   �   �   +-- PdfManipulationService.cs
�   �   �   +-- EmailApprovalService.cs
�   �   �
�   �   +-- Caliber/                            ? Caliber client plugin
�   �   �   +-- CaliberPlugin.cs
�   �   �   +-- CaliberPostStrategy.cs
�   �   �   +-- VendorInsuranceValidator.cs
�   �   �
�   �   +-- Akron/                              ? Akron client plugin
�   �   �   +-- AkronPlugin.cs
�   �   �   +-- AkronPostStrategy.cs
�   �   �
�   �   +-- [NewClient]/                        ? Future client - just add folder
�   �       +-- NewClientPlugin.cs
�   �       +-- NewClientPostStrategy.cs
�   �
�   +-- IPS.AutoPost.Host.Lambda/               ? AWS Lambda host
�   �   +-- IPS.AutoPost.Host.Lambda.csproj
�   �   +-- Function.cs                         ? Lambda handler
�   �   +-- aws-lambda-tools-defaults.json
�   �
�   +-- IPS.AutoPost.Host.Worker/               ? .NET Worker Service (ECS Fargate / local)
�   �   +-- IPS.AutoPost.Host.Worker.csproj
�   �   +-- Worker.cs                           ? IHostedService implementation
�   �   +-- appsettings.json
�   �
�   +-- IPS.AutoPost.Api/                       ? ASP.NET Core Web API (manual trigger)
�       +-- IPS.AutoPost.Api.csproj
�       +-- Controllers/
�       �   +-- PostController.cs               ? POST /api/post/{jobId}/{itemIds}
�       +-- appsettings.json
�
+-- tests/
�   +-- IPS.AutoPost.Core.Tests/
�   +-- IPS.AutoPost.Plugins.Tests/
�
+-- infra/
�   +-- cloudformation/
�   �   +-- platform-stack.yaml
�   +-- scripts/
�       +-- deploy.ps1
�
+-- IPS.AutoPost.Platform.sln
```

---

## 7. Database Design - Generic Configuration Tables

### 7.1 generic_job_configuration
Replaces all 15 `post_to_xxx_configuration` tables.

```sql
CREATE TABLE generic_job_configuration (
    -- Identity
    id                          INT IDENTITY(1,1) PRIMARY KEY,
    client_type                 VARCHAR(50)  NOT NULL,  -- 'MEDIA','INVITEDCLUB','GREENTHAL','MDS','MOB','CALIBER','AKRON'...
    job_id                      INT          NOT NULL,
    job_name                    VARCHAR(100) NOT NULL,
    default_user_id             INT          NOT NULL DEFAULT 100,
    is_active                   BIT          NOT NULL DEFAULT 1,

    -- Queue IDs (generic - all clients use these)
    source_queue_id             VARCHAR(500) NOT NULL,  -- comma-separated for multi-queue
    success_queue_id            INT          NULL,
    primary_fail_queue_id       INT          NULL,
    secondary_fail_queue_id     INT          NULL,      -- e.g. image fail vs invoice fail
    question_queue_id           INT          NULL,
    terminated_queue_id         INT          NULL,
    terminated_repost_queue_id  INT          NULL,
    suspected_duplicate_queue_id INT         NULL,

    -- Table References (generic - all clients use these)
    header_table                VARCHAR(200) NULL,
    detail_table                VARCHAR(200) NULL,
    detail_uid_column           VARCHAR(200) NULL,
    history_table               VARCHAR(200) NULL,
    db_connection_string        NVARCHAR(500) NULL,

    -- Post Service (generic)
    post_service_url            VARCHAR(500) NULL,
    auth_type                   VARCHAR(20)  NULL,      -- 'BASIC','OAUTH','APIKEY','SOAP','NONE'
    auth_username               VARCHAR(200) NULL,
    auth_password               VARCHAR(200) NULL,
    auth_token_url              VARCHAR(500) NULL,      -- for OAuth

    -- Download Service (generic)
    download_service_url        VARCHAR(500) NULL,
    download_auth_type          VARCHAR(20)  NULL,
    download_auth_username      VARCHAR(200) NULL,
    download_auth_password      VARCHAR(200) NULL,

    -- Scheduling (generic)
    last_post_time              DATETIME     NULL,
    last_download_time          DATETIME     NULL,
    allow_auto_post             BIT          NOT NULL DEFAULT 0,
    download_feed               BIT          NOT NULL DEFAULT 0,
    allow_manual_refresh        BIT          NOT NULL DEFAULT 0,
    manual_refresh              BIT          NOT NULL DEFAULT 0,

    -- Paths (generic)
    output_file_path            NVARCHAR(500) NULL,     -- where to write output files
    output_file_path_newui      NVARCHAR(500) NULL,
    feed_download_path          NVARCHAR(500) NULL,
    image_parent_path           NVARCHAR(500) NULL,
    image_parent_path_newui     NVARCHAR(500) NULL,
    is_legacy_job               BIT          NOT NULL DEFAULT 0,

    -- Image / S3 (generic)
    use_s3_for_images           BIT          NOT NULL DEFAULT 0,

    -- Approval Gates (generic flags - used by some clients)
    ap_posted_required          BIT          NOT NULL DEFAULT 0,
    validation_required         BIT          NOT NULL DEFAULT 0,
    ap_approval_required        BIT          NOT NULL DEFAULT 0,
    mgr_approval_required       BIT          NOT NULL DEFAULT 0,
    board_approval_required     BIT          NOT NULL DEFAULT 0,
    check_ready_to_pay          BIT          NOT NULL DEFAULT 0,

    -- Misc (generic)
    route_comment               VARCHAR(500) NULL,
    origination_id              VARCHAR(50)  NULL,
    is_reprocess_queue          BIT          NOT NULL DEFAULT 0,

    -- Client-specific JSON blob (for anything that doesn't fit above)
    -- This avoids adding columns for every client quirk
    client_config_json          NVARCHAR(MAX) NULL,
    -- Examples of what goes in client_config_json:
    -- Media:       {"IsPayableJob":true,"IsAdvantageClient":true,"MediaTableName":"WFMediaOrders","LoadMediaOrdersMonths":6}
    -- Greenthal:   {"JobType":"Jonas","FileSeparator":"|","IsReportPerItem":false,"IsFileMonthWise":true}
    -- MDS:         {"ComCodeColumnName":"ComCode","PostIntervalInMinutes":30}
    -- InvitedClub: {"ImagePostRetryLimit":3,"EdenredFailQueueId":500,"InvitedFailQueueId":501}

    created_date                DATETIME     NOT NULL DEFAULT GETDATE(),
    modified_date               DATETIME     NULL
);
```

### 7.2 generic_execution_schedule
Replaces all schedule tables. Supports both the existing `HH:mm` format (backward compatible with all 15 current libraries) and standard cron expressions (for EventBridge Scheduler on AWS).

```sql
CREATE TABLE generic_execution_schedule (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    job_config_id       INT NOT NULL REFERENCES generic_job_configuration(id),
    schedule_type       VARCHAR(20) NOT NULL DEFAULT 'POST',  -- 'POST', 'DOWNLOAD'

    -- HH:mm format (backward compatible with existing 15 libraries)
    -- e.g. '08:00', '14:30'
    -- Used by SchedulerService.ShouldExecute() with 30-min window logic
    execution_time      VARCHAR(10) NULL,

    -- Cron expression (for EventBridge Scheduler on AWS)
    -- e.g. 'cron(0 8 * * ? *)', 'rate(30 minutes)'
    -- Takes precedence over execution_time when both are set
    cron_expression     VARCHAR(100) NULL,

    last_execution_time DATETIME NULL,
    is_active           BIT NOT NULL DEFAULT 1,

    CONSTRAINT CHK_schedule_has_time CHECK (
        execution_time IS NOT NULL OR cron_expression IS NOT NULL
    )
);
```

**When to use which format:**

| Format | Use When | Example |
|---|---|---|
| `execution_time` (HH:mm) | Migrating from existing Windows Services, or when the 30-min window logic is needed | `08:00` |
| `cron_expression` | New clients deployed on AWS EventBridge, or when precise scheduling is needed | `cron(0 8 * * ? *)` |

**Example rows:**
```sql
-- Media AutoPost: run at 08:00 and 14:00 daily (HH:mm, backward compat)
INSERT INTO generic_execution_schedule (job_config_id, schedule_type, execution_time)
VALUES (1, 'POST', '08:00'), (1, 'POST', '14:00');

-- InvitedClub Feed Download: every day at 07:00 (cron for EventBridge)
INSERT INTO generic_execution_schedule (job_config_id, schedule_type, cron_expression)
VALUES (2, 'DOWNLOAD', 'cron(0 7 * * ? *)');

-- Vantaca AutoPost: every 30 minutes (rate expression)
INSERT INTO generic_execution_schedule (job_config_id, schedule_type, cron_expression)
VALUES (3, 'POST', 'rate(30 minutes)');
```
### 7.3 generic_feed_configuration
Replaces per-client feed type tables. Supports REST API, FTP, SFTP, S3, and local file system as feed sources — covering all existing clients including those that use FTP delivery.

```sql
CREATE TABLE generic_feed_configuration (
    id                  INT IDENTITY(1,1) PRIMARY KEY,
    job_config_id       INT NOT NULL REFERENCES generic_job_configuration(id),
    feed_name           VARCHAR(100) NOT NULL,   -- 'Vendor','Supplier','COA','MediaOrder','GL','PO','Jobs'

    -- Source type determines which connection fields are used below
    feed_source_type    VARCHAR(20) NOT NULL DEFAULT 'REST',
    -- 'REST'  -> uses feed_url + auth from generic_auth_configuration
    -- 'FTP'   -> uses ftp_host, ftp_path, ftp_port + auth from generic_auth_configuration
    -- 'SFTP'  -> uses ftp_host, ftp_path, ftp_port + auth from generic_auth_configuration
    -- 'S3'    -> uses s3_bucket, s3_key_prefix + IAM role
    -- 'FILE'  -> uses local_file_path (for legacy file-drop clients)

    -- REST source fields
    feed_url            VARCHAR(500) NULL,        -- REST endpoint or SOAP method name

    -- FTP/SFTP source fields
    ftp_host            VARCHAR(200) NULL,        -- e.g. ftp.sevita.com
    ftp_port            INT NULL DEFAULT 21,      -- 21 for FTP, 22 for SFTP
    ftp_path            VARCHAR(500) NULL,        -- e.g. /feeds/
    ftp_file_pattern    VARCHAR(200) NULL,        -- e.g. vendor_*.csv (supports wildcards)

    -- S3 source fields
    s3_bucket           VARCHAR(200) NULL,
    s3_key_prefix       VARCHAR(500) NULL,

    -- Local file source fields
    local_file_path     VARCHAR(500) NULL,

    -- File format (applies to FTP/SFTP/S3/FILE sources)
    file_format         VARCHAR(20) NULL,         -- 'CSV','TXT','XLSX','JSON','XML'
    has_header          BIT NOT NULL DEFAULT 1,
    delimiter           VARCHAR(5) NULL DEFAULT ',',

    -- Target DB table
    feed_table_name     VARCHAR(200) NULL,        -- target DB table for bulk insert

    -- Refresh strategy
    refresh_strategy    VARCHAR(20) NOT NULL DEFAULT 'TRUNCATE',
    -- 'TRUNCATE'      -> truncate table then bulk insert (COA, Vendor full refresh)
    -- 'DELETE_BY_KEY' -> delete rows by key_column then insert (Supplier incremental)
    -- 'INCREMENTAL'   -> insert/update only changed rows (based on last_download_time)
    key_column          VARCHAR(100) NULL,        -- for DELETE_BY_KEY strategy (e.g. 'SupplierId')

    last_download_time  DATETIME NULL,
    is_active           BIT NOT NULL DEFAULT 1,

    -- Extra params as JSON (date range, pagination size, chunk size, etc.)
    -- Examples:
    -- Media MediaOrders: {"ChunkDays":10,"StartMonthsBack":6,"MediaTypes":"Internet,Magazine,TV"}
    -- InvitedClub Supplier: {"PageSize":500,"IncrementalLookbackDays":2}
    -- InvitedClub COA: {"QueryFilter":"_CHART_OF_ACCOUNTS_ID=5237;EnabledFlag='Y'"}
    feed_config_json    NVARCHAR(MAX) NULL
);
```

**Which clients use which source type:**

| Client | Feed Name | Source Type | Notes |
|---|---|---|---|
| Media | Vendor | REST | Advantage/MergeWorld SOAP |
| Media | MediaOrder | REST | SOAP, 10-day chunks via feed_config_json |
| Media | GL, PO, Jobs | REST | SOAP, payable job mode only |
| InvitedClub | Supplier | REST | Paginated REST, incremental refresh |
| InvitedClub | SupplierAddress | REST | Per-supplier REST call |
| InvitedClub | SupplierSite | REST | Per-supplier REST call |
| InvitedClub | COA | REST | Full refresh, query filter in feed_config_json |
| Caliber | VendorInsurance | REST | REST with validation |
| Sevita (future) | Feed files | FTP | ftp.sevita.com /feeds/ |
| Any file-drop client | Any | FILE | Local path, CSV/XLSX |

### 7.4 generic_auth_configuration
Stores credentials per job (replaces embedded auth in config tables).

```sql
CREATE TABLE generic_auth_configuration (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    job_config_id   INT NOT NULL REFERENCES generic_job_configuration(id),
    auth_purpose    VARCHAR(20) NOT NULL,   -- 'POST', 'DOWNLOAD', 'CREDS_BY_COMCODE'
    auth_key        VARCHAR(100) NULL,      -- e.g. company_code_length for MDS
    auth_type       VARCHAR(20) NOT NULL,   -- 'BASIC','OAUTH','APIKEY','SOAP'
    username        VARCHAR(200) NULL,
    password        VARCHAR(200) NULL,
    api_key         VARCHAR(500) NULL,
    token_url       VARCHAR(500) NULL,
    secret_arn      VARCHAR(500) NULL,      -- AWS Secrets Manager ARN
    extra_json      NVARCHAR(MAX) NULL
);
```

### 7.5 generic_queue_routing_rules
Makes queue routing configurable instead of hardcoded.

```sql
CREATE TABLE generic_queue_routing_rules (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    job_config_id   INT NOT NULL REFERENCES generic_job_configuration(id),
    result_type     VARCHAR(50) NOT NULL,   -- 'SUCCESS','FAIL_POST','FAIL_IMAGE','DUPLICATE','QUESTION','TERMINATED'
    queue_id        INT NOT NULL,
    is_active       BIT NOT NULL DEFAULT 1
);
```

### 7.6 generic_post_history
Replaces all `xxx_posted_records_history` tables.

```sql
CREATE TABLE generic_post_history (
    id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    client_type         VARCHAR(50) NOT NULL,
    job_id              INT NOT NULL,
    item_id             BIGINT NOT NULL,
    step_name           VARCHAR(100) NULL,   -- 'InvoicePost','AttachmentPost','CalculateTax','FileGeneration'
    post_request        NVARCHAR(MAX) NULL,
    post_response       NVARCHAR(MAX) NULL,
    post_date           DATETIME NOT NULL DEFAULT GETDATE(),
    posted_by           INT NOT NULL,
    manually_posted     BIT NOT NULL DEFAULT 0,
    output_file_path    NVARCHAR(500) NULL,
    INDEX IX_generic_post_history_item (item_id, job_id)
);
```

### 7.7 generic_email_configuration

```sql
CREATE TABLE generic_email_configuration (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    job_config_id   INT NOT NULL REFERENCES generic_job_configuration(id),
    email_type      VARCHAR(50) NOT NULL,   -- 'POST_FAILURE','IMAGE_FAILURE','MISSING_COA','FEED_FAILURE'
    email_to        NVARCHAR(1000) NULL,
    email_cc        NVARCHAR(1000) NULL,
    email_bcc       NVARCHAR(1000) NULL,
    email_subject   NVARCHAR(500) NULL,
    email_template  NVARCHAR(500) NULL,
    smtp_server     VARCHAR(200) NULL,
    smtp_port       INT NULL DEFAULT 587,
    smtp_username   VARCHAR(200) NULL,
    smtp_password   VARCHAR(200) NULL,
    smtp_use_ssl    BIT NOT NULL DEFAULT 1,
    is_active       BIT NOT NULL DEFAULT 1
);
```

### 7.8 generic_feed_download_history

```sql
CREATE TABLE generic_feed_download_history (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    job_config_id   INT NOT NULL,
    feed_name       VARCHAR(100) NOT NULL,
    is_manual       BIT NOT NULL DEFAULT 0,
    status          VARCHAR(20) NOT NULL,   -- 'Start','End','Error'
    record_count    INT NULL,
    error_message   NVARCHAR(MAX) NULL,
    download_date   DATETIME NOT NULL DEFAULT GETDATE()
);
```


### 7.9 generic_field_mapping
Drives dynamic payload building for simple REST clients. This table allows clients like Vantaca, Akron, Michelman, Mincingespice, Signature, Rent Manager, ReactorNet, Workday, and Trump to be handled **entirely from configuration with zero plugin code** — the `GenericRestPlugin` reads these rows and builds the JSON/XML payload dynamically.

**When to use this table:**
- Client posts a simple JSON or XML body to a REST endpoint
- The only difference between clients is which DB columns map to which API fields
- No special processing (no TIFF resize, no ZIP packaging, no multi-step chaining)

**When NOT to use this table (use a plugin instead):**
- Payload requires multi-step chaining (InvitedClub: InvoiceId from step 1 used in step 2 URL)
- Payload format is a file (CSV, Excel, ZIP) — Greenthal, Media, MDS
- Payload requires grouping/batching logic before building — MDS company code grouping
- SOAP proxy calls — Media Advantage/MergeWorld

```sql
CREATE TABLE generic_field_mapping (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    job_config_id   INT NOT NULL REFERENCES generic_job_configuration(id),

    -- What this mapping applies to
    mapping_type    VARCHAR(30) NOT NULL,
    -- 'INVOICE_HEADER'  -> maps header table columns to API request body fields
    -- 'INVOICE_LINE'    -> maps detail table columns to API line item fields
    -- 'FEED_RESPONSE'   -> maps API response fields to feed table columns
    -- 'FEED_REQUEST'    -> maps config values to feed API query parameters

    -- Source: where the value comes from
    source_field    VARCHAR(200) NOT NULL,
    -- Can be:
    --   a DB column name:  'InvoiceNumber', 'NetAmountDue'
    --   a config value:    'CONST:USD'  (constant value)
    --   a nested path:     'Header.VendorCode'

    -- Target: where the value goes in the API payload
    target_field    VARCHAR(200) NOT NULL,
    -- JSON path in the request body: 'invoiceNumber', 'lines[].amount'
    -- XML element path: 'Invoice/Header/InvoiceNo'

    -- Data type for conversion
    data_type       VARCHAR(50) NOT NULL DEFAULT 'VARCHAR',
    -- 'VARCHAR', 'INT', 'DECIMAL', 'DATE', 'DATETIME', 'BIT', 'BASE64'

    -- Transform rule as JSON — optional
    transform_rule  NVARCHAR(500) NULL,
    -- Examples:
    -- Date format:     {"format":"yyyy-MM-dd"}
    -- String trim:     {"trim":true,"uppercase":true}
    -- Number format:   {"decimals":2}
    -- Conditional:     {"ifEmpty":"0.00"}
    -- Lookup:          {"lookup":"generic_code_mapping","key":"MediaCode"}

    -- Validation
    is_required     BIT NOT NULL DEFAULT 0,
    -- If true and source value is null/empty, post is blocked and item routed to question queue

    -- Ordering (controls JSON field order in payload)
    sort_order      INT NOT NULL DEFAULT 0,

    is_active       BIT NOT NULL DEFAULT 1
);
```

**Example: Vantaca — simple JSON REST post (zero plugin code needed)**
```sql
-- Vantaca posts: {"invoiceNumber":"INV001","invoiceDate":"2026-01-15","amount":1500.00,"vendorCode":"V001"}
INSERT INTO generic_field_mapping (job_config_id, mapping_type, source_field, target_field, data_type, is_required, sort_order)
VALUES
(10, 'INVOICE_HEADER', 'InvoiceNumber',  'invoiceNumber',  'VARCHAR',  1, 1),
(10, 'INVOICE_HEADER', 'InvoiceDate',    'invoiceDate',    'DATE',     1, 2),
(10, 'INVOICE_HEADER', 'NetAmountDue',   'amount',         'DECIMAL',  1, 3),
(10, 'INVOICE_HEADER', 'VendorCode',     'vendorCode',     'VARCHAR',  1, 4);
```

**Example: Akron — JSON with transform rule**
```sql
-- Akron needs InvoiceDate formatted as MM/dd/yyyy and amount as string with 2 decimals
INSERT INTO generic_field_mapping (job_config_id, mapping_type, source_field, target_field, data_type, transform_rule, is_required, sort_order)
VALUES
(11, 'INVOICE_HEADER', 'InvoiceNumber', 'Invoice',       'VARCHAR', NULL,                    1, 1),
(11, 'INVOICE_HEADER', 'InvoiceDate',   'InvoiceDate',   'DATE',    '{"format":"MM/dd/yyyy"}',1, 2),
(11, 'INVOICE_HEADER', 'NetAmountDue',  'InvoiceAmount', 'DECIMAL', '{"decimals":2}',         1, 3),
(11, 'INVOICE_HEADER', 'CONST:USD',     'Currency',      'VARCHAR', NULL,                    0, 4);
-- CONST:USD means always send "USD" regardless of DB value
```

**Example: Feed response mapping (InvitedClub Supplier — maps REST response to DB table)**
```sql
-- Maps API response JSON fields to InvitedClubSupplier table columns
INSERT INTO generic_field_mapping (job_config_id, mapping_type, source_field, target_field, data_type, sort_order)
VALUES
(2, 'FEED_RESPONSE', 'SupplierId',     'SupplierId',     'VARCHAR', 1),
(2, 'FEED_RESPONSE', 'Supplier',       'Supplier',       'VARCHAR', 2),
(2, 'FEED_RESPONSE', 'SupplierNumber', 'SupplierNumber', 'VARCHAR', 3),
(2, 'FEED_RESPONSE', 'Status',         'Status',         'VARCHAR', 4),
(2, 'FEED_RESPONSE', 'LastUpdateDate', 'LastUpdateDate', 'DATE',    5);
```

**How GenericRestPlugin uses this table (see Section 8.5):**
```
1. Load field mappings for job_config_id WHERE mapping_type = 'INVOICE_HEADER'
2. For each workitem, read header row from HeaderTable
3. For each mapping row: read source_field from header row, apply transform_rule, set target_field in JSON payload
4. POST JSON to PostServiceUrl with auth from generic_auth_configuration
5. Core engine handles routing, history, logging
```

---

### 7.10 generic_execution_history
Tracks every execution with duration, record counts, trigger type, and status. Replaces the basic `generic_feed_download_history` for full execution visibility. Used by CloudWatch dashboards and operational monitoring.

```sql
CREATE TABLE generic_execution_history (
    id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    job_config_id       INT NOT NULL REFERENCES generic_job_configuration(id),
    client_type         VARCHAR(50) NOT NULL,   -- 'MEDIA','INVITEDCLUB','GREENTHAL' etc.
    job_id              INT NOT NULL,

    -- What ran
    execution_type      VARCHAR(30) NOT NULL,   -- 'POST', 'FEED_DOWNLOAD', 'RETRY_IMAGES'
    trigger_type        VARCHAR(20) NOT NULL,   -- 'SCHEDULED', 'MANUAL', 'RETRY_QUEUE'

    -- Outcome
    status              VARCHAR(20) NOT NULL,   -- 'SUCCESS', 'FAILED', 'PARTIAL_SUCCESS', 'NO_RECORDS'
    records_processed   INT NULL DEFAULT 0,
    records_succeeded   INT NULL DEFAULT 0,
    records_failed      INT NULL DEFAULT 0,
    error_details       NVARCHAR(MAX) NULL,

    -- Timing
    start_time          DATETIME NOT NULL DEFAULT GETDATE(),
    end_time            DATETIME NULL,
    duration_seconds    AS DATEDIFF(SECOND, start_time, end_time),  -- computed column

    -- Who triggered (for manual runs)
    triggered_by_user   INT NULL,

    INDEX IX_exec_history_job (job_config_id, start_time DESC),
    INDEX IX_exec_history_status (status, start_time DESC)
);
```

**How it is written:**
```csharp
// In AutoPostOrchestrator.cs — the core engine writes this automatically
// Plugin code never needs to write to this table directly

var history = new GenericExecutionHistory {
    JobConfigId    = config.Id,
    ClientType     = config.ClientType,
    JobId          = config.JobId,
    ExecutionType  = "POST",
    TriggerType    = isManual ? "MANUAL" : "SCHEDULED",
    Status         = "SUCCESS",
    StartTime      = startTime
};
// ... after plugin.ExecutePostAsync() completes:
history.EndTime          = DateTime.Now;
history.RecordsProcessed = results.Count;
history.RecordsSucceeded = results.Count(r => r.IsSuccess);
history.RecordsFailed    = results.Count(r => !r.IsSuccess);
await _historyRepo.SaveAsync(history);
```

**Example queries:**
```sql
-- Last 10 executions for a job
SELECT TOP 10 execution_type, trigger_type, status,
       records_processed, records_failed, duration_seconds, start_time
FROM generic_execution_history
WHERE job_config_id = 1
ORDER BY start_time DESC;

-- Failed executions in last 24 hours across all jobs
SELECT client_type, job_id, execution_type, error_details, start_time
FROM generic_execution_history
WHERE status IN ('FAILED','PARTIAL_SUCCESS')
  AND start_time >= DATEADD(HOUR, -24, GETDATE())
ORDER BY start_time DESC;

-- Average duration per client type
SELECT client_type, execution_type,
       AVG(duration_seconds) AS avg_seconds,
       MAX(duration_seconds) AS max_seconds,
       COUNT(*) AS total_runs
FROM generic_execution_history
WHERE start_time >= DATEADD(DAY, -30, GETDATE())
GROUP BY client_type, execution_type
ORDER BY avg_seconds DESC;
```

---

## 8. Client-Specific Plugin Folder Structure

### 8.1 IClientPlugin Interface

Every client plugin implements this one interface. The core engine only knows about this interface � it never imports client-specific code directly.

```csharp
// IPS.AutoPost.Core/Interfaces/IClientPlugin.cs
public interface IClientPlugin
{
    // Unique identifier matching client_type in generic_job_configuration
    string ClientType { get; }

    // Called by engine for auto-post and manual post
    Task<List<PostResult>> ExecutePostAsync(
        GenericJobConfig config,
        string itemIds,         // empty = auto (fetch from queue), non-empty = manual
        int userId,
        CancellationToken ct);

    // Called by engine for feed download (return false if not applicable)
    Task<FeedResult> ExecuteFeedDownloadAsync(
        GenericJobConfig config,
        CancellationToken ct);

    // Called before post to allow client-specific pre-processing (optional)
    Task OnBeforePostAsync(GenericJobConfig config, CancellationToken ct)
        => Task.CompletedTask;  // default: do nothing
}
```

### 8.2 Plugin Registration

```csharp
// IPS.AutoPost.Core/Engine/PluginRegistry.cs
public class PluginRegistry
{
    private readonly Dictionary<string, IClientPlugin> _plugins = new();

    public void Register(IClientPlugin plugin)
        => _plugins[plugin.ClientType.ToUpper()] = plugin;

    public IClientPlugin Resolve(string clientType)
        => _plugins.TryGetValue(clientType.ToUpper(), out var plugin)
            ? plugin
            : throw new NotSupportedException($"No plugin registered for client type: {clientType}");
}

// In Program.cs / Lambda startup:
var registry = new PluginRegistry();
registry.Register(new MediaPlugin());
registry.Register(new InvitedClubPlugin());
registry.Register(new GreenthalPlugin());
registry.Register(new MDSPlugin());
registry.Register(new MOBPlugin());
registry.Register(new CaliberPlugin());
registry.Register(new AkronPlugin());
// Adding new client = one line here + one folder in Plugins/
```

### 8.3 Example: Simple Client Plugin (e.g. Vantaca - JSON REST, no feed)

```csharp
// Plugins/Vantaca/VantacaPlugin.cs
public class VantacaPlugin : IClientPlugin
{
    public string ClientType => "VANTACA";

    public async Task<List<PostResult>> ExecutePostAsync(
        GenericJobConfig config, string itemIds, int userId, CancellationToken ct)
    {
        // 1. Build JSON payload from header data
        // 2. POST to config.PostServiceUrl with Basic Auth
        // 3. Return PostResult list
        // All routing, history, logging handled by core engine
    }

    public Task<FeedResult> ExecuteFeedDownloadAsync(
        GenericJobConfig config, CancellationToken ct)
        => Task.FromResult(FeedResult.NotApplicable());  // no feed for Vantaca
}
```

### 8.4 Example: Complex Client Plugin (MDS - TIFF + ZIP + company code grouping)

```csharp
// Plugins/MDS/MDSPlugin.cs
public class MDSPlugin : IClientPlugin
{
    private readonly TiffImageProcessor _tiffProcessor;

    public string ClientType => "MDS";

    public async Task<List<PostResult>> ExecutePostAsync(
        GenericJobConfig config, string itemIds, int userId, CancellationToken ct)
    {
        var clientConfig = config.GetClientConfig<MDSClientConfig>();  // deserialize client_config_json

        // MDS-specific: group by company code length
        var workitems = await _workitemService.GetWorkitemsAsync(config, itemIds);
        var grouped = workitems.GroupBy(w => w.ComCode.Length);

        foreach (var group in grouped)
        {
            var creds = await GetCredsByComCodeLength(config, group.Key);
            foreach (var docTypeGroup in group.GroupBy(w => w.DocumentType))
            {
                // MDS-specific: process TIFF, build ZIP, post XML
                var zipPath = await BuildZipPackage(docTypeGroup, config, creds);
                var response = await PostZipToRapidPay(zipPath, creds);
                // ... routing handled by core engine
            }
        }
    }
}
```


### 8.5 GenericRestPlugin — Zero-Code Client Handler

The `GenericRestPlugin` is a built-in plugin in `IPS.AutoPost.Core` that handles **simple REST clients entirely from database configuration** — no plugin folder, no custom code. It reads `generic_field_mapping` rows to build the JSON payload dynamically.

**Which clients use GenericRestPlugin (no plugin folder needed):**

| Client | Post Type | Feed | Why Config-Only Works |
|---|---|---|---|
| Vantaca | JSON REST | No | Simple field mapping, Basic Auth |
| Akron | JSON REST | No | Simple field mapping, Basic Auth |
| Michelman | JSON REST | No | Simple field mapping |
| Mincingespice | JSON REST | No | Simple field mapping |
| Signature | JSON REST | No | Simple field mapping |
| Rent Manager | JSON REST | No | Simple field mapping |
| ReactorNet | JSON REST | No | Simple field mapping |
| Workday | XML REST | No | Simple field mapping, OAuth |
| Trump | JSON REST | No | Simple field mapping, credential table |

**How to register a config-only client (no code at all):**
```csharp
// In Program.cs — GenericRestPlugin is registered ONCE and handles all simple clients
registry.Register(new GenericRestPlugin(fieldMappingRepo, workitemService, routingService));

// Then in generic_job_configuration, set client_type = 'GENERIC_REST'
// The plugin reads field mappings from generic_field_mapping for that job_config_id
```

**GenericRestPlugin implementation:**
```csharp
// IPS.AutoPost.Core/Engine/GenericRestPlugin.cs
public class GenericRestPlugin : IClientPlugin
{
    private readonly IFieldMappingRepository _fieldMappingRepo;
    private readonly WorkitemService _workitemService;
    private readonly IRestClient _restClient;

    public string ClientType => "GENERIC_REST";

    public async Task<List<PostResult>> ExecutePostAsync(
        GenericJobConfig config, string itemIds, int userId, CancellationToken ct)
    {
        var results = new List<PostResult>();

        // 1. Load field mappings for this job from generic_field_mapping
        var headerMappings = await _fieldMappingRepo.GetMappingsAsync(
            config.Id, "INVOICE_HEADER");
        var lineMappings = await _fieldMappingRepo.GetMappingsAsync(
            config.Id, "INVOICE_LINE");

        // 2. Fetch workitems from queue
        var workitems = string.IsNullOrEmpty(itemIds)
            ? await _workitemService.GetWorkitemsAsync(config)
            : await _workitemService.GetWorkitemsByItemIdsAsync(itemIds);

        foreach (var workitem in workitems)
        {
            // 3. Read header row from HeaderTable
            var headerRow = await _workitemService.GetHeaderRowAsync(workitem.ItemId, config);

            // 4. Build JSON payload from field mappings
            var payload = BuildPayload(headerRow, headerMappings, lineMappings, config);

            // 5. POST to API
            var response = await _restClient.PostAsync(
                config.PostServiceUrl, payload, config.AuthType,
                config.AuthUsername, config.AuthPassword);

            // 6. Return result — core engine handles routing + history
            results.Add(new PostResult {
                ItemId    = workitem.ItemId,
                IsSuccess = response.IsSuccessful,
                Response  = response.Content
            });
        }
        return results;
    }

    private string BuildPayload(DataRow headerRow, List<FieldMapping> headerMappings,
        List<FieldMapping> lineMappings, GenericJobConfig config)
    {
        var obj = new JObject();
        foreach (var mapping in headerMappings.OrderBy(m => m.SortOrder))
        {
            var value = ResolveSourceValue(headerRow, mapping.SourceField);
            var transformed = ApplyTransform(value, mapping.DataType, mapping.TransformRule);
            obj[mapping.TargetField] = transformed;
        }
        return obj.ToString();
    }

    private string ResolveSourceValue(DataRow row, string sourceField)
    {
        // Handle CONST: prefix for constant values
        if (sourceField.StartsWith("CONST:"))
            return sourceField.Substring(6);

        return row.Table.Columns.Contains(sourceField)
            ? Convert.ToString(row[sourceField])
            : string.Empty;
    }

    private JToken ApplyTransform(string value, string dataType, string transformRuleJson)
    {
        if (string.IsNullOrEmpty(value)) return JValue.CreateNull();

        if (!string.IsNullOrEmpty(transformRuleJson))
        {
            var rule = JObject.Parse(transformRuleJson);
            if (rule["format"] != null && dataType == "DATE")
                return DateTime.Parse(value).ToString(rule["format"].ToString());
            if (rule["decimals"] != null)
                return Math.Round(decimal.Parse(value), (int)rule["decimals"]);
            if (rule["ifEmpty"] != null && string.IsNullOrEmpty(value))
                return rule["ifEmpty"].ToString();
        }

        return dataType switch {
            "INT"     => (JToken)(int)int.Parse(value),
            "DECIMAL" => (JToken)decimal.Parse(value),
            "BIT"     => (JToken)bool.Parse(value),
            _         => (JToken)value
        };
    }

    // No feed download for generic REST clients
    public Task<FeedResult> ExecuteFeedDownloadAsync(GenericJobConfig config, CancellationToken ct)
        => Task.FromResult(FeedResult.NotApplicable());
}
```

**Decision tree — plugin folder vs GenericRestPlugin:**
```
New client arrives:
    |
    +-- Does it post to a REST/HTTP endpoint?
    |       NO  -> Plugin folder required (SOAP, file drop, etc.)
    |       YES ->
    |           +-- Is the payload a simple flat JSON/XML?
    |           |       NO  -> Plugin folder required (ZIP, multi-step, grouped)
    |           |       YES ->
    |           |           +-- Are all field values from the header/detail table or constants?
    |           |                   NO  -> Plugin folder required (computed fields, lookups)
    |           |                   YES -> Use GenericRestPlugin + generic_field_mapping rows
    |
    +-- Does it download a feed?
            NO  -> GenericRestPlugin handles it (returns FeedResult.NotApplicable)
            YES ->
                +-- Is the feed a simple REST/FTP download into a flat table?
                        NO  -> Plugin folder required (SOAP feed, incremental logic)
                        YES -> GenericRestPlugin + generic_feed_configuration rows
```

---

## 9. Core Interfaces and Contracts

### 9.1 GenericJobConfig Model

```csharp
// IPS.AutoPost.Core/Models/GenericJobConfig.cs
public class GenericJobConfig
{
    public int Id { get; set; }
    public string ClientType { get; set; }
    public int JobId { get; set; }
    public string JobName { get; set; }
    public int DefaultUserId { get; set; }

    // Queues
    public string SourceQueueId { get; set; }
    public int SuccessQueueId { get; set; }
    public int PrimaryFailQueueId { get; set; }
    public int? SecondaryFailQueueId { get; set; }
    public int? QuestionQueueId { get; set; }

    // Tables
    public string HeaderTable { get; set; }
    public string DetailTable { get; set; }
    public string DetailUidColumn { get; set; }
    public string HistoryTable { get; set; }
    public string DbConnectionString { get; set; }

    // Service
    public string PostServiceUrl { get; set; }
    public string AuthType { get; set; }
    public string AuthUsername { get; set; }
    public string AuthPassword { get; set; }

    // Flags
    public bool AllowAutoPost { get; set; }
    public bool DownloadFeed { get; set; }
    public bool IsLegacyJob { get; set; }
    public bool UseS3ForImages { get; set; }
    public DateTime LastPostTime { get; set; }
    public DateTime LastDownloadTime { get; set; }

    // Paths
    public string OutputFilePath { get; set; }
    public string FeedDownloadPath { get; set; }
    public string ImageParentPath { get; set; }

    // Client-specific extras (deserialized from client_config_json)
    public string ClientConfigJson { get; set; }

    // Helper: deserialize client_config_json into typed object
    public T GetClientConfig<T>() where T : class, new()
        => string.IsNullOrEmpty(ClientConfigJson)
            ? new T()
            : JsonSerializer.Deserialize<T>(ClientConfigJson) ?? new T();
}
```

### 9.2 AutoPostOrchestrator (Core Engine)

```csharp
// IPS.AutoPost.Core/Engine/AutoPostOrchestrator.cs
public class AutoPostOrchestrator
{
    private readonly IConfigurationRepository _configRepo;
    private readonly PluginRegistry _pluginRegistry;
    private readonly SchedulerService _scheduler;
    private readonly RoutingService _routing;
    private readonly AuditService _audit;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var configs = await _configRepo.GetActiveConfigurationsAsync();

        foreach (var config in configs)
        {
            try
            {
                var plugin = _pluginRegistry.Resolve(config.ClientType);
                var schedules = await _configRepo.GetSchedulesAsync(config.Id);

                // Pre-processing hook (e.g. InvitedClub RetryImages)
                await plugin.OnBeforePostAsync(config, ct);

                // Auto-post
                if (config.AllowAutoPost && _scheduler.ShouldExecute(config.LastPostTime, schedules))
                {
                    _audit.WriteLog($"[{config.ClientType}] Job {config.JobId} - PostData started");
                    var results = await plugin.ExecutePostAsync(config, string.Empty, config.DefaultUserId, ct);
                    await _configRepo.UpdateLastPostTimeAsync(config.Id);
                    _audit.WriteLog($"[{config.ClientType}] Job {config.JobId} - PostData completed: {results.Count} records");
                }

                // Feed download
                if (config.DownloadFeed && _scheduler.ShouldDownload(config.LastDownloadTime, schedules))
                {
                    _audit.WriteLog($"[{config.ClientType}] Job {config.JobId} - FeedDownload started");
                    var feedResult = await plugin.ExecuteFeedDownloadAsync(config, ct);
                    if (feedResult.Success)
                        await _configRepo.UpdateLastDownloadTimeAsync(config.Id);
                }
            }
            catch (Exception ex)
            {
                _audit.WriteLog($"[{config.ClientType}] Job {config.JobId} - ERROR: {ex.Message}");
            }
        }
    }
}
```

### 9.3 WorkitemService (Shared - replaces GetWorkitemData in all 15 libs)

```csharp
// IPS.AutoPost.Core/Services/WorkitemService.cs
public class WorkitemService
{
    public async Task<DataSet> GetWorkitemsAsync(GenericJobConfig config)
        => await SqlHelper.ExecuteDatasetAsync(CommandType.Text,
            $"SELECT w.ItemId, w.StatusId, w.ImagePath FROM Workitems w " +
            $"JOIN {config.HeaderTable} h ON w.ItemId = h.UID " +
            $"WHERE w.JobId = @JobId AND w.StatusId IN ({config.SourceQueueId}) " +
            $"AND ISNULL(h.PostInProcess, 0) = 0",
            SqlHelper.Param("@JobId", SqlDbType.Int, config.JobId));

    public async Task<DataSet> GetWorkitemsByItemIdsAsync(string itemIds)
        => await SqlHelper.ExecuteDatasetAsync(CommandType.Text,
            "SELECT w.ItemId, w.StatusId, w.ImagePath FROM Workitems w " +
            "WHERE w.ItemId IN (SELECT * FROM dbo.split(@itemids, ','))",
            SqlHelper.Param("@itemids", SqlDbType.VarChar, itemIds));

    public async Task SetInProcessAsync(long itemId, string headerTable, bool inProcess)
        => await SqlHelper.ExecuteNonQueryAsync(CommandType.Text,
            $"UPDATE {headerTable} SET PostInProcess = @flag WHERE UID = @uid",
            SqlHelper.Param("@flag", SqlDbType.Bit, inProcess ? 1 : 0),
            SqlHelper.Param("@uid", SqlDbType.BigInt, itemId));
}
```

---

## 10. AWS Hosting Architecture

### 10.1 Service Selection Per Workload

| Job Type | AWS Service | Reason |
|---|---|---|
| Most post jobs (InvitedClub, Vantaca, Akron, Caliber, etc.) | **Lambda** | Fast, < 15 min, no server management |
| Media MediaOrders download (10-day chunks, months of data) | **ECS Fargate** | Can exceed 15 min Lambda limit |
| Greenthal file generation (large batches) | **Lambda** | File I/O, fast enough |
| MDS TIFF processing (GDI+, large images) | **ECS Fargate** | GDI+ needs full OS, Lambda has limits |
| Manual trigger from Workflow UI | **API Gateway ? Lambda** | Low latency, REST endpoint |
| Image retry queue | **SQS ? Lambda** | Decoupled, automatic backoff |
| Scheduled execution | **EventBridge Scheduler** | Cron per job, replaces Windows Service timer |

### 10.2 AWS Architecture Diagram

```
+---------------------------------------------------------------------+
�                         TRIGGER LAYER                                �
�                                                                      �
�  EventBridge Scheduler          API Gateway                SQS       �
�  +---------------------+   +------------------+   +--------------+  �
�  � cron(0 8 * * ? *)   �   � POST /post/{jobId}�   � retry-queue  �  �
�  � per job_config row  �   � POST /feed/{jobId}�   � (image retry)�  �
�  +---------------------+   +------------------+   +--------------+  �
+-------------+------------------------+--------------------+----------+
              �                        �                    �
+-------------?------------------------?--------------------?----------+
�                         COMPUTE LAYER                                 �
�                                                                       �
�  Lambda (short jobs)              ECS Fargate (long jobs)             �
�  +--------------------------+    +------------------------------+    �
�  � IPS.AutoPost.Host.Lambda �    � IPS.AutoPost.Host.Worker     �    �
�  �                          �    �                              �    �
�  � Handles:                 �    � Handles:                     �    �
�  � - InvitedClub post       �    � - Media MediaOrders download �    �
�  � - Greenthal file gen     �    � - MDS TIFF processing        �    �
�  � - Vantaca, Akron, etc.   �    � - Any job > 15 min           �    �
�  � - Feed downloads (REST)  �    �                              �    �
�  +--------------------------+    +------------------------------+    �
�                                                                       �
�  Both use same IPS.AutoPost.Core + IPS.AutoPost.Plugins assemblies    �
+---------------------------------------------------------------------+
              �
+-------------?---------------------------------------------------------+
�                          DATA LAYER                                    �
�                                                                        �
�  RDS SQL Server (existing Workflow DB)                                 �
�  +-- generic_job_configuration    (new - replaces 15 config tables)   �
�  +-- generic_execution_schedule   (new)                                �
�  +-- generic_post_history         (new)                                �
�  +-- generic_feed_configuration   (new)                                �
�  +-- Workitems                    (existing - unchanged)               �
�  +-- WFxxxIndexHeader tables      (existing - unchanged)               �
�  +-- InvitedClubSupplier etc.     (existing - unchanged)               �
�                                                                        �
�  S3 Buckets                                                            �
�  +-- ips-invoice-images/          (existing - invoice PDFs/TIFFs)      �
�  +-- ips-feed-archive/            (new - downloaded feed files)        �
�  +-- ips-output-files/            (new - generated CSV/Excel files)    �
�                                                                        �
�  Secrets Manager                                                       �
�  +-- /InvoiceSystem/Common/{env}/Database/Workflow  (existing)         �
�  +-- /InvoiceSystem/{client}/{env}/PostAuth         (new per client)   �
�  +-- /InvoiceSystem/{client}/{env}/FeedAuth         (new per client)   �
+------------------------------------------------------------------------+
              �
+-------------?----------------------------------------------------------+
�                       OBSERVABILITY LAYER                               �
�  CloudWatch Logs  �  CloudWatch Metrics  �  SNS Alerts  �  X-Ray       �
�  - Per job log group: /ips/autopost/{client_type}/{job_id}             �
�  - Alarm: error rate > 5 in 5 min ? SNS ? email/Slack                 �
�  - Dashboard: executions per job, success rate, duration               �
+------------------------------------------------------------------------+
```

### 10.3 Lambda Function.cs

```csharp
// IPS.AutoPost.Host.Lambda/Function.cs
public class Function
{
    private readonly AutoPostOrchestrator _orchestrator;

    public Function()
    {
        var services = new ServiceCollection();
        services.AddAutoPostCore();          // registers SqlHelper, services
        services.AddAutoPostPlugins();       // registers all client plugins
        var sp = services.BuildServiceProvider();
        _orchestrator = sp.GetRequiredService<AutoPostOrchestrator>();
    }

    // Triggered by EventBridge Scheduler with {"JobId": 123, "TriggerType": "Scheduled"}
    // Triggered by API Gateway with {"JobId": 123, "ItemIds": "456,789", "TriggerType": "Manual"}
    public async Task FunctionHandler(LambdaEvent evt, ILambdaContext context)
    {
        if (string.IsNullOrEmpty(evt.ItemIds))
            await _orchestrator.RunAsync(evt.JobId);           // auto mode
        else
            await _orchestrator.RunManualAsync(evt.JobId, evt.ItemIds, evt.UserId);  // manual mode
    }
}
```

### 10.4 Worker Service (ECS Fargate)

```csharp
// IPS.AutoPost.Host.Worker/Worker.cs
public class Worker : BackgroundService
{
    private readonly AutoPostOrchestrator _orchestrator;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _orchestrator.RunAsync(ct);
            await Task.Delay(TimeSpan.FromMinutes(30), ct);  // configurable
        }
    }
}
```

---

## 11. Implementation Roadmap

### Phase 1 � Foundation (Week 1-2)
Build the core engine. No client logic yet.

| Task | What to Build |
|---|---|
| 1.1 | Create solution `IPS.AutoPost.Platform.sln` with .NET Core 10 |
| 1.2 | Create `IPS.AutoPost.Core` project |
| 1.3 | Move `SqlHelper.cs` to Core (one copy, delete all others) |
| 1.4 | Create `IClientPlugin` interface |
| 1.5 | Create `GenericJobConfig` model |
| 1.6 | Create `AutoPostOrchestrator` |
| 1.7 | Create `WorkitemService`, `RoutingService`, `AuditService` |
| 1.8 | Create `SchedulerService` |
| 1.9 | Create `PluginRegistry` |
| 1.10 | Create `IPS.AutoPost.Host.Lambda` project |
| 1.11 | Create `IPS.AutoPost.Host.Worker` project |
| 1.12 | Create `IPS.AutoPost.Api` project (manual trigger) |

### Phase 2 � Database Migration (Week 2-3)
Create generic tables and migrate existing config data.

| Task | Script |
|---|---|
| 2.1 | Create `generic_job_configuration` table |
| 2.2 | Create `generic_execution_schedule` table |
| 2.3 | Create `generic_feed_configuration` table |
| 2.4 | Create `generic_auth_configuration` table |
| 2.5 | Create `generic_queue_routing_rules` table |
| 2.6 | Create `generic_post_history` table |
| 2.7 | Create `generic_email_configuration` table |
| 2.8 | Migrate Media config rows (INSERT INTO generic_job_configuration SELECT ... FROM post_to_media_configuration) |
| 2.9 | Migrate InvitedClub config rows |
| 2.10 | Migrate Greenthal config rows |
| 2.11 | Migrate MDS config rows |
| 2.12 | Create `get_generic_job_configurations` SP |

### Phase 3 � Client Plugins (Week 3-6)
Build plugins one by one, starting with simplest.

| Priority | Plugin | Complexity | Why This Order |
|---|---|---|---|
| 1 | `VantacaPlugin` | Low | Simple JSON REST, no feed � proves the pattern |
| 2 | `AkronPlugin` | Low | Simple JSON REST, no feed |
| 3 | `MichelmanPlugin` | Low | Simple JSON REST, no feed |
| 4 | `InvitedClubPlugin` | Medium | REST + S3 + 3-step post |
| 5 | `MediaPlugin` | Medium | SOAP + feed download |
| 6 | `MDSPlugin` | High | TIFF + ZIP + company code grouping |
| 7 | `GreenthalPlugin` | High | 20+ job types � biggest plugin |
| 8 | `MOBPlugin` | High | PDF + email approval |
| 9 | All remaining | Low-Medium | Caliber, Akron, Workday, Trump, etc. |

### Phase 4 � AWS Deployment (Week 6-7)

| Task | What |
|---|---|
| 4.1 | Create CloudFormation stack (Lambda + ECS + EventBridge + API GW) |
| 4.2 | Set up EventBridge rules per job (one rule per `generic_job_configuration` row) |
| 4.3 | Configure VPC, subnets, security groups for RDS access |
| 4.4 | Migrate secrets to Secrets Manager |
| 4.5 | Set up CloudWatch dashboards and alarms |
| 4.6 | UAT parallel run (old services + new platform running simultaneously) |

### Phase 5 � Cutover (Week 8)

| Task | What |
|---|---|
| 5.1 | Stop old Windows Services one by one |
| 5.2 | Verify new platform handles each client |
| 5.3 | Decommission old EC2 instances |
| 5.4 | Archive old Visual Studio projects |

---

## 12. Conclusion - Plain English Summary

### What Is the Problem?

You have 15+ separate projects that all do the same thing: pick up invoices from a queue, post them to an external system, and download reference data. Each project is a copy-paste of the others with client-specific logic mixed in. Every new client means a new project, new deployment, new server, and weeks of work.

### What Are We Building?

One platform with two parts:

**Part 1 � The Generic Engine** (never changes)
This handles everything that is the same across all clients: reading the schedule, picking up workitems from the queue, routing records to success/fail queues, writing history, sending emails, and logging. It reads all its settings from a database table called `generic_job_configuration`.

**Part 2 � Client Plugins** (one folder per client)
Each client gets a folder (`Plugins/Media/`, `Plugins/InvitedClub/`, `Plugins/Greenthal/`, etc.) with the logic that is unique to that client. For Media, that's the SOAP calls and CSV file format. For InvitedClub, that's the 3-step Oracle REST post and image attachment. For Greenthal, that's the 20+ different file formats. The engine calls the plugin � the plugin does the client-specific work.

### What Goes in the Database vs What Goes in Code?

**In the database** (no code change needed):
- Which URL to post to
- Which queue to route success/fail records to
- What time to run
- Which tables to read from
- Auth credentials
- Email addresses for alerts

**In code** (client plugin folder):
- How to format the payload (CSV, JSON, XML, ZIP)
- How to call the external API (SOAP, REST, file drop)
- Any special processing (TIFF resize, image attachment, tax calculation, file grouping)
- Business rules specific to that client

### What Does "Adding a New Client" Look Like?

1. Add rows to `generic_job_configuration` in the database (15 minutes)
2. Create a new folder `Plugins/NewClient/` with one class that implements `IClientPlugin` (2-4 hours for the actual API logic)
3. Register it in `PluginRegistry.cs` (one line)
4. Deploy (the platform is already running � no new server, no new service)

### Where Does It Run?

On AWS. Short jobs (most clients) run as Lambda functions triggered by EventBridge on a schedule. Long-running jobs (Media's large feed downloads, MDS's TIFF processing) run as ECS Fargate containers. Manual triggers from the Workflow UI go through API Gateway. Everything connects to the existing RDS SQL Server database.

### What Do We Keep From the Old Code?

- The `Workflow` database and all existing tables � unchanged
- The `Workitems` table � unchanged
- The `WORKITEM_ROUTE` and `GENERALLOG_INSERT` stored procedures � unchanged
- The client-specific index tables (`WFxxxIndexHeader`, etc.) � unchanged
- The S3 bucket for images � unchanged
- The AWS Secrets Manager path � unchanged

### What Do We Replace?

- 15 separate Visual Studio projects ? 1 solution
- 15 copies of `SqlHelper.cs` ? 1 shared copy
- 15 Windows Services ? 1 Lambda function + 1 ECS task
- 15 EC2 instances ? serverless (Lambda) + Fargate (only when needed)
- 15 config tables ? 1 `generic_job_configuration` table
- 15 config stored procedures ? 1 `get_generic_job_configurations` SP

---

*Document Version: 2.0 � Complete rewrite*
*Analysis covers: Media, InvitedClub, Greenthal, MDS, MOB, Caliber, Akron, Vantaca, Workday, Trump, Mincingespice, Michelman, Signature, Rent Manager, ReactorNet*
*Target: .NET Core 10 | AWS Lambda + ECS Fargate | SQL Server RDS*
*Date: April 23, 2026*
