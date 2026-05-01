namespace IPS.AutoPost.Plugins.InvitedClub.Constants;

/// <summary>
/// All compile-time constants for the InvitedClub plugin.
/// Centralises stored procedure names, Oracle Fusion API URI fragments,
/// HTTP Content-Type headers, database table names, and log message strings.
/// </summary>
public static class InvitedClubConstants
{
    // -----------------------------------------------------------------------
    // Stored procedure names
    // -----------------------------------------------------------------------

    /// <summary>Loads InvitedClub job configuration. Parameter: @IsNewUI (bit).</summary>
    public const string SpGetConfiguration = "get_invitedclub_configuration";

    /// <summary>Returns execution schedule rows. Parameters: @file_creation_config_id, @job_id.</summary>
    public const string SpGetExecutionSchedule = "GetExecutionSchedule";

    /// <summary>Returns header + detail DataSet for a single workitem. Parameter: @UID (bigint).</summary>
    public const string SpGetHeaderAndDetailData = "InvitedClub_GetHeaderAndDetailData";

    /// <summary>
    /// Returns workitems whose image attachment POST has failed and is eligible for retry.
    /// Parameters: @HeaderTable (varchar), @ImagePostRetryLimit (int), @InvitedFailPostQueueId (bigint).
    /// </summary>
    public const string SpGetFailedImagesData = "InvitedClub_GetFailedImagesData";

    /// <summary>Exports supplier data for CSV generation. No parameters.</summary>
    public const string SpGetSupplierDataToExport = "InvitedClub_GetSupplierDataToExport";

    /// <summary>
    /// Updates the SupplierSite column in InvitedClubSupplierAddress after site data is imported.
    /// No parameters.
    /// </summary>
    public const string SpUpdateSupplierSiteInSupplierAddress = "InvitedClub_UpdateSupplierSiteInSupplierAddress";

    /// <summary>
    /// Updates last_supplier_download_time in post_to_invitedclub_configuration.
    /// Parameter: @configurations_id (int).
    /// </summary>
    public const string SpUpdateSupplierLastDownloadTime = "UpdateSupplierLastDownloadTime";

    /// <summary>
    /// Loads email configuration for a specific job.
    /// Parameter: @ConfigId (int).
    /// </summary>
    public const string SpGetEmailConfigPerJob = "GetInvitedClubsEmailConfigPerJob";

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
    // Oracle Fusion REST API URI fragments
    // (appended to DownloadServiceURL or PostServiceURL from configuration)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Supplier list endpoint. Includes all suppliers (active and inactive).
    /// Full URL: {DownloadServiceURL}suppliers?onlyData=true&amp;q=InactiveDate is null
    /// </summary>
    public const string SupplierUri = "suppliers?onlyData=true&q=InactiveDate is null";

    /// <summary>Prefix for supplier address child resource. Append {supplierId} + <see cref="SupplierAddressUriSuffix"/>.</summary>
    public const string SupplierAddressUriPrefix = "suppliers/";

    /// <summary>Suffix for supplier address child resource.</summary>
    public const string SupplierAddressUriSuffix = "/child/addresses?onlyData=true";

    /// <summary>Prefix for supplier site child resource. Append {supplierId} + <see cref="SupplierSiteUriSuffix"/>.</summary>
    public const string SupplierSiteUriPrefix = "suppliers/";

    /// <summary>Suffix for supplier site child resource.</summary>
    public const string SupplierSiteUriSuffix = "/child/sites?onlyData=true";

    /// <summary>
    /// Chart of Accounts LOV endpoint filtered to chart ID 5237, enabled, non-owner accounts.
    /// Full URL: {DownloadServiceURL}accountCombinationsLOV?onlyData=true&amp;q=_CHART_OF_ACCOUNTS_ID=5237;EnabledFlag='Y';AccountType!='O'
    /// </summary>
    public const string CoaUri = "accountCombinationsLOV?onlyData=true&q=_CHART_OF_ACCOUNTS_ID=5237;EnabledFlag='Y';AccountType!='O'";

    /// <summary>
    /// Attachment child resource suffix. Append to {PostServiceURL}/{invoiceId}.
    /// Full URL: {PostServiceURL}/{invoiceId}/child/attachments
    /// </summary>
    public const string AttachmentUriSuffix = "/child/attachments";

    /// <summary>
    /// CalculateTax action suffix. Append to PostServiceURL.
    /// Full URL: {PostServiceURL}/action/calculateTax
    /// </summary>
    public const string CalculateTaxUriSuffix = "/action/calculateTax";

    // -----------------------------------------------------------------------
    // HTTP Content-Type headers
    // -----------------------------------------------------------------------

    /// <summary>Content-Type for invoice POST requests.</summary>
    public const string ContentTypeJson = "application/json";

    /// <summary>Content-Type for attachment POST requests (Oracle ADF resource item).</summary>
    public const string ContentTypeAdfResourceItem = "application/vnd.oracle.adf.resourceitem+json";

    /// <summary>Content-Type for calculateTax action POST requests (Oracle ADF action).</summary>
    public const string ContentTypeAdfAction = "application/vnd.oracle.adf.action+json";

