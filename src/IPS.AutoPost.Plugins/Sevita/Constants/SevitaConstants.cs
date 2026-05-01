namespace IPS.AutoPost.Plugins.Sevita.Constants;

/// <summary>
/// All compile-time constants for the Sevita plugin.
/// Centralises stored procedure names, API endpoint details, HTTP headers,
/// database table names, validation rules, and log message strings.
/// </summary>
public static class SevitaConstants
{
    // -----------------------------------------------------------------------
    // Stored procedure names
    // -----------------------------------------------------------------------

    /// <summary>
    /// Loads all Sevita configuration fields (email settings, OAuth2 credentials,
    /// queue IDs, table names, DB error email config). No parameters.
    /// </summary>
    public const string SpGetConfiguration = "get_sevita_configurations";

    /// <summary>
    /// Returns header + detail DataSet for a single workitem.
    /// Parameter: @UID (bigint).
    /// Returns DataSet with 2 tables: Table[0] = header, Table[1] = detail rows.
    /// </summary>
    public const string SpGetHeaderAndDetailData = "GetSevitaHeaderAndDetailDataByItem";

    /// <summary>
    /// Clears the post-in-process state and updates post fields on the header record.
    /// Parameter: @UID (bigint).
    /// Used instead of a direct SQL UPDATE for Sevita (overrides the default
    /// <c>ClearPostInProcessAsync</c> behavior in <c>IClientPlugin</c>).
    /// </summary>
    public const string SpUpdateHeaderPostFields = "UpdateSevitaHeaderPostFields";

    /// <summary>
    /// Routes a workitem to a target queue.
    /// Parameters: @itemID, @Qid, @userId, @operationType, @comment.
    /// </summary>
    public const string SpWorkitemRoute = "WORKITEM_ROUTE";

    /// <summary>
    /// Inserts an audit log entry.
    /// Parameters: @operationType, @sourceObject, @userID, @comments, @itemID.
    /// </summary>
    public const string SpGeneralLogInsert = "GENERALLOG_INSERT";

    // -----------------------------------------------------------------------
    // Database table names
    // -----------------------------------------------------------------------

    /// <summary>Sevita supplier feed table. Used to load VendorIds in OnBeforePostAsync.</summary>
    public const string SupplierFeedTable = "Sevita_Supplier_SiteInformation_Feed";

    /// <summary>Sevita employee feed table. Used to load EmployeeIds in OnBeforePostAsync.</summary>
    public const string EmployeeFeedTable = "Sevita_Employee_Feed";

    /// <summary>Sevita post history table. History records are inserted here after each workitem.</summary>
    public const string HistoryTable = "sevita_posted_records_history";

    /// <summary>
    /// API response configuration table. Queried at the start of each PostData execution
    /// for manual posts to map response type keys to codes and messages.
    /// </summary>
    public const string ApiResponseConfigTable = "api_response_configuration";

    // -----------------------------------------------------------------------
    // HTTP / API constants
    // -----------------------------------------------------------------------

    /// <summary>Content-Type header for invoice POST requests.</summary>
    public const string ContentTypeJson = "application/json";

    /// <summary>Content-Type header for OAuth2 token requests.</summary>
    public const string ContentTypeFormUrlEncoded = "application/x-www-form-urlencoded";

    /// <summary>OAuth2 grant type used for token requests.</summary>
    public const string OAuth2GrantType = "client_credentials";

    /// <summary>
    /// RestSharp Timeout value for all Sevita API calls.
    /// -1 means no timeout (matches the original Windows Service behaviour).
    /// </summary>
    public const int ApiTimeoutMs = -1;

    // -----------------------------------------------------------------------
    // Validation constants
    // -----------------------------------------------------------------------

    /// <summary>
    /// Natural account number that triggers the <c>cerfTrackingNumber</c> requirement
    /// for Non-PO records.
    /// </summary>
    public const string CerfRequiredAccountNumber = "174098";

    /// <summary>
    /// Default value for <c>checkMemo</c> when it is empty on PO records.
    /// </summary>
    public const string DefaultCheckMemoPO = "PO#";

