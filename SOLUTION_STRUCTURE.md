# IPS.AutoPost Platform — Complete Solution Structure & Technology Stack
### .NET Core 10 | AWS ECS Fargate | Plugin Architecture | Multi-Client

> **Version:** 1.0
> **Date:** April 30, 2026
> **Location:** D:\Projects\IPS\Generic-Solution\

---

## Table of Contents

1. [Solution Overview](#1-solution-overview)
2. [Complete Folder and File Structure](#2-complete-folder-and-file-structure)
3. [Project Details — Every .csproj](#3-project-details--every-csproj)
4. [Technology Stack — Every Tool and Package](#4-technology-stack--every-tool-and-package)
5. [NuGet Package Reference — Complete List](#5-nuget-package-reference--complete-list)
6. [AWS Services Used](#6-aws-services-used)
7. [Database Tools and Scripts](#7-database-tools-and-scripts)
8. [Development Tools](#8-development-tools)
9. [CI/CD Pipeline Tools](#9-cicd-pipeline-tools)
10. [Configuration Files](#10-configuration-files)
11. [Project Dependencies Map](#11-project-dependencies-map)

---

## 1. Solution Overview

```
Platform Name:    IPS.AutoPost Platform
Solution File:    IPS.AutoPost.Platform.sln
Target Framework: .NET 10.0
Language:         C# 13
Architecture:     Clean Architecture + Plugin Pattern
Deployment:       AWS ECS Fargate (Docker containers)
Database:         SQL Server (Amazon RDS)
Queue:            Amazon SQS
Registry:         Amazon ECR
Monitoring:       Amazon CloudWatch
Secrets:          AWS Secrets Manager
Storage:          Amazon S3
Scheduling:       Amazon EventBridge + AWS Lambda
CI/CD:            GitHub Actions
```

---

## 2. Complete Folder and File Structure

```
D:\Projects\IPS\Generic-Solution\
|
+-- IPS.AutoPost.Platform.sln
|
+-- src/
|   |
|   +-- IPS.AutoPost.Core/
|   |   +-- IPS.AutoPost.Core.csproj
|   |   |
|   |   +-- Commands/                              # MediatR commands (CQRS)
|   |   |   +-- ExecutePostCommand.cs             # Sent by PostWorker per SQS message
|   |   |   +-- ExecuteFeedCommand.cs             # Sent by FeedWorker per SQS message
|   |   |
|   |   +-- Handlers/                             # MediatR handlers
|   |   |   +-- ExecutePostHandler.cs             # Handles ExecutePostCommand
|   |   |   +-- ExecuteFeedHandler.cs             # Handles ExecuteFeedCommand
|   |   |
|   |   +-- Behaviors/                            # MediatR pipeline behaviors
|   |   |   +-- LoggingBehavior.cs                # Logs every command start/end
|   |   |   +-- ValidationBehavior.cs             # Runs FluentValidation before handler
|   |   |
|   |   +-- Engine/
|   |   |   +-- AutoPostOrchestrator.cs
|   |   |   +-- SchedulerService.cs
|   |   |   +-- PluginRegistry.cs
|   |   |
|   |   +-- Interfaces/
|   |   |   +-- IClientPlugin.cs
|   |   |   +-- IConfigurationRepository.cs
|   |   |   +-- IWorkitemRepository.cs
|   |   |   +-- IRoutingRepository.cs
|   |   |   +-- IAuditRepository.cs
|   |   |   +-- IScheduleRepository.cs
|   |   |   +-- ICloudWatchMetricsService.cs      # Granular per-client metrics interface
|   |   |   +-- ICorrelationIdService.cs          # Correlation ID tracking interface
|   |   |
|   |   +-- Models/
|   |   |   +-- GenericJobConfig.cs
|   |   |   +-- WorkitemData.cs
|   |   |   +-- DynamicRecord.cs                  # Schema-agnostic data model (key/value)
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
|   |   |
|   |   +-- Services/
|   |   |   +-- ConfigurationService.cs
|   |   |   +-- S3ImageService.cs
|   |   |   +-- EmailService.cs
|   |   |   +-- CloudWatchMetricsService.cs       # Granular per-client CloudWatch metrics
|   |   |   +-- CorrelationIdService.cs           # AsyncLocal correlation ID per SQS message
|   |   |
|   |   +-- DataAccess/
|   |   |   +-- SqlHelper.cs
|   |   |   +-- ConfigurationRepository.cs
|   |   |   +-- WorkitemRepository.cs
|   |   |   +-- RoutingRepository.cs
|   |   |   +-- AuditRepository.cs
|   |   |   +-- ScheduleRepository.cs
|   |   |
|   |   +-- Migrations/                           # EF Core migrations for generic tables
|   |   |   +-- DatabaseContext.cs                # EF Core DbContext for generic tables only
|   |   |   +-- 20260430_InitialGenericTables.cs  # Creates all 10 generic tables
|   |   |   +-- DatabaseContextModelSnapshot.cs
|   |   |
|   |   +-- Infrastructure/                       # AWS and external system integrations
|   |   |   +-- SecretsManagerConfigurationProvider.cs  # Config-path Secrets Manager pattern
|   |   |
|   |   +-- Exceptions/
|   |   |   +-- PluginNotFoundException.cs
|   |   |
|   |   +-- Extensions/
|   |       +-- DataTableExtensions.cs
|   |       +-- ParserExtensions.cs
|   |
|   +-- IPS.AutoPost.Plugins/
|   |   +-- IPS.AutoPost.Plugins.csproj
|   |   +-- PluginRegistration.cs
|   |   |
|   |   +-- InvitedClub/
|   |   |   +-- InvitedClubPlugin.cs
|   |   |   +-- InvitedClubPostStrategy.cs
|   |   |   +-- InvitedClubFeedStrategy.cs
|   |   |   +-- InvitedClubRetryService.cs
|   |   |   |
|   |   |   +-- Models/
|   |   |   |   +-- InvitedClubConfig.cs
|   |   |   |   +-- InvoiceRequest.cs
|   |   |   |   +-- AttachmentRequest.cs
|   |   |   |   +-- InvoiceCalculateTaxRequest.cs
|   |   |   |   +-- InvoiceResponse.cs
|   |   |   |   +-- AttachmentResponse.cs
|   |   |   |   +-- InvoiceCalculateTaxResponse.cs
|   |   |   |   +-- InvoicePostResponse.cs
|   |   |   |   +-- SupplierResponse.cs
|   |   |   |   +-- SupplierAddressResponse.cs
|   |   |   |   +-- SupplierSiteResponse.cs
|   |   |   |   +-- COAResponse.cs
|   |   |   |   +-- FailedImagesData.cs
|   |   |   |   +-- PostHistory.cs
|   |   |   |   +-- EmailConfig.cs
|   |   |   |   +-- APIResponseType.cs
|   |   |   |
|   |   |   +-- Constants/
|   |   |       +-- InvitedClubConstants.cs
|   |   |
|   |   +-- Sevita/
|   |       +-- SevitaPlugin.cs
|   |       +-- SevitaPostStrategy.cs
|   |       +-- SevitaTokenService.cs
|   |       +-- SevitaValidationService.cs
|   |       |
|   |       +-- Models/
|   |       |   +-- SevitaConfig.cs
|   |       |   +-- InvoiceRequest.cs
|   |       |   +-- InvoiceResponse.cs
|   |       |   +-- InvoicePostResponse.cs
|   |       |   +-- ValidIds.cs
|   |       |   +-- PostHistory.cs
|   |       |   +-- PostFailedRecord.cs
|   |       |   +-- PostResponseType.cs
|   |       |   +-- EmailConfiguration.cs
|   |       |   +-- FailedPostConfiguration.cs
|   |       |   +-- DBErrorEmailConfiguration.cs
|   |       |
|   |       +-- Constants/
|   |           +-- SevitaConstants.cs
|   |
|   +-- IPS.AutoPost.Host.FeedWorker/
|   |   +-- IPS.AutoPost.Host.FeedWorker.csproj
|   |   +-- Program.cs
|   |   +-- FeedWorker.cs
|   |   +-- appsettings.json
|   |   +-- appsettings.Development.json
|   |
|   +-- IPS.AutoPost.Host.PostWorker/
|   |   +-- IPS.AutoPost.Host.PostWorker.csproj
|   |   +-- Program.cs
|   |   +-- PostWorker.cs
|   |   +-- appsettings.json
|   |   +-- appsettings.Development.json
|   |
|   +-- IPS.AutoPost.Api/
|   |   +-- IPS.AutoPost.Api.csproj
|   |   +-- Program.cs
|   |   +-- appsettings.json
|   |   +-- appsettings.Development.json
|   |   |
|   |   +-- Controllers/
|   |   |   +-- PostController.cs
|   |   |   +-- FeedController.cs
|   |   |   +-- StatusController.cs
|   |   |
|   |   +-- Middleware/
|   |       +-- ApiKeyAuthMiddleware.cs
|   |
|   +-- IPS.AutoPost.Scheduler/
|       +-- IPS.AutoPost.Scheduler.csproj
|       +-- Function.cs
|       +-- SchedulerSyncService.cs
|       +-- aws-lambda-tools-defaults.json
|
+-- tests/
|   |
|   +-- IPS.AutoPost.Core.Tests/
|   |   +-- IPS.AutoPost.Core.Tests.csproj
|   |   +-- Engine/
|   |   |   +-- AutoPostOrchestratorTests.cs
|   |   |   +-- SchedulerServiceTests.cs
|   |   |   +-- PluginRegistryTests.cs
|   |   +-- Handlers/
|   |   |   +-- ExecutePostHandlerTests.cs        # MediatR handler tests
|   |   |   +-- ExecuteFeedHandlerTests.cs
|   |   +-- Behaviors/
|   |   |   +-- LoggingBehaviorTests.cs           # Pipeline behavior tests
|   |   |   +-- ValidationBehaviorTests.cs
|   |   +-- Services/
|   |   |   +-- CloudWatchMetricsServiceTests.cs  # Granular metrics tests
|   |   |   +-- CorrelationIdServiceTests.cs      # AsyncLocal isolation tests
|   |   +-- DataAccess/
|   |   |   +-- SqlHelperTests.cs
|   |   |   +-- WorkitemRepositoryTests.cs
|   |   +-- Extensions/
|   |       +-- DataTableExtensionsTests.cs
|   |
|   +-- IPS.AutoPost.Plugins.Tests/
|       +-- IPS.AutoPost.Plugins.Tests.csproj
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
|   |
|   +-- cloudformation/
|   |   +-- infrastructure.yaml
|   |   +-- application.yaml
|   |   +-- monitoring.yaml
|   |
|   +-- docker/
|   |   +-- Dockerfile.FeedWorker
|   |   +-- Dockerfile.PostWorker
|   |
|   +-- scripts/
|       +-- deploy-infra.sh
|       +-- deploy-app.sh
|       +-- deploy-monitoring.sh
|
+-- db/
|   +-- migrations/
|   |   +-- 001_create_generic_job_configuration.sql
|   |   +-- 002_create_generic_execution_schedule.sql
|   |   +-- 003_create_generic_feed_configuration.sql
|   |   +-- 004_create_generic_auth_configuration.sql
|   |   +-- 005_create_generic_queue_routing_rules.sql
|   |   +-- 006_create_generic_post_history.sql
|   |   +-- 007_create_generic_email_configuration.sql
|   |   +-- 008_create_generic_feed_download_history.sql
|   |   +-- 009_create_generic_execution_history.sql
|   |   +-- 010_create_generic_field_mapping.sql
|   +-- seed/
|       +-- 011_seed_invitedclub_job_configuration.sql
|       +-- 012_seed_invitedclub_execution_schedule.sql
|       +-- 013_seed_sevita_job_configuration.sql
|       +-- 014_seed_sevita_execution_schedule.sql
|
+-- .github/
|   +-- workflows/
|       +-- deploy.yml
|
+-- .kiro/
|   +-- specs/
|       +-- generic-autopost-platform/
|           +-- requirements.md
|           +-- design.md
|           +-- tasks.md
|           +-- .config.kiro
|
+-- README.md
+-- SOLUTION_STRUCTURE.md          (this file)
+-- .gitignore
+-- .editorconfig
+-- global.json                    (pins .NET SDK version to 10.0)
+-- Directory.Build.props          (shared MSBuild properties)
+-- Directory.Packages.props       (centralized NuGet version management)
```

---

## 3. Project Details — Every .csproj

### 3.1 IPS.AutoPost.Core

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>IPS.AutoPost.Core</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <!-- AWS SDK -->
    <PackageReference Include="AWSSDK.S3" />
    <PackageReference Include="AWSSDK.SecretsManager" />
    <PackageReference Include="AWSSDK.CloudWatch" />
    <PackageReference Include="AWSSDK.CloudWatchLogs" />
    <PackageReference Include="AWSSDK.Extensions.NETCore.Setup" />

    <!-- Data Access -->
    <PackageReference Include="Microsoft.Data.SqlClient" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Tools" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" />

    <!-- MediatR CQRS + Pipeline Behaviors -->
    <PackageReference Include="MediatR" />
    <PackageReference Include="FluentValidation" />

    <!-- Hosting + DI -->
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Configuration" />

    <!-- Logging -->
    <PackageReference Include="Serilog" />
    <PackageReference Include="Serilog.Extensions.Hosting" />
    <PackageReference Include="Serilog.Sinks.Console" />
    <PackageReference Include="Serilog.Sinks.AmazonCloudWatch" />
    <PackageReference Include="Serilog.Settings.Configuration" />
  </ItemGroup>
</Project>
```

**Purpose:** Generic engine. Contains all shared infrastructure — SqlHelper, repositories, orchestrator, interfaces, models, MediatR commands/handlers/behaviors, EF Core DbContext for generic tables, CorrelationIdService, CloudWatchMetricsService, SecretsManagerConfigurationProvider. Never changes when adding new clients.

---

### 3.2 IPS.AutoPost.Plugins

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>IPS.AutoPost.Plugins</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\IPS.AutoPost.Core\IPS.AutoPost.Core.csproj" />
    <PackageReference Include="RestSharp" />
    <PackageReference Include="Newtonsoft.Json" />
    <PackageReference Include="ClosedXML" />
    <PackageReference Include="DocumentFormat.OpenXml" />
    <PackageReference Include="AWSSDK.S3" />
  </ItemGroup>
</Project>
```

**Purpose:** All client-specific plugins. InvitedClub (Oracle Fusion, Basic Auth, 3-step post + feed) and Sevita (OAuth2, PO/Non-PO validation, line grouping). New clients added here only.

---

### 3.3 IPS.AutoPost.Host.FeedWorker

```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>IPS.AutoPost.Host.FeedWorker</RootNamespace>
    <UserSecretsId>ips-autopost-feedworker</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\IPS.AutoPost.Core\IPS.AutoPost.Core.csproj" />
    <ProjectReference Include="..\IPS.AutoPost.Plugins\IPS.AutoPost.Plugins.csproj" />
    <PackageReference Include="AWSSDK.SQS" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Serilog.Extensions.Hosting" />
  </ItemGroup>
</Project>
```

**Purpose:** ECS Fargate Feed Worker. BackgroundService that polls `ips-feed-queue` and calls `AutoPostOrchestrator.RunScheduledFeedAsync`. Deployed as Docker container.

---

### 3.4 IPS.AutoPost.Host.PostWorker

```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>IPS.AutoPost.Host.PostWorker</RootNamespace>
    <UserSecretsId>ips-autopost-postworker</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\IPS.AutoPost.Core\IPS.AutoPost.Core.csproj" />
    <ProjectReference Include="..\IPS.AutoPost.Plugins\IPS.AutoPost.Plugins.csproj" />
    <PackageReference Include="AWSSDK.SQS" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Serilog.Extensions.Hosting" />
  </ItemGroup>
</Project>
```

**Purpose:** ECS Fargate Post Worker. BackgroundService that polls `ips-post-queue` and calls `AutoPostOrchestrator.RunScheduledPostAsync`. Deployed as Docker container.

---

### 3.5 IPS.AutoPost.Api

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>IPS.AutoPost.Api</RootNamespace>
    <UserSecretsId>ips-autopost-api</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\IPS.AutoPost.Core\IPS.AutoPost.Core.csproj" />
    <ProjectReference Include="..\IPS.AutoPost.Plugins\IPS.AutoPost.Plugins.csproj" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
    <PackageReference Include="Swashbuckle.AspNetCore" />
    <PackageReference Include="Serilog.AspNetCore" />
  </ItemGroup>
</Project>
```

**Purpose:** ASP.NET Core Web API for manual post triggers from Workflow UI. Exposes `POST /api/post/{jobId}/items/{itemIds}`, `POST /api/feed/{jobId}`, `GET /api/status/{executionId}`. Calls orchestrator directly (bypasses SQS). Protected by API key middleware.

---

### 3.6 IPS.AutoPost.Scheduler

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>IPS.AutoPost.Scheduler</RootNamespace>
    <AWSProjectType>Lambda</AWSProjectType>
    <AssemblyName>IPS.AutoPost.Scheduler</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\IPS.AutoPost.Core\IPS.AutoPost.Core.csproj" />
    <PackageReference Include="Amazon.Lambda.Core" />
    <PackageReference Include="Amazon.Lambda.Serialization.SystemTextJson" />
    <PackageReference Include="AWSSDK.EventBridge" />
    <PackageReference Include="AWSSDK.Scheduler" />
    <PackageReference Include="Microsoft.Data.SqlClient" />
  </ItemGroup>
</Project>
```

**Purpose:** AWS Lambda function. Runs every 10 minutes. Reads `generic_execution_schedule` from RDS and creates/updates/disables EventBridge Scheduler rules. NEVER touches invoices.

---

### 3.7 IPS.AutoPost.Core.Tests

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\IPS.AutoPost.Core\IPS.AutoPost.Core.csproj" />
    <!-- xUnit v3 — latest version, matches GenericMissingInvoicesProcess pattern -->
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Moq" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="FsCheck" />
    <PackageReference Include="FsCheck.Xunit" />
    <!-- EF Core InMemory: fast in-process DB for handler/behavior tests -->
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" />
    <PackageReference Include="coverlet.collector" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

---

### 3.8 IPS.AutoPost.Plugins.Tests

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\IPS.AutoPost.Core\IPS.AutoPost.Core.csproj" />
    <ProjectReference Include="..\..\src\IPS.AutoPost.Plugins\IPS.AutoPost.Plugins.csproj" />
    <!-- xUnit v3 — latest version -->
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Moq" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="FsCheck" />
    <PackageReference Include="FsCheck.Xunit" />
    <!-- WireMock.Net: HTTP mock server for Oracle Fusion and Sevita API calls -->
    <PackageReference Include="WireMock.Net" />
    <!-- EF Core InMemory: integration tests without real DB -->
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" />
    <PackageReference Include="coverlet.collector" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

---

## 4. Technology Stack — Every Tool and Package

### 4.1 Runtime and Language

| Tool | Version | Purpose |
|---|---|---|
| **.NET SDK** | 10.0 (latest) | Runtime for all projects |
| **C#** | 13 | Language |
| **ASP.NET Core** | 10.0 | Web API (IPS.AutoPost.Api) |
| **Worker Service** | 10.0 | Background services (FeedWorker, PostWorker) |
| **AWS Lambda .NET** | 10.0 | Scheduler Lambda |

### 4.2 Data Access

| Tool | Version | Purpose |
|---|---|---|
| **Microsoft.Data.SqlClient** | 5.x | SQL Server connectivity (replaces System.Data.SqlClient) |
| **SqlBulkCopy** | Built-in | Bulk feed data inserts (600s timeout) |
| **ADO.NET** | Built-in | Direct SQL execution via SqlHelper for existing Workflow tables |
| **Entity Framework Core** | 10.x | ORM for the 10 new generic tables only — migrations, schema management |
| **EF Core SQL Server** | 10.x | SQL Server provider for EF Core |
| **EF Core InMemory** | 10.x | In-memory database for fast integration tests (no real DB needed) |

> **Why Microsoft.Data.SqlClient over System.Data.SqlClient?**
> `System.Data.SqlClient` is the old .NET Framework package. `Microsoft.Data.SqlClient` is the actively maintained cross-platform version for .NET Core. Required for .NET 10.

> **Why EF Core for generic tables but ADO.NET for existing tables?**
> The 10 new generic tables (`generic_job_configuration`, `generic_execution_schedule`, etc.) are owned by this platform — EF Core migrations give us proper schema versioning, rollback capability, and auto-apply at startup. The existing Workflow tables (`Workitems`, `WFInvitedClubsIndexHeader`, etc.) are shared with other systems and must not be touched by EF Core — SqlHelper is used for those.

### 4.3 HTTP Client (ERP API Calls)

| Tool | Version | Purpose |
|---|---|---|
| **RestSharp** | 112.x | HTTP REST client for Oracle Fusion and Sevita API calls |

> **Why RestSharp?**
> The existing InvitedClub and Sevita libraries use RestSharp. Keeping the same library eliminates rewriting all HTTP call patterns. RestSharp 112.x supports .NET 10.

### 4.4 JSON Serialization

| Tool | Version | Purpose |
|---|---|---|
| **Newtonsoft.Json** | 13.x | JSON serialization/deserialization for Oracle Fusion payloads |
| **System.Text.Json** | Built-in | Used for `GetClientConfig<T>()` deserialization of `client_config_json` |

> **Why both?**
> Newtonsoft.Json is used for Oracle Fusion payloads because the existing code uses `JObject.Parse`, `JArray.Parse`, `JsonProperty` attributes, and `JsonConvert.SerializeObject`. System.Text.Json is used for the generic config deserialization where performance matters.

### 4.5 AWS SDK

| Package | Version | Purpose |
|---|---|---|
| **AWSSDK.SQS** | 3.7.x | Poll SQS queues, delete messages |
| **AWSSDK.S3** | 3.7.x | Get invoice images, upload audit JSON, archive feeds |
| **AWSSDK.SecretsManager** | 3.7.x | Retrieve DB credentials and API keys |
| **AWSSDK.CloudWatch** | 3.7.x | Publish custom metrics (PostSuccessCount etc.) |
| **AWSSDK.CloudWatchLogs** | 3.7.x | Write structured log entries |
| **AWSSDK.EventBridge** | 3.7.x | Create/update EventBridge rules (Scheduler Lambda) |
| **AWSSDK.Scheduler** | 3.7.x | EventBridge Scheduler API (Scheduler Lambda) |
| **Amazon.Lambda.Core** | 2.x | Lambda handler base |
| **Amazon.Lambda.Serialization.SystemTextJson** | 2.x | Lambda JSON serialization |

### 4.6 Excel Export

| Tool | Version | Purpose |
|---|---|---|
| **ClosedXML** | 0.102.x | Export missing COA codes to Excel (InvitedClub feed) |
| **DocumentFormat.OpenXml** | 3.x | Required by ClosedXML |

### 4.7 Logging

| Tool | Version | Purpose |
|---|---|---|
| **Serilog** | 4.x | Structured logging framework |
| **Serilog.Extensions.Hosting** | 8.x | Integration with .NET Generic Host |
| **Serilog.AspNetCore** | 8.x | Integration with ASP.NET Core |
| **Serilog.Sinks.Console** | 5.x | Console output (visible in ECS logs) |
| **Serilog.Sinks.AmazonCloudWatch** | 3.x | Direct CloudWatch Logs sink |

> **Why Serilog?**
> Serilog provides structured logging with properties (ClientType, JobId, ItemId) that map directly to CloudWatch Logs Insights queries. The existing code uses `CommonMethods.WriteToFile` — Serilog replaces this with cloud-native logging.

### 4.8 Testing

| Tool | Version | Purpose |
|---|---|---|
| **xUnit** | 2.9.x | Unit test framework |
| **xunit.runner.visualstudio** | 2.8.x | Visual Studio test runner integration |
| **Microsoft.NET.Test.Sdk** | 17.x | Test SDK |
| **Moq** | 4.20.x | Mocking framework for interfaces |
| **FluentAssertions** | 6.x | Readable assertion syntax |
| **FsCheck** | 3.x | Property-based testing (PBT) framework |
| **FsCheck.Xunit** | 3.x | FsCheck integration with xUnit |
| **WireMock.Net** | 1.6.x | HTTP mock server for Oracle Fusion and Sevita API integration tests |

### 4.9 API Documentation

| Tool | Version | Purpose |
|---|---|---|
| **Swashbuckle.AspNetCore** | 6.x | Swagger/OpenAPI documentation for IPS.AutoPost.Api |
| **Microsoft.AspNetCore.OpenApi** | 10.x | Built-in OpenAPI support |

---

## 5. NuGet Package Reference — Complete List

### Directory.Packages.props (Centralized Version Management)

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- AWS SDK -->
    <PackageVersion Include="AWSSDK.SQS"                                    Version="3.7.400.33" />
    <PackageVersion Include="AWSSDK.S3"                                     Version="3.7.412.4" />
    <PackageVersion Include="AWSSDK.SecretsManager"                         Version="3.7.400.33" />
    <PackageVersion Include="AWSSDK.CloudWatch"                             Version="3.7.503" />
    <PackageVersion Include="AWSSDK.CloudWatchLogs"                         Version="3.7.0" />
    <PackageVersion Include="AWSSDK.EventBridge"                            Version="3.7.400" />
    <PackageVersion Include="AWSSDK.Scheduler"                              Version="3.7.100" />
    <PackageVersion Include="Amazon.Lambda.Core"                            Version="2.5.0" />
    <PackageVersion Include="Amazon.Lambda.Serialization.SystemTextJson"    Version="2.4.4" />

    <!-- Data Access -->
    <PackageVersion Include="Microsoft.Data.SqlClient"                      Version="5.2.2" />

    <!-- HTTP Client -->
    <PackageVersion Include="RestSharp"                                     Version="112.1.0" />

    <!-- JSON -->
    <PackageVersion Include="Newtonsoft.Json"                               Version="13.0.3" />

    <!-- Excel -->
    <PackageVersion Include="ClosedXML"                                     Version="0.102.3" />
    <PackageVersion Include="DocumentFormat.OpenXml"                        Version="3.1.0" />

    <!-- Logging -->
    <PackageVersion Include="Serilog"                                       Version="4.1.0" />
    <PackageVersion Include="Serilog.Extensions.Hosting"                    Version="8.0.0" />
    <PackageVersion Include="Serilog.AspNetCore"                            Version="8.0.3" />
    <PackageVersion Include="Serilog.Sinks.Console"                         Version="5.0.1" />
    <PackageVersion Include="Serilog.Sinks.AmazonCloudWatch"                Version="3.0.0" />

    <!-- ASP.NET Core -->
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi"                  Version="10.0.0" />
    <PackageVersion Include="Swashbuckle.AspNetCore"                        Version="6.9.0" />

    <!-- Hosting -->
    <PackageVersion Include="Microsoft.Extensions.Hosting"                  Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Hosting.Abstractions"     Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions"     Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />

    <!-- MediatR: Command/Handler + Pipeline Behaviors -->
    <PackageVersion Include="MediatR"                                       Version="14.1.0" />
    <PackageVersion Include="MediatR.Extensions.Microsoft.DependencyInjection" Version="14.1.0" />

    <!-- FluentValidation: Used in ValidationBehavior pipeline -->
    <PackageVersion Include="FluentValidation"                              Version="11.8.0" />
    <PackageVersion Include="FluentValidation.DependencyInjectionExtensions" Version="11.8.0" />

    <!-- EF Core: Migrations for generic tables only -->
    <PackageVersion Include="Microsoft.EntityFrameworkCore"                 Version="10.0.0" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer"       Version="10.0.0" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Tools"           Version="10.0.0" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design"          Version="10.0.0" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.InMemory"        Version="10.0.0" />

    <!-- Configuration -->
    <PackageVersion Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.0" />

    <!-- Testing — xUnit v3 (latest, matches GenericMissingInvoicesProcess) -->
    <PackageVersion Include="xunit.v3"                                      Version="3.2.2" />
    <PackageVersion Include="xunit.runner.visualstudio"                     Version="3.1.4" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk"                        Version="17.14.1" />
    <PackageVersion Include="Moq"                                           Version="4.20.72" />
    <PackageVersion Include="FluentAssertions"                              Version="6.12.2" />
    <PackageVersion Include="FsCheck"                                       Version="3.1.0" />
    <PackageVersion Include="FsCheck.Xunit"                                 Version="3.1.0" />
    <PackageVersion Include="WireMock.Net"                                  Version="1.6.9" />
    <PackageVersion Include="coverlet.collector"                            Version="6.0.4" />
  </ItemGroup>
</Project>
```

---

## 6. AWS Services Used

### 6.1 Compute

| Service | Usage | Config |
|---|---|---|
| **Amazon ECS Fargate** | Runs FeedWorker and PostWorker containers | 2 services, MinCapacity=1, MaxCapacity=5 |
| **AWS Lambda** | Runs Scheduler (EventBridge sync) | rate(10 minutes) trigger, .NET 10 runtime |

### 6.2 Messaging

| Service | Usage | Config |
|---|---|---|
| **Amazon SQS** | Feed queue + Post queue + 2 DLQs | VisibilityTimeout=7200s, Retention=14 days, maxReceive=3 |
| **Amazon EventBridge Scheduler** | Cron triggers per job | One rule per job per schedule type |

### 6.3 Storage

| Service | Usage | Bucket/Path |
|---|---|---|
| **Amazon S3** | Invoice images | `ips-invoice-images/` |
| **Amazon S3** | Feed archives | `ips-feed-archive/{client_type}/{date}/` |
| **Amazon S3** | Output files (CSV, Excel) | `ips-output-files/{client_type}/` |
| **Amazon S3** | Sevita audit JSON | `{post_json_path}/{itemId}_{timestamp}.json` |
| **Amazon S3** | Docker deployment artifacts | `ips-autopost-deployments-{env}/` |

### 6.4 Database

| Service | Usage | Config |
|---|---|---|
| **Amazon RDS SQL Server** | Workflow database | Multi-AZ, existing instance, Max Pool Size=2000 |

### 6.5 Security

| Service | Usage | Paths |
|---|---|---|
| **AWS Secrets Manager** | DB credentials, API keys | `/IPS/Common/{env}/Database/Workflow` |
| **AWS IAM** | ECS Task Execution Role + Task Role | Least-privilege per service |

### 6.6 Observability

| Service | Usage | Config |
|---|---|---|
| **Amazon CloudWatch Logs** | All application logs | `/ips/autopost/feed/{env}`, `/ips/autopost/post/{env}`, 90-day retention |
| **Amazon CloudWatch Metrics** | Custom metrics | Namespace: `IPS/AutoPost/{env}` |
| **Amazon CloudWatch Alarms** | Error alerts, DLQ alerts, scaling triggers | 5-error threshold, DLQ ≥1 |
| **Amazon CloudWatch Dashboard** | Operations view | `IPS-AutoPost-Operations-{env}` |
| **AWS X-Ray** | Distributed tracing | RDS + S3 + ERP API spans |

### 6.7 Container Registry

| Service | Usage | Config |
|---|---|---|
| **Amazon ECR** | Docker image registry | `ecr-ips-autopost-{env}`, ScanOnPush=true, lifecycle 7 days |

---

## 7. Database Tools and Scripts

### 7.1 Database

| Tool | Purpose |
|---|---|
| **SQL Server (Amazon RDS)** | Production database |
| **SQL Server Management Studio (SSMS)** | Local development and script execution |
| **Azure Data Studio** | Alternative SQL editor (cross-platform) |

### 7.2 Migration Strategy — Two-Track Approach

**Track 1: EF Core Migrations (new generic tables)**
The 10 new generic tables are managed by EF Core. Migrations are auto-applied at startup via `context.Database.Migrate()`. This gives proper schema versioning, rollback capability, and team-friendly change tracking.

```
src/IPS.AutoPost.Core/Migrations/
  DatabaseContext.cs                              # EF Core DbContext (generic tables only)
  20260430_InitialGenericTables.cs               # Creates all 10 generic tables
  20260430_InitialGenericTables.Designer.cs
  DatabaseContextModelSnapshot.cs
```

**EF Core CLI commands:**
```bash
# Add a new migration
dotnet ef migrations add AddNewColumn --project src/IPS.AutoPost.Core

# Apply migrations to database
dotnet ef database update --project src/IPS.AutoPost.Core

# Generate SQL script (for DBA review before production)
dotnet ef migrations script --project src/IPS.AutoPost.Core --output db/migrations/ef_migration.sql
```

**Track 2: Raw SQL Scripts (seed data only)**
Seed data for existing clients is managed as plain SQL scripts — no EF Core involvement.

```
db/
  seed/
    011_seed_invitedclub_job_configuration.sql
    012_seed_invitedclub_execution_schedule.sql
    013_seed_sevita_job_configuration.sql
    014_seed_sevita_execution_schedule.sql
```

> **Rule:** EF Core ONLY manages the 10 new generic tables. It NEVER touches existing Workflow tables (`Workitems`, `WFInvitedClubsIndexHeader`, etc.). Those are managed by the existing DBA process.

### 7.3 Connection String

```
Server=ips-rds-database-1.cmrmduasa2gk.us-east-1.rds.amazonaws.com;
Database=Workflow;
User ID=IPSAppsUser;
Password=<from Secrets Manager: /IPS/Common/{env}/Database/Workflow>;
Max Pool Size=2000;
Connect Timeout=30;
TrustServerCertificate=True;
```

> **Note:** Password is stored in AWS Secrets Manager. The connection string in `appsettings.json` contains everything except the password. The password is fetched at runtime from Secrets Manager and injected.

---

## 8. Development Tools

### 8.1 IDE

| Tool | Version | Purpose |
|---|---|---|
| **Visual Studio 2022** | 17.x | Primary IDE (Windows) |
| **Visual Studio Code** | Latest | Alternative / CloudFormation YAML editing |
| **JetBrains Rider** | Latest | Alternative IDE |

### 8.2 .NET Tools

| Tool | Purpose |
|---|---|
| **.NET SDK 10.0** | Build and run all projects |
| **dotnet CLI** | Build, test, publish from command line |
| **dotnet-lambda** | AWS Lambda deployment tool (`dotnet tool install -g Amazon.Lambda.Tools`) |

### 8.3 AWS Tools

| Tool | Purpose |
|---|---|
| **AWS CLI v2** | CloudFormation deployment, ECR login, SQS management |
| **AWS Toolkit for Visual Studio** | Lambda deployment, ECS task management from IDE |
| **AWS SAM CLI** | Local Lambda testing (optional) |

### 8.4 Docker

| Tool | Purpose |
|---|---|
| **Docker Desktop** | Build and test containers locally |
| **Docker CLI** | Build, tag, push images to ECR |

### 8.5 Source Control

| Tool | Purpose |
|---|---|
| **Git** | Version control |
| **GitHub** | Remote repository, CI/CD via GitHub Actions |

### 8.6 Code Quality

| Tool | Purpose |
|---|---|
| **EditorConfig** | Consistent code style across team |
| **Roslyn Analyzers** | Built-in .NET code analysis |
| **SonarQube / SonarCloud** | Optional — code quality scanning |

---

## 9. CI/CD Pipeline Tools

### 9.1 GitHub Actions Workflow

```
.github/workflows/deploy.yml
```

**Trigger:** `workflow_dispatch` (manual) with environment selection (`uat` | `production`)

**Jobs:**
```
infrastructure  →  application  →  monitoring
```

### 9.2 Tools Used in Pipeline

| Tool | Step | Purpose |
|---|---|---|
| **actions/checkout@v4** | All jobs | Checkout source code |
| **aws-actions/configure-aws-credentials@v4** | All jobs | Configure AWS credentials from secrets |
| **aws-actions/amazon-ecr-login@v2** | application job | Login to ECR |
| **docker build** | application job | Build FeedWorker and PostWorker images |
| **docker push** | application job | Push images to ECR |
| **aws cloudformation deploy** | All jobs | Deploy CloudFormation stacks |
| **dotnet build** | (optional pre-deploy) | Build and test before deploy |
| **dotnet test** | (optional pre-deploy) | Run unit tests |

### 9.3 GitHub Secrets Required

| Secret | Value |
|---|---|
| `AWS_ACCESS_KEY_ID` | IAM user access key for CI/CD |
| `AWS_SECRET_ACCESS_KEY` | IAM user secret key for CI/CD |

### 9.4 GitHub Variables Required

| Variable | Example Value |
|---|---|
| `AWS_REGION` | `us-east-1` |
| `VPC_ID` | `vpc-0abc123def456` |
| `DATABASE_SECURITY_GROUP_ID` | `sg-0abc123def456` |
| `ECS_TASK_CPU` | `1024` |
| `ECS_TASK_MEMORY` | `2048` |
| `PUBLIC_SUBNET_CIDR` | `10.0.10.0/24` |
| `PRIVATE_SUBNET_1_CIDR` | `10.0.11.0/24` |
| `PRIVATE_SUBNET_2_CIDR` | `10.0.12.0/24` |

---

## 10. Configuration Files

### 10.1 global.json (pins .NET SDK version)

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestMinor"
  }
}
```

### 10.2 Directory.Build.props (shared MSBuild properties)

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <AnalysisLevel>latest</AnalysisLevel>
    <Company>IPS</Company>
    <Product>IPS.AutoPost.Platform</Product>
    <Copyright>Copyright © IPS 2026</Copyright>
  </PropertyGroup>
</Project>
```

### 10.3 appsettings.json (FeedWorker / PostWorker)

> **Secrets Manager Config-Path Pattern:** Values starting with `/` in `ConnectionStrings` are treated as Secrets Manager paths and fetched at startup. No special code needed in services — they just use `IConfiguration` normally.

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] [{ClientType}] [{JobId}] {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "AmazonCloudWatch",
        "Args": {
          "logGroup": "/ips/autopost/post/production",
          "region": "us-east-1",
          "restrictedToMinimumLevel": "Information",
          "logStreamPrefix": "app"
        }
      }
    ],
    "Enrich": ["FromLogContext"]
  },
  "AWS": {
    "Region": "us-east-1"
  },
  "ConnectionStrings": {
    "Workflow": "/IPS/Common/production/Database/Workflow"
  },
  "Email": {
    "SmtpPassword": "/IPS/Common/production/Smtp"
  },
  "ApiKey": {
    "Value": "/IPS/Common/production/ApiKey"
  }
}
```

> **How the Secrets Manager pattern works:**
> 1. `appsettings.json` has `"Workflow": "/IPS/Common/production/Database/Workflow"`, `"SmtpPassword": "/IPS/Common/production/Smtp"`, `"Value": "/IPS/Common/production/ApiKey"` — paths, not real values
> 2. `Program.cs` calls `await builder.Configuration.AddSecretsManagerAsync()`
> 3. `SecretsManagerConfigurationProvider` scans `ConnectionStrings`, `Email:SmtpPassword`, and `ApiKey:Value`, finds values starting with `/`, fetches them from Secrets Manager in parallel (30s timeout)
> 4. Handles JSON secrets: if secret body is JSON with `AppConnectionString` key, extracts that value (for RDS-managed connection string secrets)
> 5. The real values replace the paths in `IConfiguration` via `AddInMemoryCollection`
> 6. `DatabaseContext`, `SqlHelper`, `EmailService`, and `ApiKeyAuthMiddleware` read their respective config keys normally — they never know about Secrets Manager
> 7. Plugin-specific credentials (`/IPS/InvitedClub/{env}/PostAuth`, `/IPS/Sevita/{env}/PostAuth`) are fetched on-demand by each plugin via `ConfigurationService.GetSecretAsync()` — not at startup

### 10.4 appsettings.json (Api)

```json
{
  "Serilog": {
    "MinimumLevel": { "Default": "Information" },
    "WriteTo": [{ "Name": "Console" }]
  },
  "AWS": { "Region": "us-east-1" },
  "ApiKey": {
    "HeaderName": "x-api-key",
    "Value": "/IPS/Common/production/ApiKey"
  },
  "ConnectionStrings": {
    "Workflow": "/IPS/Common/production/Database/Workflow"
  },
  "Email": {
    "SmtpPassword": "/IPS/Common/production/Smtp"
  }
}
```

### 10.5 aws-lambda-tools-defaults.json (Scheduler)

```json
{
  "profile": "",
  "region": "us-east-1",
  "configuration": "Release",
  "framework": "net10.0",
  "function-runtime": "dotnet10",
  "function-memory-size": 256,
  "function-timeout": 30,
  "function-handler": "IPS.AutoPost.Scheduler::IPS.AutoPost.Scheduler.Function::FunctionHandler"
}
```

### 10.6 .editorconfig

```ini
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = crlf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

[*.{yaml,yml}]
indent_size = 2

[*.json]
indent_size = 2
```

### 10.7 .gitignore

```
# .NET
bin/
obj/
*.user
*.suo
.vs/
*.DotSettings.user

# Secrets (never commit)
appsettings.*.json
!appsettings.json
!appsettings.Development.json
secrets.json
*.pfx

# Docker
.dockerignore

# AWS
.aws/

# Test results
TestResults/
coverage/
```

---

## 11. Project Dependencies Map

```
IPS.AutoPost.Platform.sln
│
├── IPS.AutoPost.Core                    (no project references)
│   └── Packages: AWSSDK.S3, AWSSDK.SecretsManager, AWSSDK.CloudWatch,
│                 Microsoft.Data.SqlClient, Serilog, Microsoft.Extensions.*,
│                 MediatR, FluentValidation, EF Core (generic tables only)
│
├── IPS.AutoPost.Plugins
│   └── References: IPS.AutoPost.Core
│   └── Packages: RestSharp, Newtonsoft.Json, ClosedXML, AWSSDK.S3
│
├── IPS.AutoPost.Host.FeedWorker
│   └── References: IPS.AutoPost.Core, IPS.AutoPost.Plugins
│   └── Packages: AWSSDK.SQS, Microsoft.Extensions.Hosting, Serilog
│   └── Pattern: SQS BackgroundService, MaxNumberOfMessages=10, scoped DI per message
│
├── IPS.AutoPost.Host.PostWorker
│   └── References: IPS.AutoPost.Core, IPS.AutoPost.Plugins
│   └── Packages: AWSSDK.SQS, Microsoft.Extensions.Hosting, Serilog
│   └── Pattern: SQS BackgroundService, MaxNumberOfMessages=10, scoped DI per message
│
├── IPS.AutoPost.Api
│   └── References: IPS.AutoPost.Core, IPS.AutoPost.Plugins
│   └── Packages: Swashbuckle.AspNetCore, Serilog.AspNetCore
│
├── IPS.AutoPost.Scheduler
│   └── References: IPS.AutoPost.Core
│   └── Packages: Amazon.Lambda.Core, AWSSDK.EventBridge, AWSSDK.Scheduler
│
├── IPS.AutoPost.Core.Tests
│   └── References: IPS.AutoPost.Core
│   └── Packages: xunit.v3, Moq, FluentAssertions, FsCheck, EF Core InMemory
│
└── IPS.AutoPost.Plugins.Tests
    └── References: IPS.AutoPost.Core, IPS.AutoPost.Plugins
    └── Packages: xunit.v3, Moq, FluentAssertions, FsCheck, WireMock.Net, EF Core InMemory
```

### Dependency Rule

```
Core  ←  Plugins  ←  FeedWorker
                  ←  PostWorker
                  ←  Api
Core  ←  Scheduler

Core.Tests  ←  Core
Plugins.Tests  ←  Core + Plugins
```

**Core has zero project references** — it only depends on NuGet packages. This ensures the generic engine is completely independent of any client-specific code.

---

## 12. Architecture Patterns — Adopted from GenericMissingInvoicesProcess

The following patterns are adopted from the production `GenericMissingInvoicesProcess` project and applied to IPS.AutoPost Platform:

### 12.1 MediatR Command/Handler Pattern

```csharp
// SQS consumer sends a command — completely decoupled from business logic
public class ExecutePostCommand : IRequest<PostBatchResult>
{
    public int JobId { get; set; }
    public string ClientType { get; set; } = string.Empty;
    public string TriggerType { get; set; } = "Scheduled";
    public string ItemIds { get; set; } = string.Empty;
    public int UserId { get; set; }
}

// Handler contains all business logic
public class ExecutePostHandler : IRequestHandler<ExecutePostCommand, PostBatchResult>
{
    public async Task<PostBatchResult> Handle(ExecutePostCommand request, CancellationToken ct)
    {
        return await _orchestrator.RunScheduledPostAsync(request.JobId, request.ClientType, ct);
    }
}

// PostWorker — just sends the command, knows nothing about business logic
private async Task ProcessMessageAsync(Message message)
{
    using var scope = _serviceProvider.CreateScope();
    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
    var correlationService = scope.ServiceProvider.GetRequiredService<ICorrelationIdService>();

    using (correlationService.SetCorrelationId(Guid.NewGuid().ToString()))
    {
        var command = JsonSerializer.Deserialize<ExecutePostCommand>(message.Body)!;
        await mediator.Send(command);
    }
}
```

### 12.2 MediatR Pipeline Behaviors

```csharp
// Registered once — wraps EVERY command automatically
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// LoggingBehavior — logs every command with CorrelationId
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        _logger.LogInformation("Handling {RequestName}", typeof(TRequest).Name);
        var response = await next();
        _logger.LogInformation("Handled {RequestName}", typeof(TRequest).Name);
        return response;
    }
}

// ValidationBehavior — runs FluentValidation before handler
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var context = new ValidationContext<TRequest>(request);
        var failures = _validators.SelectMany(v => v.Validate(context).Errors).ToList();
        if (failures.Any()) throw new ValidationException(failures);
        return await next();
    }
}
```

### 12.3 SecretsManagerConfigurationProvider — Config-Path Pattern

```csharp
// Program.cs — one line, replaces all manual Secrets Manager calls
await builder.Configuration.AddSecretsManagerAsync();

// appsettings.json — prefix with "/" to mark as secret path
{
  "ConnectionStrings": {
    "Workflow": "/IPS/Common/production/Database/Workflow"
  },
  "Email": {
    "SmtpPassword": "/IPS/Common/production/Smtp"
  },
  "ApiKey": {
    "Value": "/IPS/Common/production/ApiKey"
  }
}

// SecretsManagerConfigurationProvider scans:
//   - ConnectionStrings (all children)
//   - Email:SmtpPassword
//   - ApiKey:Value
// Finds "/" prefixed values, fetches from Secrets Manager in parallel (30s timeout),
// handles JSON secrets (extracts AppConnectionString key for RDS-managed secrets),
// injects real values back into IConfiguration via AddInMemoryCollection.
// All other code reads IConfiguration normally — no Secrets Manager SDK needed.
//
// Plugin-specific credentials (/IPS/InvitedClub/{env}/PostAuth, /IPS/Sevita/{env}/PostAuth)
// are NOT scanned at startup — fetched on-demand by each plugin via ConfigurationService.GetSecretAsync()
//
// Credential provider chain (automatic):
//   - IAM role for ECS tasks (production — no config needed)
//   - AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY env vars (local dev)
//   - ~/.aws/credentials file (developer machines)
```

### 12.4 CorrelationIdService with AsyncLocal

```csharp
public class CorrelationIdService : ICorrelationIdService
{
    private static readonly AsyncLocal<string> _correlationId = new();

    public string GetOrCreateCorrelationId()
        => _correlationId.Value ??= Guid.NewGuid().ToString();

    public IDisposable SetCorrelationId(string correlationId)
    {
        _correlationId.Value = correlationId;
        return LogContext.PushProperty("CorrelationId", correlationId);
    }
}

// Usage: every SQS message gets its own correlation ID
// All log entries for that message automatically include [{CorrelationId}]
// Searchable in CloudWatch Logs Insights: filter @message like /abc-123-def/
```

### 12.5 SQS Consumer — MaxNumberOfMessages=10 + Scoped DI per Message

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        var response = await _sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
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
                await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
            }
            catch (Exception ex)
            {
                // Log but do NOT delete — SQS will retry up to maxReceiveCount
                _logger.LogError(ex, "Failed to process message {MessageId}", message.MessageId);
            }
        }
    }
}
```

### 12.6 Mode Override via SQS Message

```csharp
// SQS message can carry a Mode field to override behavior at runtime
public class ExecutePostCommand : IRequest<PostBatchResult>
{
    public int JobId { get; set; }
    public string ClientType { get; set; } = string.Empty;
    public string TriggerType { get; set; } = "Scheduled";
    public string? Mode { get; set; }  // Optional: "UAT", "DryRun", "Manual"
}

// In handler: Mode can override email API identifier, skip actual posting, etc.
// No redeployment needed to change behavior — just send a different SQS message.
```

### 12.7 EF Core Migrations — Generic Tables Only

```csharp
// DatabaseContext.cs — ONLY the 10 new generic tables
public class AutoPostDatabaseContext : DbContext
{
    public DbSet<GenericJobConfiguration> JobConfigurations { get; set; }
    public DbSet<GenericExecutionSchedule> ExecutionSchedules { get; set; }
    public DbSet<GenericPostHistory> PostHistories { get; set; }
    public DbSet<GenericExecutionHistory> ExecutionHistories { get; set; }
    // ... other generic tables
    // NOTE: No DbSet for Workitems, WFInvitedClubsIndexHeader, etc.
    // Those are accessed via SqlHelper only.
}

// Program.cs — auto-apply migrations at startup
using (var scope = host.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AutoPostDatabaseContext>();
    context.Database.Migrate();  // Creates tables if not exist, applies pending migrations
}
```

### 12.8 Integration Tests with xUnit v3 + EF Core InMemory

```csharp
// Fast integration tests — no real database needed
public class ExecutePostHandlerTests
{
    private readonly AutoPostDatabaseContext _context;
    private readonly ExecutePostHandler _handler;

    public ExecutePostHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AutoPostDatabaseContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new AutoPostDatabaseContext(options);

        // Seed test data
        _context.JobConfigurations.Add(new GenericJobConfiguration
        {
            ClientType = "INVITEDCLUB", JobId = 371, IsActive = true
        });
        _context.SaveChanges();

        _handler = new ExecutePostHandler(/* inject mocks */);
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccess()
    {
        var command = new ExecutePostCommand { JobId = 371, ClientType = "INVITEDCLUB" };
        var result = await _handler.Handle(command, CancellationToken.None);
        Assert.True(result.Success);
    }
}
```

---

## 12. Architecture Patterns — Adopted from GenericMissingInvoicesProcess

The following patterns are adopted from the production `GenericMissingInvoicesProcess` project:

### 12.1 MediatR Command/Handler Pattern

```csharp
// Command — just data, no logic
public class ExecutePostCommand : IRequest<PostBatchResult>
{
    public int JobId { get; set; }
    public string ClientType { get; set; } = string.Empty;
    public string TriggerType { get; set; } = "Scheduled";
    public string ItemIds { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string? Mode { get; set; }  // Optional runtime override
}

// Handler — all business logic here
public class ExecutePostHandler : IRequestHandler<ExecutePostCommand, PostBatchResult>
{
    public async Task<PostBatchResult> Handle(ExecutePostCommand request, CancellationToken ct)
        => await _orchestrator.RunScheduledPostAsync(request.JobId, request.ClientType, ct);
}
```

**Why:** SQS consumer just calls `mediator.Send(command)`. Handler does the work. Completely decoupled.

### 12.2 MediatR Pipeline Behaviors

```csharp
// Registered once — wraps EVERY command automatically
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

- `LoggingBehavior` — logs every command start/end with CorrelationId, no handler changes needed
- `ValidationBehavior` — runs FluentValidation before handler, throws on failure

### 12.3 SecretsManagerConfigurationProvider — Config-Path Pattern

```json
// appsettings.json — "/" prefix = fetch from Secrets Manager at startup
{
  "ConnectionStrings": {
    "Workflow": "/IPS/Common/production/Database/Workflow"
  }
}
```

```csharp
// Program.cs — one line replaces all manual Secrets Manager calls
await builder.Configuration.AddSecretsManagerAsync();
// SecretsManagerConfigurationProvider scans ConnectionStrings,
// fetches "/" prefixed values, injects real values into IConfiguration.
// All other code reads IConfiguration normally.
```

### 12.4 CorrelationIdService with AsyncLocal

```csharp
public class CorrelationIdService : ICorrelationIdService
{
    private static readonly AsyncLocal<string> _correlationId = new();

    public IDisposable SetCorrelationId(string id)
    {
        _correlationId.Value = id;
        return LogContext.PushProperty("CorrelationId", id);  // Serilog
    }
}
// Every log entry for a SQS message includes [{CorrelationId}] automatically.
// Searchable in CloudWatch Logs Insights: filter @message like /abc-123/
```

### 12.5 SQS Consumer — MaxNumberOfMessages=10 + Scoped DI per Message

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        var response = await _sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = _queueUrl,
            MaxNumberOfMessages = 10,   // Process up to 10 at once (was 1)
            WaitTimeSeconds = 20        // Long polling
        }, stoppingToken);

        foreach (var message in response.Messages)
        {
            try
            {
                // NEW SCOPE per message — prevents state leakage between messages
                using var scope = _serviceProvider.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var correlationSvc = scope.ServiceProvider.GetRequiredService<ICorrelationIdService>();

                using (correlationSvc.SetCorrelationId(Guid.NewGuid().ToString()))
                {
                    var command = JsonSerializer.Deserialize<ExecutePostCommand>(message.Body)!;
                    await mediator.Send(command, stoppingToken);
                }

                // Delete ONLY after successful processing
                await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
            }
            catch (Exception ex)
            {
                // Log but do NOT delete — SQS retries up to maxReceiveCount then DLQ
                _logger.LogError(ex, "Failed to process message {MessageId}", message.MessageId);
            }
        }
    }
}
```

### 12.6 Mode Override via SQS Message

```csharp
// SQS message carries optional Mode to override runtime behavior
// No redeployment needed — just send a different message
public class ExecutePostCommand : IRequest<PostBatchResult>
{
    public string? Mode { get; set; }  // "DryRun", "Manual", "UAT"
}
// Handler checks Mode and adjusts behavior (skip email, skip actual post, etc.)
```

### 12.7 EF Core Migrations — Generic Tables Only

```csharp
// DatabaseContext — ONLY the 10 new generic tables
public class AutoPostDatabaseContext : DbContext
{
    public DbSet<GenericJobConfiguration> JobConfigurations { get; set; }
    public DbSet<GenericExecutionSchedule> ExecutionSchedules { get; set; }
    public DbSet<GenericPostHistory> PostHistories { get; set; }
    // ... 7 more generic tables
    // NO DbSet for Workitems, WFInvitedClubsIndexHeader — those use SqlHelper
}

// Program.cs — auto-apply at startup
context.Database.Migrate();
```

**EF Core CLI:**
```bash
dotnet ef migrations add AddNewColumn --project src/IPS.AutoPost.Core
dotnet ef database update --project src/IPS.AutoPost.Core
dotnet ef migrations script --output db/migrations/ef_migration.sql
```

### 12.8 Integration Tests with xUnit v3 + EF Core InMemory

```csharp
// Fast tests — no real database, no network
public class ExecutePostHandlerTests
{
    private readonly AutoPostDatabaseContext _context;

    public ExecutePostHandlerTests()
    {
        var options = new DbContextOptionsBuilder<AutoPostDatabaseContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())  // Fresh DB per test
            .Options;
        _context = new AutoPostDatabaseContext(options);
        _context.JobConfigurations.Add(new GenericJobConfiguration
            { ClientType = "INVITEDCLUB", JobId = 371, IsActive = true });
        _context.SaveChanges();
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccess()
    {
        var command = new ExecutePostCommand { JobId = 371, ClientType = "INVITEDCLUB" };
        var result = await _handler.Handle(command, CancellationToken.None);
        Assert.True(result.Success);
    }
}
```

---

## Quick Start — Build and Run Locally

```bash
# 1. Clone the repository
git clone https://github.com/IPS/ips-autopost-platform.git
cd ips-autopost-platform

# 2. Restore packages
dotnet restore IPS.AutoPost.Platform.sln

# 3. Build all projects
dotnet build IPS.AutoPost.Platform.sln -c Release

# 4. Run all tests
dotnet test IPS.AutoPost.Platform.sln --configuration Release

# 5. Apply EF Core migrations (generic tables)
dotnet ef database update --project src/IPS.AutoPost.Core

# 6. Run PostWorker locally
cd src/IPS.AutoPost.Host.PostWorker
dotnet run

# 7. Run Api locally
cd src/IPS.AutoPost.Api
dotnet run
# API: https://localhost:5001/swagger

# 8. Build Docker images locally
docker build -f infra/docker/Dockerfile.PostWorker -t ips-autopost-post:local .
docker build -f infra/docker/Dockerfile.FeedWorker -t ips-autopost-feed:local .

# 9. Deploy to UAT
# Trigger GitHub Actions workflow_dispatch with environment=uat
```

---

*Document Version: 2.0 — Updated with GenericMissingInvoicesProcess patterns*
*IPS.AutoPost Platform — Complete Solution Structure*
*Date: April 30, 2026*














### 12.7 EF Core Migrations — Generic Tables Only

```csharp
// DatabaseContext.cs — ONLY the 10 new generic tables
public class AutoPostDatabaseContext : DbContext
{
    public DbSet<GenericJobConfiguration> JobConfigurations { get; set; }
    public DbSet<GenericExecutionSchedule> ExecutionSchedules { get; set; }
    public DbSet<GenericPostHistory> PostHistories { get; set; }
    public DbSet<GenericExecutionHistory> ExecutionHistories { get; set; }
    // ... other generic tables
    // NOTE: No DbSet for Workitems, WFInvitedClubsIndexHeader, etc.
    // Those are accessed via SqlHelper only.
}

// Program.cs — auto-apply migrations at startup
using (var scope = host.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AutoPostDatabaseContext>();
    context.Database.Migrate();  // Creates tables if not exist, applies pending migrations
}
```

### 12.8 Integration Tests with xUnit v3 + EF Core InMemory

```csharp
// Fast integration tests — no real database needed
public class ExecutePostHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessResult()
    {
        // Arrange: create in-memory database with seeded config
        var options = new DbContextOptionsBuilder<AutoPostDatabaseContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())  // Fresh DB per test
            .Options;

        using var context = new AutoPostDatabaseContext(options);
        context.JobConfigurations.Add(new GenericJobConfiguration
        {
            Id = 1,
            JobId = 371,
            ClientType = "INVITEDCLUB",
            IsActive = true,
            // ... other fields
        });
        await context.SaveChangesAsync();

        var handler = new ExecutePostHandler(/* inject mocked dependencies */);
        var command = new ExecutePostCommand { JobId = 371, ClientType = "INVITEDCLUB" };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.RecordsProcessed.Should().BeGreaterThan(0);
    }
}
```

### 12.9 SecretsManagerConfigurationProvider — Complete Implementation

```csharp
// IPS.AutoPost.Core/Infrastructure/SecretsManagerConfigurationProvider.cs
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace IPS.AutoPost.Core.Infrastructure;

/// <summary>
/// Config-path pattern: values starting with "/" in appsettings.json are fetched from AWS Secrets Manager.
/// 
/// Usage in Program.cs:
/// <code>
/// var builder = WebApplication.CreateBuilder(args);
/// await builder.Configuration.AddSecretsManagerAsync();
/// </code>
/// 
/// In appsettings.json:
/// <code>
/// {
///   "ConnectionStrings": { "Workflow": "/IPS/Common/production/Database/Workflow" },
///   "Email": { "SmtpPassword": "/IPS/Common/production/Smtp" },
///   "ApiKey": { "Value": "/IPS/Common/production/ApiKey" }
/// }
/// </code>
/// 
/// The provider scans ConnectionStrings, Email:SmtpPassword, and ApiKey:Value.
/// Values starting with "/" are fetched from Secrets Manager in parallel (30s timeout).
/// JSON secrets with "AppConnectionString" key are extracted (for RDS-managed secrets).
/// Real values replace the paths in IConfiguration via AddInMemoryCollection.
/// </summary>
public static class SecretsManagerExtensions
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Fetches secrets from AWS Secrets Manager and adds them to configuration.
    /// Values starting with "/" are treated as secret ARN/paths and will be fetched.
    /// </summary>
    public static async Task AddSecretsManagerAsync(
        this IConfigurationBuilder builder,
        TimeSpan? timeout = null)
    {
        var secretsManager = CreateSecretsManagerClient();
        var currentConfig = builder.Build();
        var secretMappings = FindSecretMappings(currentConfig);

        if (secretMappings.Count == 0)
        {
            return;
        }

        var secrets = await FetchSecretsAsync(secretsManager, secretMappings, timeout ?? DefaultTimeout);
        
        // Add secrets as in-memory configuration (highest priority)
        builder.AddInMemoryCollection(secrets);
    }

    private static IAmazonSecretsManager CreateSecretsManagerClient()
    {
        var region = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION") 
            ?? Environment.GetEnvironmentVariable("AWS_REGION") 
            ?? "us-east-1";

        // Use default credential provider chain - works with:
        // - IAM roles for ECS tasks (recommended for production)
        // - Environment variables (AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY)
        // - IAM instance profiles for EC2
        // - AWS credentials file (~/.aws/credentials)
        return new AmazonSecretsManagerClient(Amazon.RegionEndpoint.GetBySystemName(region));
    }

    private static Dictionary<string, string> FindSecretMappings(IConfiguration config)
    {
        var mappings = new Dictionary<string, string>();

        // Scan ConnectionStrings
        var connectionStrings = config.GetSection("ConnectionStrings");
        foreach (var item in connectionStrings.GetChildren())
        {
            if (item.Value?.StartsWith("/") == true)
            {
                mappings[$"ConnectionStrings:{item.Key}"] = item.Value;
            }
        }

        // Scan Email:SmtpPassword
        var smtpPassword = config["Email:SmtpPassword"];
        if (smtpPassword?.StartsWith("/") == true)
        {
            mappings["Email:SmtpPassword"] = smtpPassword;
        }

        // Scan ApiKey:Value
        var apiKeyValue = config["ApiKey:Value"];
        if (apiKeyValue?.StartsWith("/") == true)
        {
            mappings["ApiKey:Value"] = apiKeyValue;
        }

        // Plugin-specific credentials (/IPS/InvitedClub/{env}/PostAuth, /IPS/Sevita/{env}/PostAuth)
        // are NOT scanned here — they are fetched on-demand by each plugin via ConfigurationService.GetSecretAsync()

        return mappings;
    }

    private static async Task<Dictionary<string, string?>> FetchSecretsAsync(
        IAmazonSecretsManager client,
        Dictionary<string, string> mappings,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var results = new Dictionary<string, string?>();

        var tasks = mappings.Select(async kvp =>
        {
            var value = await GetSecretValueAsync(client, kvp.Value, cts.Token);
            return (ConfigKey: kvp.Key, Value: value);
        });

        try
        {
            var completed = await Task.WhenAll(tasks);
            foreach (var (configKey, value) in completed)
            {
                results[configKey] = value;
            }
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Secrets Manager request timed out after {timeout.TotalSeconds}s");
        }

        return results;
    }

    private static async Task<string> GetSecretValueAsync(
        IAmazonSecretsManager client,
        string secretId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetSecretValueAsync(
                new GetSecretValueRequest { SecretId = secretId },
                cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(response.SecretString))
            {
                throw new InvalidOperationException($"Secret '{secretId}' is empty");
            }

            // Handle JSON secrets (e.g., RDS secrets with username/password)
            if (response.SecretString.TrimStart().StartsWith("{"))
            {
                using var doc = JsonDocument.Parse(response.SecretString);
                
                // Common patterns for connection strings in AWS secrets
                if (doc.RootElement.TryGetProperty("AppConnectionString", out var connStr))
                {
                    return connStr.GetString() 
                        ?? throw new InvalidOperationException($"Secret '{secretId}' has null AppConnectionString");
                }
                
                // Could add more patterns here (e.g., build connection string from host/user/password)
            }

            return response.SecretString;
        }
        catch (ResourceNotFoundException)
        {
            throw new InvalidOperationException($"Secret '{secretId}' not found in Secrets Manager");
        }
    }
}
```

---

## Summary

This document provides the complete solution structure for the IPS.AutoPost Platform, including:

- **8 .NET 10 projects** with exact package references
- **Complete folder structure** with Commands/, Handlers/, Behaviors/, Infrastructure/, Migrations/
- **Technology stack** with MediatR, EF Core, xUnit v3, CorrelationId, CloudWatch metrics, DynamicRecord, SecretsManager config-path pattern
- **12 architecture patterns** adopted from GenericMissingInvoicesProcess production code
- **AWS infrastructure** with 3-stack CloudFormation deployment
- **CI/CD pipeline** with GitHub Actions
- **Docker multi-stage builds** for FeedWorker and PostWorker
- **Database migration strategy** (EF Core for generic tables, raw SQL for existing tables)
- **Configuration patterns** with Secrets Manager integration

All patterns are production-proven and ready for implementation.