    // -----------------------------------------------------------------------
    // Database table names
    // -----------------------------------------------------------------------

    public const string SupplierTableName = "InvitedClubSupplier";
    public const string SupplierAddressTableName = "InvitedClubSupplierAddress";
    public const string SupplierSiteTableName = "InvitedClubSupplierSite";
    public const string CoaTableName = "InvitedClubCOA";
    public const string CoaFullFeedTableName = "InvitedClubsCOAFullFeed";

    // -----------------------------------------------------------------------
    // Audit / routing constants
    // -----------------------------------------------------------------------

    /// <summary>operationType value passed to GENERALLOG_INSERT and WORKITEM_ROUTE for all InvitedClub post operations.</summary>
    public const string OperationTypePost = "Post To InvitedClubs";

    /// <summary>sourceObject value passed to GENERALLOG_INSERT.</summary>
    public const string SourceObjectContents = "Contents";

    /// <summary>Routing comment used for automatic (scheduled) routes.</summary>
    public const string RouteCommentAutomatic = "Automatic Route:";

    /// <summary>Routing comment used for manual (UI-triggered) routes.</summary>
    public const string RouteCommentManual = "Manual Route:";

    // -----------------------------------------------------------------------
    // Attachment constants
    // -----------------------------------------------------------------------

    /// <summary>Oracle Fusion attachment type for invoice image files.</summary>
    public const string AttachmentType = "File";

    /// <summary>Oracle Fusion attachment category for invoice images.</summary>
    public const string AttachmentCategory = "From Supplier";

    // -----------------------------------------------------------------------
    // API response type keys (from api_response_configuration)
    // -----------------------------------------------------------------------

    public const string ResponseTypePostSuccess = "POST_SUCCESS";
    public const string ResponseTypeRecordNotPosted = "RECORD_NOT_POSTED";

    // -----------------------------------------------------------------------
    // Log message strings
    // -----------------------------------------------------------------------

    public const string LogPostStarted = "Post started for record - {ItemId}";
    public const string LogPostCompleted = "Post completed for record - {ItemId}";
    public const string LogImageNotAvailable = "Image is not available.";
    public const string LogRequesterIdNotFound = "RequesterId not found in HR Feed";
    public const string LogInvoicePostedSuccess = "Invoice Posted Successfully with InvoiceId - {InvoiceId}";
    public const string LogInvoicePostFailed = "Invoice Post Failed";
    public const string LogImagePostedSuccess = "Image Posted Successfully with AttachedDocumentId - {AttachedDocumentId}";
    public const string LogImageNotFound = "Image Not Found";
    public const string LogCalculateTaxSuccess = "Calculate Tax post success";
    public const string LogCalculateTaxFailed = "Calculate Tax post failed - {Result}";
    public const string LogRetryPostImagesStarted = "RetryPostImages started";
    public const string LogRetryPostImagesCompleted = "RetryPostImages completed";
    public const string LogDownloadFeedStarted = "Method - DownloadFeed - Start";
    public const string LogDownloadFeedCompleted = "Method - DownloadFeed - End";
    public const string LogLoadSupplierStarted = "Method - LoadSupplier - Start";
    public const string LogLoadSupplierCompleted = "Method - LoadSupplier - End";
    public const string LogLoadSupplierAddressStarted = "Method - LoadSupplierAddress - Start";
    public const string LogLoadSupplierAddressCompleted = "Method - LoadSupplierAddress - End";
    public const string LogLoadSupplierSiteStarted = "Method - LoadSupplierSite - Start";
    public const string LogLoadSupplierSiteCompleted = "Method - LoadSupplierSite - End";
    public const string LogLoadCoaStarted = "Method - LoadCOA - Start";
    public const string LogLoadCoaCompleted = "Method - LoadCOA - End";
    public const string LogNoRecordsToProcess = "No Records found to process for Job Id: {JobId}, Source Queue: {SourceQueueId}";
    public const string LogGettingImageFromS3 = "Getting Image From S3 {ImagePath}";
    public const string LogResponseCode = "Response Code from InvitedClubs {Operation}: {StatusCode}";

    // -----------------------------------------------------------------------
    // Miscellaneous
    // -----------------------------------------------------------------------

    /// <summary>Pagination page size used for all Oracle Fusion REST API calls.</summary>
    public const int ApiPageSize = 500;

    /// <summary>
    /// RestSharp Timeout value for all Oracle Fusion REST calls.
    /// -1 means no timeout (matches the original Windows Service behaviour).
    /// </summary>
    public const int ApiTimeoutMs = -1;

    /// <summary>UseTax flag value that enables ShipToLocation and calculateTax step.</summary>
    public const string UseTaxYes = "YES";

    /// <summary>UseTax flag value that strips ShipToLocation and skips calculateTax step.</summary>
    public const string UseTaxNo = "NO";

    /// <summary>Placeholder token in the image-failure email HTML template.</summary>
    public const string EmailMissingImagesPlaceholder = "#MissingImagesTable#";

    /// <summary>Temp Excel file name for missing COA export.</summary>
    public const string TempMissingCoaExcelFileName = "InvitedClubsMissingCOAInMaster.xlsx";
}
