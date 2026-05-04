# Add Alert Feature - Deep Analysis

## Table of Contents
1. [Overview](#overview)
2. [UI Form Fields](#ui-form-fields)
3. [Frontend Architecture](#frontend-architecture)
4. [Backend Architecture](#backend-architecture)
5. [API Endpoints](#api-endpoints)
6. [Database Tables](#database-tables)
7. [Stored Procedures & SQL](#stored-procedures--sql)
8. [End-to-End Flow: Add Alert](#end-to-end-flow-add-alert)
9. [Flow Diagrams](#flow-diagrams)
10. [File Reference Map](#file-reference-map)

---

## Overview

The **Add Alert** feature allows authenticated users to create configurable alerts that monitor document queues based on custom criteria. When conditions are met, the system notifies recipients via email (Hourly, Daily Digest) or surfaces the alert online within the platform.

**Tech Stack:**
- Frontend: React (class components), Redux, Axios, Lodash
- Backend: C# .NET, Dapper (stored procedures), CQRS pattern
- Database: SQL Server (stored procedures + inline SQL)

---

## UI Form Fields

The "ADD ALERT" modal (`platform.ipswx.com`) contains the following fields:

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| Alert Title | Text Input | Yes | Max 31 chars, must be unique per user, no `/ ? : * ] [` chars, cannot start/end with `'` |
| Job | Dropdown | Yes | Populated from `UsersJobs` + `Jobs` tables |
| Queues | Multi-select Dropdown | Yes | At least 1 required; loaded per selected Job |
| Share With | Multi-select Dropdown | No | Users who share the same queues |
| Not Touched Days | Range Select (operator + number) | No | Operators: `<`, `<=`, `=`, `>=`, `>` |
| Overall Age | Range Select (operator + number) | No | Operators: `<`, `<=`, `=`, `>=`, `>` |
| Queue Age | Range Select (operator + number) | No | Operators: `<`, `<=`, `=`, `>=`, `>` |
| Dynamic Fields | Varies (text, dropdown, date-range, amount-range, predictive) | No | Job-specific fields from `GetAlertFieldsByJob` SP |
| Delivery Method | Dropdown | Yes | Hourly / Daily Digest / Online |
| Delivery Method Option | Dropdown | Conditional | Only shown when Delivery Method = "Daily Digest"; values: Morning / Afternoon / Evening |
| Recipients | Tag Input (emails) | Conditional | Required if Delivery Method != Online |
| Recipients CC | Tag Input (emails) | No | Disabled if Delivery Method = Online |
| Notification Options | Dropdown | Conditional | Required if Delivery Method != Online; Full Report Embedded / Full Report Attached |
| Email Body | Textarea | No | Disabled if Delivery Method = Online |

---

## Frontend Architecture

### Component Tree

```
Alerts (src/components/Alerts/index.jsx)
├── AlertsMainTable/          - Paginated list of existing alerts
├── AlertsHistoryModal/       - Alert history viewer
├── AlertsCreateModal/        - ADD ALERT modal (this feature)
│   ├── index.jsx             - Modal wrapper, validation trigger, submit handler
│   ├── CreateAlertForm.jsx   - All form fields, dynamic field rendering
│   ├── LabelInlineInput.jsx  - Reusable label+input wrapper
│   └── dymanicFields/
│       ├── AmountRange.jsx
│       ├── DateRange.jsx
│       └── AlertsPredictiveInput.jsx
└── AlertsEditModal/          - EDIT ALERT modal (reuses CreateAlertForm)
```

### Redux State Shape (alerts slice)

```
state.alerts
├── alertsCountGlobal         - Total alert count for current user
├── createAlertModalOpen      - Boolean: ADD modal visibility
├── editAlertModalOpen        - Boolean: EDIT modal visibility
├── createAlertJobs           - Array: available jobs for dropdown
├── createAlertQueues         - Array: queues for selected job
├── shareWithOptions          - Array: users available to share with
├── createAlertFieldsOptions  - Static options: deliveryMethod, notificationMethod
└── createAlertValues         - Form state object:
    ├── alertID               - 0 for new, existing ID for edit
    ├── title                 - { value, isRequired, isValid, validationMessage }
    ├── jobID                 - { value, isRequired, isValid }
    ├── queueIDs              - { value: [], isRequired, isValid }
    ├── shareWith             - { value: [] }
    ├── dynamicFields         - Array of job-specific field objects
    ├── notTouchedDays        - { value, operator }
    ├── overallAge            - { value, operator }
    ├── queueAge              - { value, operator }
    ├── deliveryMethod        - string
    ├── deliveryMethodOption  - string
    ├── recipients            - string[]
    ├── recipientsCC          - string[]
    ├── notificationOption    - string
    └── emailBody             - string
```

### Redux Actions (src/redux/actions/alerts/index.js)

| Action | Type | Description |
|--------|------|-------------|
| `changeAlertsCreateModalDisplay()` | Thunk | Resets form state, toggles modal open/close |
| `loadAlertsJobs()` | Async Thunk | GET `/api/v2/alerts/jobs` → populates job dropdown |
| `setAlertJob(jobID)` | Thunk | Sets selected job, triggers `loadAlertsQueues()` |
| `loadAlertsQueues(jobID)` | Async Thunk | GET `/api/v2/alerts/queues?jobId=X` → populates queue dropdown, then calls `loadFormFields()` |
| `setAlertQueues(queueIDs)` | Thunk | Sets selected queues, triggers `loadShareWithOptions()` |
| `loadShareWithOptions(queueIDs)` | Async Thunk | POST `/api/v2/alerts/sharedUsers` → populates Share With dropdown |
| `loadFormFields(jobIDs)` | Async Thunk | GET `/api/v2/alerts/fields?JobId=X` → populates dynamic fields |
| `validateAlertTitleDebounced(title)` | Debounced Thunk | Local validation first, then GET `/api/v2/alerts/checkIfTitleExists?title=X` |
| `rangeFieldsChange(value, key)` | Action | Updates notTouchedDays / overallAge / queueAge |
| `dynamicFieldValueChange(value, fieldID)` | Action | Updates a specific dynamic field value |
| `recipientsChange(emails)` | Action | Updates recipients To array |
| `recipientsCCChange(emails)` | Action | Updates recipients CC array |
| `deliveryMethodChange(method)` | Action | Updates delivery method |
| `deliveryMethodOptionChange(option)` | Action | Updates delivery method option |
| `notificationOptionChange(option)` | Action | Updates notification option |
| `emailBodyChange(body)` | Action | Updates email body |
| `saveAlert()` | Async Thunk | POST `/api/v2/alerts` → creates alert, refreshes list |
| `getAlerts()` | Async Thunk | GET `/api/v2/alerts` → refreshes alert list |
| `getAlertsCount()` | Async Thunk | GET `/api/v2/alerts/count` → refreshes counter |

### Frontend Validation (src/components/Alerts/utils.js + actions/alerts/index.js)

**Client-side (immediate):**
- Title not empty
- Title does not start/end with `'`
- Title length <= 31 characters
- Title does not contain `/ ? : * ] [`

**Server-side (debounced, 1000ms):**
- Title uniqueness check per user via `CheckIfAlertTitleExistsByUserId` SP

**On Submit (`validateAlertPayload`):**
- Title valid
- Job selected
- At least 1 queue selected
- Delivery method selected
- If delivery method != Online: recipients required, notification option required
- If delivery method = Daily Digest: delivery method option required
- At least one range filter set (notTouchedDays, overallAge, or queueAge)

### Payload Structure Sent to Backend

```json
{
  "Alert": {
    "AlertId": 0,
    "Title": "My Alert",
    "JobId": 123,
    "QueueIds": ["456", "789"],
    "SharedUserIds": ["101", "102"],
    "DeliveryMethod": "Daily Digest",
    "DeliveryMethodOption": "Morning",
    "NotTouchDays": "5",
    "NotTouchDaysOperator": ">=",
    "OverallAge": "10",
    "AgeOperator": ">=",
    "QueueAge": "",
    "QueueAgeOperator": "",
    "RecipientsTo": ["user@example.com"],
    "RecipientsCC": [],
    "NotificationOption": "Full Report Attached",
    "EmailBody": "Please review the following items."
  },
  "FieldDetails": [
    {
      "FieldId": 708,
      "FieldName": "CurrentReceiverUser",
      "Value1": ["John Smith"],
      "Value2": []
    }
  ]
}
```

---

## Backend Architecture

### Project Structure

```
ModernUI/src/
├── IPS.WebAPI/
│   └── Controllers/V2/
│       └── AlertsController.cs       - REST API controller (18 endpoints)
└── IPS.AlertsService/
    ├── Services/
    │   ├── IAlertService.cs          - Service interface
    │   └── AlertService.cs           - Business logic orchestrator
    ├── Commands/
    │   ├── CreateAlertCommand.cs     - Command model for create/update
    │   ├── CreateAlertCommandHandler.cs  - Executes SaveUpdateAlerts SP
    │   ├── DeleteAlertCommand.cs     - Command model for delete
    │   └── DeleteAlertCommandHandler.cs  - Executes DeleteUserAlertById SP
    ├── Queries/                      - 21 query handler files (see below)
    └── Models/
        ├── AlertBase.cs              - { AlertId, Title }
        ├── Alert.cs                  - Full alert model (extends AlertBase)
        ├── SaveAlertRequest.cs       - API request: { Alert, FieldDetails[] }
        ├── AlertDetails.cs           - Frequency/recipient details
        ├── AlertDetailsShort.cs      - List view: { AlertId, Title, TotalDocuments, Owner }
        ├── AlertEditDetailsResponse.cs
        ├── AlertDetailsResult.cs     - Extended details for edit modal
        ├── AlertConfig.cs            - Field filter configuration
        ├── AlertJob.cs               - { JobId, JobName }
        ├── AlertQueue.cs             - { QueueId, QueueName }
        ├── AlertField.cs             - Dynamic field definition
        ├── AlertFieldResult.cs       - { AlertFields[], AlertFieldCount, EmbeddedColumns }
        ├── AlertSharedUser.cs        - { UserId, UserName }
        ├── AlertsHistory.cs          - History list item
        ├── AlertHistory.cs           - History detail item
        ├── FieldDetailsRequest.cs    - { FieldId, FieldName, Value1[], Value2[] }
        └── PredictiveInputOptionsRequest.cs
```

### Alert Model (C#)

```csharp
// AlertBase
public int AlertId { get; set; }
public string Title { get; set; }

// Alert : AlertBase
public int JobId { get; set; }
public IEnumerable<string> QueueIds { get; set; }
public List<string> SharedUserIds { get; set; }
public string DeliveryMethod { get; set; }       // Hourly | Daily Digest | Online
public string DeliveryMethodOption { get; set; } // Morning | Afternoon | Evening
public string OverallAge { get; set; }
public string QueueAge { get; set; }
public string AgeOperator { get; set; }
public string QueueAgeOperator { get; set; }
public string NotTouchDays { get; set; }
public string NotTouchDaysOperator { get; set; }
public IEnumerable<string> RecipientsTo { get; set; }
public IEnumerable<string> RecipientsCC { get; set; }
public string NotificationOption { get; set; }
public string EmailBody { get; set; }
```

### CreateAlertCommand - Data Transformation

Before calling the stored procedure, the command constructor transforms the data:

| Input | Transformation | SP Parameter |
|-------|---------------|--------------|
| `alert.QueueIds` (array) | `string.Join(",", ...)` | `@QueueIds` |
| `alert.RecipientsTo` (array) | `string.Join(";", ...)` | `@RecipientsTo` |
| `alert.RecipientsCC` (array) | `string.Join(";", ...)` | `@RecipientsCC` |
| `alert.SharedUserIds` (array) | `string.Join(",", ...)` + prepend current userId | `@SharedUsers` |
| `fieldDetailsRequest` (List) | `DataUtils.ConvertToDatatable(...)` | `@FieldDetails` (TVP) |

---

## API Endpoints

All endpoints are under `GET/POST/PUT/DELETE /api/v2/alerts`

| Method | Path | Auth | Description | Handler |
|--------|------|------|-------------|---------|
| GET | `/api/v2/alerts` | `[Authorize]` | Get paginated alert list for current user | `GetAlertsBaseInfo` |
| GET | `/api/v2/alerts/count` | `[Authorize]` | Get total alert count for current user | `GetUserAlertsCount` |
| GET | `/api/v2/alerts/history` | `[Authorize]` | Get alert history list (paginated) | `GetAlertsHistory` |
| GET | `/api/v2/alerts/alertHistory` | `[Alert]` | Get detailed history for a specific alert | `GetAlertHistory` |
| GET | `/api/v2/alerts/jobs` | `[Authorize]` | Get jobs available to current user | `GetJobs` |
| GET | `/api/v2/alerts/queues?jobId=X` | `[Authorize]` | Get queues for a job | `GetQueues` |
| GET | `/api/v2/alerts/fields?JobId=X&queueId=Y` | `[AlertQueueId]` | Get dynamic fields for a job | `GetJobFields` |
| GET | `/api/v2/alerts/codes?jobId=X` | `[Job]` | Get error codes for a job | `GetCodes` |
| GET | `/api/v2/alerts/details?alertId=X` | `[Alert]` | Get alert delivery/recipient details | `GetAlertDetails` |
| GET | `/api/v2/alerts/extendedDetails?alertId=X` | `[Alert]` | Get full alert data for edit modal | `GetAlertExtendedDetails` |
| GET | `/api/v2/alerts/records?alertId=X` | `[Alert]` | Get paginated alert records | `GetUserAlertsRecordsPagination` |
| GET | `/api/v2/alerts/checkIfTitleExists?title=X` | `[Authorize]` | Check title uniqueness | `CheckIfAlertTitleExistsByUserId` |
| POST | `/api/v2/alerts` | `[Job]` | **Create new alert** | `SaveAlert` |
| POST | `/api/v2/alerts/sharedUsers` | `[AlertQueueId]` | Get users to share alert with | `GetSharedUsers` |
| POST | `/api/v2/alerts/predictiveInputOptions` | `[Job]` | Get predictive input suggestions | `GetPredictiveInputOptions` |
| POST | `/api/v2/alerts/export` | `[Alert]` | Export alert records to Excel | `Export` |
| PUT | `/api/v2/alerts` | `[Job]` | Update existing alert | `UpdateAlert` |
| DELETE | `/api/v2/alerts?alertId=X` | `[Alert]` | Delete alert | `DeleteAlert` |

**Authorization Types:**
- `[Authorize]` - JWT authenticated user only
- `[Job]` - `CustomAuthorizeAttribute(ResourceType.Job)` - user must have access to the job
- `[Alert]` - `CustomAuthorizeAttribute(ResourceType.Alert)` - user must own or share the alert
- `[AlertQueueId]` - `CustomAuthorizeAttribute(ResourceType.AlertQueueId)` - user must have queue access

---

## Database Tables

Based on code analysis (inline SQL queries and stored procedure parameters), the following tables are used:

### Core Alert Tables

#### `UserAlert`
Primary table storing alert definitions.

| Column | Type | Notes |
|--------|------|-------|
| `Alert_id` | INT (PK) | Alert identifier |
| `Title` | VARCHAR(31) | Alert title, unique per user |
| `User_Id` | INT (FK) | Owner user ID |
| `Frequency` | VARCHAR | Delivery method: `Hourly`, `Daily Digest`, `Online` |
| `Frequency_option` | VARCHAR | `Morning`, `Afternoon`, `Evening` (for Daily Digest) |
| `Recipient_to` | VARCHAR | Semicolon-separated email list |
| `Recipient_cc` | VARCHAR | Semicolon-separated email list |
| `Notification_option` | VARCHAR | `Full Report Embedded`, `Full Report Attached` |
| `EmailBody` | VARCHAR(MAX) | Email body text |
| `Is_Active` | BIT | Soft delete flag (1 = active) |
| `Created_date` | DATETIME | Creation timestamp |
| `Created_by` | INT (FK) | User ID who created |
| `Last_modified_date` | DATETIME | Last update timestamp |
| `Last_modified_by` | INT (FK) | User ID who last modified |

**Referenced in:**
- `AlertsHistoryListQueryHandler` - inline SQL on `UserAlert`
- `GetAlertDetailsQueryHandler` - inline SQL on `UserAlert`
- `GetAlertTitleQueryHandler` - inline SQL on `UserAlert`
- `GetAlertsCountQueryHandler` - inline SQL on `UserAlert`

---

#### `AlertConfig` (inferred: `pwrx.AlertConfigs`)
Stores field filter configurations per alert.

| Column | Type | Notes |
|--------|------|-------|
| `Config_id` | INT (PK) | Config identifier |
| `Alert_id` | INT (FK) | References `UserAlert.Alert_id` |
| `Filter_id` | INT | Field ID (-1 = NotTouchDays special case) |
| `Operator` | VARCHAR | `<`, `<=`, `=`, `>=`, `>` |
| `Field_value` | VARCHAR | Filter value |
| `Job_id` | INT | Job context |
| `Is_calculated_column` | BIT | Whether field is calculated |
| `FName` | VARCHAR | Field name |
| `FType` | VARCHAR | Field type |
| `JobName` | VARCHAR | Job name |
| `ControlType` | VARCHAR | `TEXT`, `DATE`, `AMOUNT`, `ERRORCODE` |

**Referenced in:**
- `GetAlertExtendedDetailsQueryHandler` - via `GetAlertByID` SP (result set 2)
- `AlertService.GetAlertExtendedDetails()` - maps `RoutedDate` → QueueAge, `InitialUploadDate` → OverallAge, `FilterId = -1` → NotTouchDays

---

#### `AlertJob` (inferred: `pwrx.AlertJobs`)
Maps alerts to jobs (many-to-many).

| Column | Type | Notes |
|--------|------|-------|
| `AlertId` | INT (FK) | References `UserAlert.Alert_id` |
| `JobId` | INT (FK) | References `Jobs.JobID` |
| `JobName` | VARCHAR | Denormalized job name |

**Referenced in:**
- `GetAlertExtendedDetailsQueryHandler` - via `GetAlertByID` SP (result set 3)

---

#### `AlertQueue` (inferred: `pwrx.AlertQueues`)
Maps alerts to queues (many-to-many).

| Column | Type | Notes |
|--------|------|-------|
| `AlertId` | INT (FK) | References `UserAlert.Alert_id` |
| `QueueId` | INT (FK) | References `QMenuUser.QID` |
| `QName` | VARCHAR | Denormalized queue name |

**Referenced in:**
- `GetAlertExtendedDetailsQueryHandler` - via `GetAlertByID` SP (result set 4)

---

#### `AlertSharedUser` (inferred: `pwrx.AlertSharedUsers`)
Tracks which users an alert is shared with.

| Column | Type | Notes |
|--------|------|-------|
| `AlertId` | INT (FK) | References `UserAlert.Alert_id` |
| `Share_With` | VARCHAR | Comma-separated user IDs |

**Referenced in:**
- `GetAlertExtendedDetailsQueryHandler` - via `GetAlertByID` SP (result set 6)

---

### Supporting / Reference Tables

#### `Jobs`
| Column | Notes |
|--------|-------|
| `JobID` | PK |
| `JobName` | Display name |

**Referenced in:**
- `GetJobsListQueryHandler` - inline SQL: `SELECT DISTINCT uj.JobID, j.JobName FROM UsersJobs uj INNER JOIN Jobs j ON j.JobID = uj.JobID WHERE uj.UserID = @userId`
- `GetUserAlertsRecordsPaginationQueryHandler` - inline SQL: `select RTRIM(JobName) from dbo.Jobs where JobID = @jobId`

---

#### `UsersJobs`
Maps users to the jobs they have access to.

| Column | Notes |
|--------|-------|
| `UserID` | FK to Users |
| `JobID` | FK to Jobs |

**Referenced in:**
- `GetJobsListQueryHandler` - inline SQL join

---

#### `QMenuUser`
Queue definitions with user access.

| Column | Notes |
|--------|-------|
| `QID` | Queue ID (PK) |
| `QName` | Queue name |
| `userid` | User who has access |

**Referenced in:**
- `GetQueuesByJobIdQueryHandler` - via `GetWorkItemsByJobId` SP
- `CheckItemIdAccessQueryHandler` - inline SQL join

---

#### `Users`
User account information.

| Column | Notes |
|--------|-------|
| `UserID` | PK |
| `FirstName` | First name |
| `LastName` | Last name |

**Referenced in:**
- `AlertsHistoryListQueryHandler` - inline SQL join for `CreatedBy` and `ModifiedBy` names

---

#### `WorkItems`
Document/work item records.

| Column | Notes |
|--------|-------|
| `ItemID` | PK |
| `JobID` | FK to Jobs |
| `StatusID` | FK to QMenuUser.QID |
| `Lock` | Lock flag |
| `LockedBy` | User ID who locked |

**Referenced in:**
- `CheckItemIdAccessQueryHandler` - inline SQL

---

## Stored Procedures & SQL

### Stored Procedures

| SP Name | Used By | Purpose | Parameters |
|---------|---------|---------|------------|
| `SaveUpdateAlerts` | `CreateAlertCommandHandler` | **Create OR update** an alert and all its related data | `@Alert_Id`, `@Title`, `@JobIds`, `@QueueIds`, `@SharedUsers`, `@FrequencyTypes`, `@FrequencyOptions`, `@OverallAge`, `@AgeOperator`, `@queueAge`, `@QueueageOperator`, `@RecipientsTo`, `@RecipientsCC`, `@NotificationOptions`, `@EmailBody`, `@UserId`, `@FieldDetails` (TVP) |
| `GetAllUserAlertsWithOwner` | `GetAlertsByUserIdQueryHandler`, `GetAlertByUserIdQueryHandler` | Get all alerts for a user with owner name and document count | `@UserId`, optional `@alert` (alertId) |
| `GetAlertByID` | `GetAlertExtendedDetailsQueryHandler` | Get full alert details for edit modal — returns **6 result sets** | `@AlertId` |
| `GetAlertFieldsByJob` | `GetAlertFieldsByJobQueryHandler` | Get dynamic fields available for an alert by job — returns **2 result sets** | `@jobID`, `@userID`, `@qID` |
| `GetWorkItemsByJobId` | `GetQueuesByJobIdQueryHandler` | Get queues (work item types) for a job | `@JobId`, `@UserId` |
| `GeSharedUsersByQId` | `GetSharedUsersQueryHandler` | Get users who share the same queues | `@QId` (queue IDs), `@UserId` |
| `CheckIfAlertTitleExistsByUserId` | `CheckIfAlertTitleExistsByUserIdQueryHandler` | Check if alert title already exists for user | `@UserId`, `@Title`, `@AlertId` |
| `DeleteUserAlertById` | `DeleteAlertCommandHandler` | Soft or hard delete an alert | `@AlertId`, `@UserId` |
| `GetAlertHistoryByID` | `GetAlertHistoryQueryHandler` | Get history entries for a specific alert | `@AlertId` |
| `Sp_GetFilter` | `GetUserAlertsRecordsPaginationQueryHandler` | Get paginated alert records (documents matching alert criteria) | `@AlertId`, `@iDisplayStart`, `@iDisplayLength`, `@UserId` |
| `GetCodesDataByJobId` | `GetCodesDataByJobIdQueryHandler` | Get predictive input suggestions (field values) | `@JobId`, `@SearchString`, `@ColumnName`, `@UserId` |
| `GetErrorCodesByJobId` | `GetErrorCodesQueryHandler` | Get error code dropdown options for a job | `@jobId` |

---

### `GetAlertByID` — 6 Result Sets

This SP is the most complex, returning 6 result sets used to populate the edit modal:

| Result Set | Maps To | Content |
|------------|---------|---------|
| 1 | `EditAlert` | Alert header: `Alert_id`, `Title`, `Frequency`, `Frequency_option`, `Recipient_to`, `Recipient_cc`, `Notification_option`, `Created_date`, `Created_by`, `Last_modified_date`, `Last_modified_by`, `Is_Active`, `User_Id`, `EmailBody` |
| 2 | `AlertConfig[]` | Field filter configs: `Config_id`, `Alert_id`, `Filter_id`, `Operator`, `Field_value`, `Job_id`, `Is_calculated_column`, `FName`, `FType`, `JobName`, `ControlType` |
| 3 | `AlertJob[]` | Job mappings: `JobId`, `JobName` |
| 4 | `AlertQueue[]` | Queue mappings: `QueueId`, `QName` |
| 5 | User metadata | `Created_By` (name), `Last_modified_by` (name) |
| 6 | Shared users | `Share_With` (comma-separated user IDs) |

---

### Inline SQL Queries (not stored procedures)

| Handler | SQL |
|---------|-----|
| `GetAlertsCountQueryHandler` | `select count(*) from useralert where user_id = @userId and Is_Active = 1` |
| `GetJobsListQueryHandler` | `SELECT DISTINCT uj.JobID, j.JobName FROM UsersJobs uj INNER JOIN Jobs j ON j.JobID = uj.JobID WHERE uj.UserID = @userId ORDER BY j.JobName` |
| `AlertsHistoryListQueryHandler` | `select Alert_id, Title, Created_date, CreatedBy, Last_modified_date, ModifiedBy from UserAlert ua left join Users c ... left join Users m ... WHERE User_Id = @userId and ua.Is_Active = 1` |
| `GetAlertDetailsQueryHandler` | `select Alert_id, Frequency, Frequency_option, Recipient_to, Recipient_cc, Notification_option from UserAlert where Alert_id = @AlertId` |
| `GetAlertTitleQueryHandler` | `select Title from UserAlert where Alert_id = @alertId` |
| `CheckItemIdAccessQueryHandler` | `select ItemId from WorkItems w inner join QMenuUser q on w.StatusID = q.QID where JobID = @jobId and ItemID in ([itemIds]) and q.userid = @userId and (isnull(Lock,0) <> 1 or (Lock = 1 and LockedBy = @userId))` |
| `GetUserAlertsRecordsPaginationQueryHandler` | `select RTRIM(JobName) from dbo.Jobs where JobID = @jobId` (secondary query) |

---

## End-to-End Flow: Add Alert

### Step 1 — User Opens the Modal

```
User clicks "ADD ALERT" button
  → Alerts.jsx: _openAddAlertModal()
  → dispatch(changeAlertsCreateModalDisplay())
    → Redux: ALERTS_RESET_CREATE_DATA  (clears form state to INITIAL_VALIDATION_OBJECT)
    → Redux: ALERTS_CHANGE_CREATE_MODAL_DISPLAY  (createAlertModalOpen = true)
  → AlertsCreateModal renders with empty CreateAlertForm
  → CreateAlertForm.componentDidMount()
    → dispatch(loadAlertsJobs())
      → GET /api/v2/alerts/jobs
        → AlertsController.GetJobs()
        → AlertService.GetJobsList(userId)
        → GetJobsListQueryHandler.Execute()
          → SQL: SELECT DISTINCT uj.JobID, j.JobName FROM UsersJobs uj INNER JOIN Jobs j ...
        → Returns: [{ JobId, JobName }, ...]
      → Redux: ALERTS_CREATION_SET_JOBS
```

---

### Step 2 — User Selects a Job

```
User selects a Job from dropdown
  → CreateAlertForm._onChangeJob()
  → dispatch(setAlertJob(jobID))
    → Redux: ALERTS_CREATION_SET_SELECTED_JOB  (sets jobID, clears queueIDs)
    → dispatch(loadAlertsQueues(jobID))
      → GET /api/v2/alerts/queues?jobId=X
        → AlertsController.GetQueues(jobId)
        → AlertService.GetQueuesByJobId(jobId, userId)
        → GetQueuesByJobIdQueryHandler.Execute()
          → SP: GetWorkItemsByJobId(@JobId, @UserId)
          → Returns: [{ QueueId, QueueName }, ...]
        → Redux: ALERTS_CREATION_SET_QUEUES_LIST
      → dispatch(loadFormFields(jobID))
        → GET /api/v2/alerts/fields?JobId=X&queueId=Y
          → AlertsController.GetJobFields(jobId, queueId)
          → AlertService.GetAlertFieldsByJob(jobId, userId, queueId)
          → GetAlertFieldsByJobQueryHandler.Execute()
            → SP: GetAlertFieldsByJob(@jobID, @userID, @qID)
            → Result set 1: dynamic field definitions
            → Result set 2: { TotalDisplayColumns, EmbeddedColumns }
            → Prepends hardcoded "CurrentReceiverUser" (FieldId=708) field
          → Redux: ALERTS_CREATION_SET_DYNAMIC_FIELDS_LIST
          → Redux: ALERTS_CREATION_SET_MAX_EMBEDDED_FIELDS
          → Redux: ALERTS_CREATION_SET_EMBEDDED_FIELDS
```

---

### Step 3 — User Selects Queues

```
User selects one or more queues
  → CreateAlertForm._onChangeQueue()
  → dispatch(setAlertQueues(queueIDs))
    → Redux: ALERTS_CREATION_SET_SELECTED_QUEUES
    → dispatch(loadShareWithOptions(queueIDs))
      → POST /api/v2/alerts/sharedUsers  { QueueIds: [...] }
        → AlertsController.GetSharedUsers(request)
        → AlertService.GetSharedUsers(queueIds, userId)
        → GetSharedUsersQueryHandler.Execute()
          → SP: GeSharedUsersByQId(@QId, @UserId)
          → Returns: [{ UserId, UserName }, ...]
        → Redux: ALERTS_CREATION_SET_SHARE_WITH_OPTIONS
```

---

### Step 4 — User Types Alert Title

```
User types in Alert Title field (debounced 1000ms)
  → CreateAlertForm._onChangeTitle()
  → dispatch(validateAlertTitleDebounced(title))
    → Client-side validation first:
        - Empty? → { IsValid: false, Msg: "Alert title should not be empty." }
        - Starts/ends with '? → { IsValid: false, Msg: "...single quotes..." }
        - Length > 31? → { IsValid: false, Msg: "...exceed 31 characters." }
        - Contains /?: *][ ? → { IsValid: false, Msg: "...these characters..." }
        - Otherwise → { IsValid: true, Msg: "" }
    → Redux: ALERTS_CREATION_VALIDATE_TITLE  (updates title.isValid, title.validationMessage)
    → Redux: ALERTS_CREATION_SET_TITLE
    → If client-side valid:
        → GET /api/v2/alerts/checkIfTitleExists?title=X
          → AlertsController.CheckIfAlertTitleExistsByUserId(title, alertId=0)
          → AlertService.CheckIfAlertTitleExistsByUserId(userId, title, 0)
          → CheckIfAlertTitleExistsByUserIdQueryHandler.Execute()
            → SP: CheckIfAlertTitleExistsByUserId(@UserId, @Title, @AlertId)
            → Returns: bool
          → If true: Redux: ALERTS_CREATION_VALIDATE_TITLE { IsValid: false, Msg: "The title already exists." }
          → If false: Redux: ALERTS_CREATION_VALIDATE_TITLE { IsValid: true, Msg: "" }
```

---

### Step 5 — User Fills Remaining Fields

```
Each field change dispatches a Redux action:

  Delivery Method change:
    → dispatch(deliveryMethodChange(method))
    → Redux: ALERTS_CREATION_SET_DELIVERY_METHOD
    → If "Daily Digest": shows Delivery Method Option dropdown
    → If "Online": disables Recipients, CC, Notification Options, Email Body

  Range fields (Not Touched Days / Overall Age / Queue Age):
    → dispatch(rangeFieldsChange({ value, operator }, labelKey))
    → Redux: ALERTS_CREATION_SET_RANGE_FIELDS

  Dynamic fields:
    → dispatch(dynamicFieldValueChange(value, fieldID))  [debounced 900ms]
    → Redux: ALERTS_CREATION_SET_DYNAMIC_FIELD_VALUE

  Recipients:
    → dispatch(recipientsChange(emails))  [debounced 900ms]
    → Redux: ALERTS_CREATION_SET_RECIPIENTS

  Notification Option:
    → dispatch(notificationOptionChange(option))
    → Redux: ALERTS_CREATION_SET_NOTIFICATION_OPTION
```

---

### Step 6 — User Clicks "ADD"

```
User clicks ADD button
  → AlertsCreateModal.onSuccess()
    → validateAlertPayload(createAlertValues)  [client-side final validation]
        Checks: title.isValid, jobID, queueIDs, deliveryMethod,
                recipients (if not Online), notificationOption (if not Online),
                deliveryMethodOption (if Daily Digest),
                at least one range filter set
      → If invalid: setState({ invalidFields }) → shows inline error messages, STOPS
      → If valid:
          → dispatch(saveAlert())
            → formSavePayload(alertSaveState)  [builds API payload]
                - Converts form state to SaveAlertRequest structure
                - Maps dynamicFields → FieldDetails[]
                - Clears email fields if delivery = Online
                - Clears deliveryMethodOption if not Daily Digest
            → POST /api/v2/alerts  { Alert: {...}, FieldDetails: [...] }
```

---

### Step 7 — Backend Processes the Request

```
POST /api/v2/alerts  (SaveAlertRequest)
  → AlertsController.SaveAlert(request)
      1. Null checks on request, request.Alert, request.FieldDetails
      2. Title length check (> 31 → 500)
      3. CheckIfAlertTitleExistsByUserId(userId, title)
           → SP: CheckIfAlertTitleExistsByUserId
           → If exists → 500 "Alert with this title already exists"
      4. AlertService.CreateAlert(userId, alert, fieldDetails)
           → GetFieldDetailsList(fieldDetailsRequest, alert)
               - Processes FieldDetails[] into internal list
               - Handles special cases: NotTouchDays (FilterId=-1),
                 OverallAge (FieldName="InitialUploadDate"),
                 QueueAge (FieldName="RoutedDate")
           → DataUtils.ConvertToDatatable(fieldDetailsList)  → DataTable (TVP)
           → new CreateAlertCommand(userId, alert, fieldDetails)
               - Joins QueueIds with ","
               - Joins RecipientsTo with ";"
               - Joins RecipientsCC with ";"
               - Joins SharedUserIds with "," + prepends current userId
           → CreateAlertCommandHandler.Execute(command)
               → Dapper QueryMultiple on SP: SaveUpdateAlerts
                   Parameters:
                     @Alert_Id        = 0 (new) or existing ID (update)
                     @Title           = "My Alert"
                     @JobIds          = 123
                     @QueueIds        = "456,789"
                     @SharedUsers     = "1,101,102"  (userId always first)
                     @FrequencyTypes  = "Daily Digest"
                     @FrequencyOptions = "Morning"
                     @OverallAge      = "10"
                     @AgeOperator     = ">="
                     @queueAge        = ""
                     @QueueageOperator = ""
                     @RecipientsTo    = "user@example.com"
                     @RecipientsCC    = ""
                     @NotificationOptions = "Full Report Attached"
                     @EmailBody       = "Please review..."
                     @UserId          = 1
                     @FieldDetails    = DataTable (TVP)
               → Reads all result sets; last result set contains { alertID }
               → Returns alertID
      5. AlertService.GetAlertByUserId(userId, alertId)
           → SP: GetAllUserAlertsWithOwner(@UserId, @alert=alertId)
           → Returns AlertDetailsShort { AlertId, Title, TotalDocuments, Owner }
      6. Returns 200 OK with AlertDetailsShort
```

---

### Step 8 — Frontend Handles Response

```
POST /api/v2/alerts succeeds
  → dispatch(getAlerts())
      → GET /api/v2/alerts?pageIndex=1&pageSize=10&search=
        → SP: GetAllUserAlertsWithOwner(@UserId)
        → Redux: ALERTS_SET_RECORDS  (refreshes alert list)
  → dispatch(getAlertsCount())
      → GET /api/v2/alerts/count
        → SQL: select count(*) from useralert where user_id = @userId and Is_Active = 1
        → Redux: ALERTS_UPDATE_GLOBAL_COUNTER
  → dispatch(flashMessageAdd('success', 'Alert was saved successfully.'))
  → Modal closes (changeAlertsCreateModalDisplay())
  → Form state resets to INITIAL_VALIDATION_OBJECT
```

---

## Flow Diagrams

### 1. Modal Open & Form Initialization

```
[User clicks ADD ALERT]
        |
        v
changeAlertsCreateModalDisplay()
        |
        +---> ALERTS_RESET_CREATE_DATA
        +---> ALERTS_CHANGE_CREATE_MODAL_DISPLAY
        |
        v
CreateAlertForm.componentDidMount()
        |
        v
loadAlertsJobs()
        |
        v
GET /api/v2/alerts/jobs
        |
        v
SQL: SELECT DISTINCT JobID, JobName
     FROM UsersJobs INNER JOIN Jobs
     WHERE UserID = @userId
        |
        v
ALERTS_CREATION_SET_JOBS --> Job dropdown populated
```

---

### 2. Job → Queue → Fields Cascade

```
[User selects Job]
        |
        v
setAlertJob(jobID)
        |
        +---> ALERTS_CREATION_SET_SELECTED_JOB
        |
        v
loadAlertsQueues(jobID)
        |
        +---> GET /api/v2/alerts/queues?jobId=X
        |         SP: GetWorkItemsByJobId
        |         --> ALERTS_CREATION_SET_QUEUES_LIST
        |
        +---> loadFormFields(jobID)
                  |
                  v
             GET /api/v2/alerts/fields?JobId=X
                  SP: GetAlertFieldsByJob
                  Result 1: field definitions
                  Result 2: { TotalDisplayColumns, EmbeddedColumns }
                  |
                  +---> ALERTS_CREATION_SET_DYNAMIC_FIELDS_LIST
                  +---> ALERTS_CREATION_SET_MAX_EMBEDDED_FIELDS
                  +---> ALERTS_CREATION_SET_EMBEDDED_FIELDS

[User selects Queues]
        |
        v
setAlertQueues(queueIDs)
        |
        +---> ALERTS_CREATION_SET_SELECTED_QUEUES
        |
        v
loadShareWithOptions(queueIDs)
        |
        v
POST /api/v2/alerts/sharedUsers { QueueIds: [...] }
        SP: GeSharedUsersByQId
        |
        v
ALERTS_CREATION_SET_SHARE_WITH_OPTIONS --> Share With dropdown populated
```

---

### 3. Title Validation Flow

```
[User types title]
        |
        v
validateAlertTitleDebounced(title)  [1000ms debounce]
        |
        v
Client-side checks:
  - Empty?          --> INVALID
  - Starts/ends '?  --> INVALID
  - Length > 31?    --> INVALID
  - Has /?: *][ ?   --> INVALID
  - Otherwise       --> VALID (continue)
        |
        v (if client-side valid)
GET /api/v2/alerts/checkIfTitleExists?title=X
        |
        v
SP: CheckIfAlertTitleExistsByUserId
        |
        +---> true  --> ALERTS_CREATION_VALIDATE_TITLE { IsValid: false }
        +---> false --> ALERTS_CREATION_VALIDATE_TITLE { IsValid: true }
```

---

### 4. Save Alert Flow (Full)

```
[User clicks ADD]
        |
        v
AlertsCreateModal.onSuccess()
        |
        v
validateAlertPayload(createAlertValues)
        |
        +---> Invalid? --> setState(invalidFields) --> show errors --> STOP
        |
        v (valid)
dispatch(saveAlert())
        |
        v
formSavePayload(state) --> builds SaveAlertRequest JSON
        |
        v
POST /api/v2/alerts
        |
        v
AlertsController.SaveAlert(request)
        |
        +---> Null checks
        +---> Title length check
        +---> CheckIfAlertTitleExistsByUserId SP
                  |
                  +--> exists? --> 500 error --> frontend flash error
        |
        v (title is unique)
AlertService.CreateAlert(userId, alert, fieldDetails)
        |
        v
GetFieldDetailsList() --> processes FieldDetails[]
        |
        v
DataUtils.ConvertToDatatable() --> DataTable TVP
        |
        v
new CreateAlertCommand(userId, alert, fieldDetails)
  - QueueIds: array --> "456,789"
  - RecipientsTo: array --> "a@b.com;c@d.com"
  - SharedUserIds: array --> "1,101,102" (userId prepended)
        |
        v
CreateAlertCommandHandler.Execute(command)
        |
        v
Dapper.QueryMultiple("SaveUpdateAlerts", params, StoredProcedure)
        |
        v
SP: SaveUpdateAlerts
  Inserts/Updates:
    - UserAlert (main record)
    - AlertConfig (field filters)
    - AlertJob (job mapping)
    - AlertQueue (queue mappings)
    - AlertSharedUser (sharing)
  Returns: last result set { alertID }
        |
        v
AlertService.GetAlertByUserId(userId, alertId)
        |
        v
SP: GetAllUserAlertsWithOwner(@UserId, @alert=alertId)
        |
        v
Returns AlertDetailsShort to controller --> 200 OK
        |
        v
Frontend:
  dispatch(getAlerts())     --> refreshes list
  dispatch(getAlertsCount()) --> refreshes counter
  flashMessageAdd('success')
  closeModal()
```

---

### 5. Dynamic Field Types

```
GetAlertFieldsByJob SP returns fields with ControlType:

  ControlType = "TEXT" + IsPredictiveAlert = "1"
        --> AlertsPredictiveInput component
            (calls POST /api/v2/alerts/predictiveInputOptions on input)
            (SP: GetCodesDataByJobId)

  ControlType = "DATE"
        --> DateRange component (start date / end date)

  ControlType = "AMOUNT"
        --> AmountRange component (min / max values)

  ControlType = "ERRORCODE"
        --> Dropdown component
            (options from SP: GetErrorCodesByJobId)

  ControlType = "TEXT" + IsPredictiveAlert = "0"
        --> Plain text InputDS component

  Special hardcoded field (always added):
        FieldId = 708, FieldName = "CurrentReceiverUser"
        --> AlertsPredictiveInput (Owner/user search)
```

---

## File Reference Map

### Frontend Files (ModernUI-UI)

| File | Purpose |
|------|---------|
| `src/components/Alerts/index.jsx` | Main Alerts page: header, search, table, modal triggers |
| `src/components/Alerts/AlertsCreateModal/index.jsx` | ADD ALERT modal wrapper; validation on submit |
| `src/components/Alerts/AlertsCreateModal/CreateAlertForm.jsx` | All form fields; dynamic field rendering |
| `src/components/Alerts/AlertsCreateModal/AlertsEditModal.jsx` | EDIT ALERT modal (reuses CreateAlertForm) |
| `src/components/Alerts/AlertsCreateModal/LabelInlineInput.jsx` | Reusable label + input row wrapper |
| `src/components/Alerts/AlertsCreateModal/dymanicFields/AmountRange.jsx` | Amount range input (min/max) |
| `src/components/Alerts/AlertsCreateModal/dymanicFields/DateRange.jsx` | Date range input (start/end) |
| `src/components/Alerts/AlertsCreateModal/dymanicFields/AlertsPredictiveInput.jsx` | Predictive text input with API lookup |
| `src/components/Alerts/AlertsMainTable/` | Paginated alert list table |
| `src/components/Alerts/AlertsHistoryModal/` | Alert history viewer modal |
| `src/components/Alerts/utils.js` | `validateAlertPayload()` - final submit validation |
| `src/redux/actions/alerts/index.js` | All Redux thunks and actions for alerts |
| `src/redux/actions/alerts/utils.js` | `formSavePayload()`, `setDynamicFields()`, `formatEditPayloadToStoreFormat()` |
| `src/redux/reducers/alerts/index.js` | Alerts Redux reducer; full state shape |
| `src/config/API_ENDPOINTS.js` | All API endpoint URL constants |

### Backend Files (ModernUI)

| File | Purpose |
|------|---------|
| `src/IPS.WebAPI/Controllers/V2/AlertsController.cs` | REST controller; 18 endpoints; auth; request validation |
| `src/IPS.AlertsService/Services/IAlertService.cs` | Service interface |
| `src/IPS.AlertsService/Services/AlertService.cs` | Business logic; orchestrates commands/queries; Excel export |
| `src/IPS.AlertsService/Commands/CreateAlertCommand.cs` | Command model; array-to-string transformations |
| `src/IPS.AlertsService/Commands/CreateAlertCommandHandler.cs` | Executes `SaveUpdateAlerts` SP via Dapper |
| `src/IPS.AlertsService/Commands/DeleteAlertCommand.cs` | Delete command model |
| `src/IPS.AlertsService/Commands/DeleteAlertCommandHandler.cs` | Executes `DeleteUserAlertById` SP |
| `src/IPS.AlertsService/Queries/GetAlertsByUserIdQueryHandler.cs` | SP: `GetAllUserAlertsWithOwner` |
| `src/IPS.AlertsService/Queries/GetAlertByUserIdQueryHandler.cs` | SP: `GetAllUserAlertsWithOwner` (single alert) |
| `src/IPS.AlertsService/Queries/GetAlertsCountQueryHandler.cs` | Inline SQL on `UserAlert` |
| `src/IPS.AlertsService/Queries/GetAlertFieldsByJobQueryHandler.cs` | SP: `GetAlertFieldsByJob` |
| `src/IPS.AlertsService/Queries/GetQueuesByJobIdQueryHandler.cs` | SP: `GetWorkItemsByJobId` |
| `src/IPS.AlertsService/Queries/GetJobsListQueryHandler.cs` | Inline SQL on `UsersJobs` + `Jobs` |
| `src/IPS.AlertsService/Queries/GetSharedUsersQueryHandler.cs` | SP: `GeSharedUsersByQId` |
| `src/IPS.AlertsService/Queries/CheckIfAlertTitleExistsByUserIdQueryHandler.cs` | SP: `CheckIfAlertTitleExistsByUserId` |
| `src/IPS.AlertsService/Queries/GetAlertExtendedDetailsQueryHandler.cs` | SP: `GetAlertByID` (6 result sets) |
| `src/IPS.AlertsService/Queries/GetAlertDetailsQueryHandler.cs` | Inline SQL on `UserAlert` |
| `src/IPS.AlertsService/Queries/GetAlertHistoryQueryHandler.cs` | SP: `GetAlertHistoryByID` |
| `src/IPS.AlertsService/Queries/AlertsHistoryListQueryHandler.cs` | Inline SQL on `UserAlert` + `Users` |
| `src/IPS.AlertsService/Queries/GetUserAlertsRecordsPaginationQueryHandler.cs` | SP: `Sp_GetFilter` |
| `src/IPS.AlertsService/Queries/GetCodesDataByJobIdQueryHandler.cs` | SP: `GetCodesDataByJobId` |
| `src/IPS.AlertsService/Queries/GetErrorCodesQueryHandler.cs` | SP: `GetErrorCodesByJobId` |
| `src/IPS.AlertsService/Queries/GetAlertTitleQueryHandler.cs` | Inline SQL on `UserAlert` |
| `src/IPS.AlertsService/Queries/CheckItemIdAccessQueryHandler.cs` | Inline SQL on `WorkItems` + `QMenuUser` |
| `src/IPS.AlertsService/Models/Alert.cs` | Full alert model |
| `src/IPS.AlertsService/Models/AlertBase.cs` | Base: `{ AlertId, Title }` |
| `src/IPS.AlertsService/Models/SaveAlertRequest.cs` | API request: `{ Alert, FieldDetails[] }` |
| `src/IPS.AlertsService/Models/FieldDetailsRequest.cs` | `{ FieldId, FieldName, Value1[], Value2[] }` |

---

## Key Design Notes

1. **`SaveUpdateAlerts` handles both create and update** — the same SP and command handler are used for both. When `Alert_Id = 0`, it inserts; when `Alert_Id > 0`, it updates.

2. **`SharedUserIds` always includes the creator** — the `CreateAlertCommand` constructor prepends the current `userId` to the shared users list before passing to the SP.

3. **`NotTouchDays` uses `FilterId = -1`** as a special sentinel value in `AlertConfig` to distinguish it from regular field filters.

4. **Range fields map to specific field names in `AlertConfig`:**
   - `OverallAge` → stored as `FieldName = "InitialUploadDate"`
   - `QueueAge` → stored as `FieldName = "RoutedDate"`
   - `NotTouchDays` → stored as `FilterId = -1`

5. **`CurrentReceiverUser` (FieldId=708)** is always injected as the first dynamic field by `GetAlertFieldsByJobQueryHandler`, regardless of what the SP returns.

6. **`Full Report Embedded` is conditionally hidden** — if the number of selected alert fields exceeds `EmbeddedColumns` (returned by `GetAlertFieldsByJob`), the "Full Report Embedded" notification option is removed from the dropdown.

7. **Delivery method = Online disables all email fields** — Recipients, CC, Notification Options, and Email Body are all disabled and cleared from the payload when Online is selected.

8. **`Sp_GetFilter`** is a shared stored procedure (not alert-specific) used to retrieve the actual documents/work items that match the alert criteria for the records view and export.