    // -----------------------------------------------------------------------
    // API response type keys (from api_response_configuration)
    // -----------------------------------------------------------------------

    /// <summary>Response type key for a successful invoice post.</summary>
    public const string ResponseTypePostSuccess = "POST_SUCCESS";

    /// <summary>Response type key for a record that was not posted.</summary>
    public const string ResponseTypeRecordNotPosted = "RECORD_NOT_POSTED";

    // -----------------------------------------------------------------------
    // Error messages
    // -----------------------------------------------------------------------

    /// <summary>
    /// Error message used when the Sevita API returns HTTP 500 Internal Server Error.
    /// </summary>
    public const string ErrorInternalServerError = "Internal Server error occurred while posting invoice.";

    /// <summary>Error message used when the invoice image cannot be retrieved from S3.</summary>
    public const string ErrorImageNotAvailable = "Image is not available.";

    /// <summary>Error message used when line item amounts do not sum to the header invoice amount.</summary>
    public const string ErrorLineSumMismatch = "Line sum does not match invoice header.";

    // -----------------------------------------------------------------------
    // Email template placeholder
    // -----------------------------------------------------------------------

    /// <summary>
    /// Placeholder token in the post failure notification email HTML template.
    /// Replaced with the HTML table of failed records generated by <c>GenerateHtmlTable()</c>.
    /// Note: Sevita uses <c>[[AppendTable]]</c> — NOT InvitedClub's <c>#MissingImagesTable#</c>.
    /// </summary>
    public const string EmailAppendTablePlaceholder = "[[AppendTable]]";

    // -----------------------------------------------------------------------
    // Audit / routing constants
    // -----------------------------------------------------------------------

    /// <summary>operationType value passed to GENERALLOG_INSERT and WORKITEM_ROUTE for Sevita post operations.</summary>
    public const string OperationTypePost = "Post To Sevita";

    /// <summary>sourceObject value passed to GENERALLOG_INSERT.</summary>
    public const string SourceObjectContents = "Contents";

    /// <summary>Routing comment used for automatic (scheduled) routes.</summary>
    public const string RouteCommentAutomatic = "Automatic Route:";

    /// <summary>Routing comment used for manual (UI-triggered) routes.</summary>
    public const string RouteCommentManual = "Manual Route:";

    // -----------------------------------------------------------------------
    // S3 audit JSON path
    // -----------------------------------------------------------------------

    /// <summary>
    /// Timestamp format used when constructing the S3 audit JSON file name.
    /// File name pattern: <c>{itemId}_{timestamp}.json</c>
    /// </summary>
    public const string AuditJsonTimestampFormat = "yyyyMMddHHmmss";

    // -----------------------------------------------------------------------
    // Log message strings
    // -----------------------------------------------------------------------

    public const string LogPostStarted = "Post started for record - {ItemId}";
    public const string LogPostCompleted = "Post completed for record - {ItemId}";
    public const string LogImageNotAvailable = "Image is not available.";
    public const string LogInvoicePostedSuccess = "Invoice Posted Successfully with InvoiceId - {InvoiceId}";
    public const string LogInvoicePostFailed = "Invoice Post Failed";
    public const string LogValidationFailed = "Validation failed for record {ItemId}: {Error}";
    public const string LogLineSumMismatch = "Line sum mismatch for record {ItemId}";
    public const string LogNoRecordsToProcess = "No Records found to process for Job Id: {JobId}, Source Queue: {SourceQueueId}";
    public const string LogLoadValidIdsStarted = "Loading ValidIds from Sevita feed tables";
    public const string LogLoadValidIdsCompleted = "ValidIds loaded: {VendorCount} vendors, {EmployeeCount} employees";
    public const string LogUploadingAuditJson = "Uploading audit JSON to S3 for record {ItemId}";
    public const string LogResponseCode = "Response Code from Sevita API: {StatusCode}";
    public const string LogSendingFailureEmail = "Sending post failure notification email for {FailedCount} failed records";
}
