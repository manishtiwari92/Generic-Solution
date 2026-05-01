using System.Data;
using System.Text;
using IPS.AutoPost.Core.Extensions;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Core.Services;
using IPS.AutoPost.Plugins.InvitedClub.Constants;
using IPS.AutoPost.Plugins.InvitedClub.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace IPS.AutoPost.Plugins.InvitedClub;

/// <summary>
/// Implements the InvitedClub invoice posting strategy:
/// GetImage -> BuildInvoiceRequest -> PostInvoice -> PostAttachment -> PostCalculateTax (if UseTax=YES).
/// </summary>
public class InvitedClubPostStrategy
{
    private readonly IInvitedClubPostDataAccess _db;
    private readonly S3ImageService _s3ImageService;
    private readonly IEmailService _emailService;
    private readonly ILogger<InvitedClubPostStrategy> _logger;

    public InvitedClubPostStrategy(
        IInvitedClubPostDataAccess db,
        S3ImageService s3ImageService,
        IEmailService emailService,
        ILogger<InvitedClubPostStrategy> logger)
    {
        _db = db;
        _s3ImageService = s3ImageService;
        _emailService = emailService;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // 12.1 GetImageAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Retrieves the invoice image as a base64 string.
    /// Non-legacy jobs: fetches from S3 using the image path as the S3 key.
    /// Legacy jobs: reads from the local file system using
    ///   <c>{config.ImageParentPath}{imagePath}</c>.
    /// Returns (base64, fileName, failed) where <c>failed = true</c> means the image
    /// could not be retrieved.
    /// </summary>
    public virtual async Task<(string? Base64, string FileName, bool Failed)> GetImageAsync(
        GenericJobConfig config,
        EdenredApiUrlConfig s3Config,
        string imagePath,
        CancellationToken ct)
    {
        var fileName = Path.GetFileName(imagePath);

        if (config.IsLegacyJob)
        {
            // Legacy: read from local file system using image_parent_path + ImagePath
            var fullPath = Path.Combine(config.ImageParentPath, imagePath);

            if (!File.Exists(fullPath))
            {
                _logger.LogWarning(InvitedClubConstants.LogImageNotFound + " Path: {FullPath}", fullPath);
                return (null, fileName, true);
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(fullPath, ct);
                return (Convert.ToBase64String(bytes), fileName, false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read local image file: {FullPath}", fullPath);
                return (null, fileName, true);
            }
        }
        else
        {
            // Non-legacy: fetch from S3
            _logger.LogInformation(InvitedClubConstants.LogGettingImageFromS3, imagePath);
            var base64 = await _s3ImageService.GetBase64ImageAsync(imagePath, s3Config, ct);

            if (base64 is null)
            {
                _logger.LogWarning(InvitedClubConstants.LogImageNotFound + " S3Key: {S3Key}", imagePath);
                return (null, fileName, true);
            }

            return (base64, fileName, false);
        }
    }

    // -----------------------------------------------------------------------
    // 12.2 BuildInvoiceRequestJson
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maps the header + detail DataSet rows to an <see cref="InvoiceRequest"/> and
    /// serializes it to JSON.
    /// When <c>UseTax = "NO"</c>, strips <c>ShipToLocation</c> from all invoice lines
    /// using JObject manipulation so the field is completely absent from the payload
    /// (not just null/empty).
    /// </summary>
    public virtual string BuildInvoiceRequestJson(DataSet ds, string useTax)
    {
        var header = ds.Tables[0].Rows[0];
        var detailTable = ds.Tables[1];

        var invoiceLines = new List<InvoiceLine>();
        foreach (DataRow detailRow in detailTable.Rows)
        {
            var distributions = new List<InvoiceDistribution>
            {
                new InvoiceDistribution
                {
                    DistributionLineNumber  = detailRow["DistributionLineNumber"]?.ToString() ?? string.Empty,
                    DistributionLineType    = detailRow["DistributionLineType"]?.ToString() ?? string.Empty,
                    DistributionAmount      = detailRow["DistributionAmount"]?.ToString() ?? string.Empty,
                    DistributionCombination = detailRow["DistributionCombination"]?.ToString() ?? string.Empty
                }
            };

            invoiceLines.Add(new InvoiceLine
            {
                LineNumber              = detailRow["LineNumber"]?.ToString() ?? string.Empty,
                LineAmount              = detailRow["LineAmount"]?.ToString() ?? string.Empty,
                ShipToLocation          = detailRow["ShipToLocation"]?.ToString() ?? string.Empty,
                DistributionCombination = detailRow["DistributionCombination"]?.ToString() ?? string.Empty,
                InvoiceDistributions    = distributions
            });
        }

        var invoiceRequest = new InvoiceRequest
        {
            InvoiceNumber           = header["InvoiceNumber"]?.ToString() ?? string.Empty,
            InvoiceCurrency         = header["InvoiceCurrency"]?.ToString() ?? string.Empty,
            PaymentCurrency         = header["PaymentCurrency"]?.ToString() ?? string.Empty,
            InvoiceAmount           = header["InvoiceAmount"]?.ToString() ?? string.Empty,
            InvoiceDate             = header["InvoiceDate"]?.ToString() ?? string.Empty,
            BusinessUnit            = header["BusinessUnit"]?.ToString() ?? string.Empty,
            Supplier                = header["Supplier"]?.ToString() ?? string.Empty,
            SupplierSite            = header["SupplierSite"]?.ToString() ?? string.Empty,
            RequesterId             = header["RequesterId"]?.ToString() ?? string.Empty,
            AccountingDate          = header["AccountingDate"]?.ToString() ?? string.Empty,
            Description             = header["Description"]?.ToString() ?? string.Empty,
            InvoiceType             = header["InvoiceType"]?.ToString() ?? string.Empty,
            LegalEntity             = header["LegalEntity"]?.ToString() ?? string.Empty,
            LegalEntityIdentifier   = header["LegalEntityIdentifier"]?.ToString() ?? string.Empty,
            LiabilityDistribution   = header["LiabilityDistribution"]?.ToString() ?? string.Empty,
            RoutingAttribute2       = header["RoutingAttribute2"]?.ToString() ?? string.Empty,
            InvoiceSource           = header["InvoiceSource"]?.ToString() ?? string.Empty,
            InvoiceDff = new List<InvoiceDff>
            {
                new InvoiceDff { Payor = header["Payor"]?.ToString() ?? string.Empty }
            },
            InvoiceLines = invoiceLines
        };

        // Serialize to JSON first
        var json = JsonConvert.SerializeObject(invoiceRequest);

        // When UseTax = "NO", strip ShipToLocation from all invoice lines using JObject manipulation
        // so the field is completely absent from the payload (not null/empty — fully removed)
        if (string.Equals(useTax, InvitedClubConstants.UseTaxNo, StringComparison.OrdinalIgnoreCase))
        {
            var jObj = JObject.Parse(json);
            var lines = jObj["invoiceLines"] as JArray;
            if (lines is not null)
            {
                foreach (var line in lines)
                {
                    (line as JObject)?.Remove("ShipToLocation");
                }
            }
            json = jObj.ToString(Formatting.None);
        }

        return json;
    }

    // -----------------------------------------------------------------------
    // 12.3 PostInvoiceAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// POSTs the invoice JSON to Oracle Fusion.
    /// Uses Basic Auth, no timeout, Content-Type: application/json.
    /// Expects HTTP 201. On success: extracts InvoiceId from response JSON.
    /// On non-201: calls UpdateGLDateValue to set GlDate=NULL, routes to InvitedFailPostQueueId.
    /// </summary>
    public virtual async Task<InvoiceResponse> PostInvoiceAsync(
        GenericJobConfig config,
        string invoiceRequestJson,
        CancellationToken ct)
    {
        var clientOptions = new RestClientOptions(config.PostServiceUrl)
        {
            MaxTimeout = InvitedClubConstants.ApiTimeoutMs
        };
        using var client = new RestClient(clientOptions);

        var authHeader = BuildBasicAuthHeader(config.AuthUsername, config.AuthPassword);

        var restRequest = new RestRequest();
        restRequest.AddHeader("Authorization", authHeader);
        restRequest.AddHeader("Content-Type", InvitedClubConstants.ContentTypeJson);
        restRequest.AddStringBody(invoiceRequestJson, InvitedClubConstants.ContentTypeJson);

        var response = await client.ExecutePostAsync(restRequest, ct);

        _logger.LogInformation(
            InvitedClubConstants.LogResponseCode,
            "Invoice Post",
            (int)response.StatusCode);

        if ((int)response.StatusCode == 201)
        {
            var invoiceId = string.Empty;
            if (!string.IsNullOrWhiteSpace(response.Content))
            {
                try
                {
                    var json = JObject.Parse(response.Content);
                    invoiceId = json["InvoiceId"]?.ToString() ?? string.Empty;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse InvoiceId from invoice POST response");
                }
            }

            _logger.LogInformation(InvitedClubConstants.LogInvoicePostedSuccess, invoiceId);

            return new InvoiceResponse
            {
                Status    = 0,
                InvoiceId = invoiceId,
                Result    = response.Content ?? string.Empty
            };
        }

        _logger.LogWarning(InvitedClubConstants.LogInvoicePostFailed + " HTTP {StatusCode}: {Content}",
            (int)response.StatusCode, response.Content);

        return new InvoiceResponse
        {
            Status   = -1,
            Result   = response.Content ?? string.Empty,
            ErrorMsg = $"HTTP {(int)response.StatusCode}: {response.Content}"
        };
    }

    // -----------------------------------------------------------------------
    // 12.4 PostInvoiceAttachmentAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// POSTs the invoice image attachment to Oracle Fusion at
    /// <c>{PostServiceURL}/{invoiceId}/child/attachments</c>.
    /// Uses Basic Auth, no timeout, Content-Type: application/vnd.oracle.adf.resourceitem+json.
    /// Expects HTTP 201. On success: extracts AttachedDocumentId from response JSON.
    /// </summary>
    public virtual async Task<AttachmentResponse> PostInvoiceAttachmentAsync(
        GenericJobConfig config,
        string invoiceId,
        AttachmentRequest attachmentRequest,
        CancellationToken ct)
    {
        var attachmentUrl = $"{config.PostServiceUrl}{invoiceId}{InvitedClubConstants.AttachmentUriSuffix}";

        var clientOptions = new RestClientOptions(attachmentUrl)
        {
            MaxTimeout = InvitedClubConstants.ApiTimeoutMs
        };
        using var client = new RestClient(clientOptions);

        var authHeader = BuildBasicAuthHeader(config.AuthUsername, config.AuthPassword);
        var requestBody = JsonConvert.SerializeObject(attachmentRequest);

        var restRequest = new RestRequest();
        restRequest.AddHeader("Authorization", authHeader);
        restRequest.AddHeader("Content-Type", InvitedClubConstants.ContentTypeAdfResourceItem);
        restRequest.AddStringBody(requestBody, InvitedClubConstants.ContentTypeAdfResourceItem);

        var response = await client.ExecutePostAsync(restRequest, ct);

        _logger.LogInformation(
            InvitedClubConstants.LogResponseCode,
            "Attachment Post",
            (int)response.StatusCode);

        if ((int)response.StatusCode == 201)
        {
            var attachedDocumentId = string.Empty;
            if (!string.IsNullOrWhiteSpace(response.Content))
            {
                try
                {
                    var json = JObject.Parse(response.Content);
                    attachedDocumentId = json["AttachedDocumentId"]?.ToString() ?? string.Empty;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse AttachedDocumentId from attachment POST response");
                }
            }

            _logger.LogInformation(InvitedClubConstants.LogImagePostedSuccess, attachedDocumentId);

            return new AttachmentResponse
            {
                Status             = 0,
                AttachedDocumentId = attachedDocumentId,
                Result             = response.Content ?? string.Empty
            };
        }

        return new AttachmentResponse
        {
            Status   = -1,
            Result   = response.Content ?? string.Empty,
            ErrorMsg = $"HTTP {(int)response.StatusCode}: {response.Content}"
        };
    }

    // -----------------------------------------------------------------------
    // 12.5 PostCalculateTaxAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// POSTs the calculateTax action to Oracle Fusion at
    /// <c>{PostServiceURL}/action/calculateTax</c>.
    /// Uses Basic Auth, no timeout, Content-Type: application/vnd.oracle.adf.action+json.
    /// Uses AddJsonBody (not AddParameter) per spec.
    /// Expects HTTP 200.
    /// </summary>
    public virtual async Task<InvoiceCalculateTaxResponse> PostCalculateTaxAsync(
        GenericJobConfig config,
        InvoiceCalculateTaxRequest calculateTaxRequest,
        CancellationToken ct)
    {
        var calculateTaxUrl = $"{config.PostServiceUrl}{InvitedClubConstants.CalculateTaxUriSuffix}";

        var clientOptions = new RestClientOptions(calculateTaxUrl)
        {
            MaxTimeout = InvitedClubConstants.ApiTimeoutMs
        };
        using var client = new RestClient(clientOptions);

        var authHeader = BuildBasicAuthHeader(config.AuthUsername, config.AuthPassword);

        var restRequest = new RestRequest();
        restRequest.AddHeader("Authorization", authHeader);
        restRequest.AddHeader("Content-Type", InvitedClubConstants.ContentTypeAdfAction);
        // Use AddJsonBody (not AddParameter) per spec requirement
        restRequest.AddJsonBody(calculateTaxRequest);

        var response = await client.ExecutePostAsync(restRequest, ct);

        _logger.LogInformation(
            InvitedClubConstants.LogResponseCode,
            "CalculateTax",
            (int)response.StatusCode);

        if ((int)response.StatusCode == 200)
        {
            _logger.LogInformation(InvitedClubConstants.LogCalculateTaxSuccess);
            return new InvoiceCalculateTaxResponse
            {
                Status = 0,
                Result = response.Content ?? string.Empty
            };
        }

        _logger.LogWarning(InvitedClubConstants.LogCalculateTaxFailed, response.Content);
        return new InvoiceCalculateTaxResponse
        {
            Status = -1,
            Result = response.Content ?? string.Empty
        };
    }

    // -----------------------------------------------------------------------
    // 12.6 SaveHistoryAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Inserts a row into <c>post_to_invitedclub_history</c>.
    /// Only called when at least one Oracle Fusion API call was attempted —
    /// NOT for image-not-found or RequesterId-empty early exits.
    /// </summary>
    public virtual async Task SaveHistoryAsync(
        PostHistory history,
        CancellationToken ct)
    {
        await _db.SavePostHistoryAsync(history, ct);
    }

    // -----------------------------------------------------------------------
    // 12.7 ExecuteAsync — main workitem loop
    // -----------------------------------------------------------------------

    /// <summary>
    /// Main post execution loop. For each workitem:
    /// 1. Set PostInProcess = 1
    /// 2. Get image from S3 (non-legacy) or local path (legacy)
    /// 3. Validate RequesterId
    /// 4. Build invoice payload
    /// 5. PostInvoice -> PostAttachment -> PostCalculateTax (if UseTax=YES)
    /// 6. Route to success/fail queue
    /// 7. Write history to WFInvitedClubsIndexHeader + post_to_invitedclub_history
    /// 8. Clear PostInProcess in finally
    /// After loop: send image failure email if any images failed.
    /// </summary>
    public async Task<PostBatchResult> ExecuteAsync(
        GenericJobConfig config,
        PostContext context,
        CancellationToken ct)
    {
        var clientConfig = config.GetClientConfig<InvitedClubConfig>();
        var result = new PostBatchResult();
        var failedImageItems = new List<(long ItemId, string ImagePath)>();

        // Load API response types for manual posts (used to return structured response codes)
        List<APIResponseType> apiResponseTypes = new();
        if (context.ProcessManually)
        {
            apiResponseTypes = await _db.GetApiResponseTypesAsync(config.JobId, ct);
        }

        // Parse item IDs to process — each item's header+detail data is loaded
        // individually inside ProcessWorkitemAsync via GetHeaderAndDetailDataAsync.
        var itemIds = ParseItemIds(context.ItemIds);

        if (itemIds.Count == 0)
        {
            _logger.LogInformation(
                InvitedClubConstants.LogNoRecordsToProcess,
                config.JobId,
                config.SourceQueueId);
            return result;
        }

        foreach (var itemId in itemIds)
        {
            if (ct.IsCancellationRequested)
                break;

            result.RecordsProcessed++;
            await ProcessWorkitemAsync(
                config, clientConfig, context, apiResponseTypes,
                itemId, result, failedImageItems, ct);
        }

        // 12.8 Send image failure email after the loop if any images failed
        if (failedImageItems.Count > 0)
        {
            await SendImageFailureEmailAsync(config, failedImageItems, ct);
        }

        return result;
    }

    /// <summary>
    /// Processes a single workitem through the full InvitedClub post pipeline.
    /// </summary>
    private async Task ProcessWorkitemAsync(
        GenericJobConfig config,
        InvitedClubConfig clientConfig,
        PostContext context,
        List<APIResponseType> apiResponseTypes,
        long itemId,
        PostBatchResult batchResult,
        List<(long ItemId, string ImagePath)> failedImageItems,
        CancellationToken ct)
    {
        _logger.LogInformation(InvitedClubConstants.LogPostStarted, itemId);

        // Track whether any API call was attempted (controls history write)
        var apiCallAttempted = false;
        var history = new PostHistory
        {
            ItemId        = itemId,
            ManuallyPosted = context.ProcessManually,
            PostedBy      = context.UserId
        };

        // Set PostInProcess = 1 before any API call
        await _db.SetPostInProcessAsync(itemId, config.HeaderTable, ct);

        try
        {
            // Step 1: Load header + detail data
            var ds = await _db.GetHeaderAndDetailDataAsync(itemId, ct);
            if (ds.IsEmpty())
            {
                _logger.LogWarning("No header/detail data found for ItemId={ItemId}", itemId);
                await RouteToFailAsync(config, clientConfig, itemId, context.UserId,
                    "No data found", batchResult, ct);
                return;
            }

            var headerRow = ds.Tables[0].Rows[0];
            var imagePath = headerRow["ImagePath"]?.ToString() ?? string.Empty;
            var useTax    = headerRow["UseTax"]?.ToString() ?? InvitedClubConstants.UseTaxNo;
            var requesterId = headerRow["RequesterId"]?.ToString() ?? string.Empty;

            // Step 2: Get image
            var (base64Image, fileName, imageFailed) = await GetImageAsync(
                config, context.S3Config, imagePath, ct);

            if (imageFailed || string.IsNullOrEmpty(base64Image))
            {
                // Image not found -> route to EdenredFailPostQueueId, NO API call, NO history
                _logger.LogWarning(InvitedClubConstants.LogImageNotAvailable + " ItemId={ItemId}", itemId);
                failedImageItems.Add((itemId, imagePath));

                var edenredFailQueueId = clientConfig.EdenredFailQueueId > 0
                    ? clientConfig.EdenredFailQueueId
                    : config.PrimaryFailQueueId;

                await _db.RouteWorkitemAsync(
                    itemId, edenredFailQueueId, context.UserId,
                    InvitedClubConstants.OperationTypePost,
                    $"{InvitedClubConstants.RouteCommentAutomatic} {InvitedClubConstants.LogImageNotAvailable}",
                    ct);

                await _db.InsertGeneralLogAsync(
                    InvitedClubConstants.OperationTypePost,
                    InvitedClubConstants.SourceObjectContents,
                    context.UserId,
                    InvitedClubConstants.LogImageNotAvailable,
                    itemId,
                    ct);

                batchResult.RecordsFailed++;
                batchResult.ItemResults.Add(new PostItemResult
                {
                    ItemId           = itemId,
                    IsSuccess        = false,
                    DestinationQueue = edenredFailQueueId,
                    ResponseMessage  = InvitedClubConstants.LogImageNotAvailable
                });
                return;
            }

            // Step 3: Validate RequesterId
            if (string.IsNullOrWhiteSpace(requesterId))
            {
                // Empty RequesterId -> route to InvitedFailPostQueueId, NO API call, NO history
                _logger.LogWarning(InvitedClubConstants.LogRequesterIdNotFound + " ItemId={ItemId}", itemId);

                var invitedFailQueueId = clientConfig.InvitedFailQueueId > 0
                    ? clientConfig.InvitedFailQueueId
                    : config.PrimaryFailQueueId;

                await _db.RouteWorkitemAsync(
                    itemId, invitedFailQueueId, context.UserId,
                    InvitedClubConstants.OperationTypePost,
                    $"{InvitedClubConstants.RouteCommentAutomatic} {InvitedClubConstants.LogRequesterIdNotFound}",
                    ct);

                await _db.InsertGeneralLogAsync(
                    InvitedClubConstants.OperationTypePost,
                    InvitedClubConstants.SourceObjectContents,
                    context.UserId,
                    InvitedClubConstants.LogRequesterIdNotFound,
                    itemId,
                    ct);

                batchResult.RecordsFailed++;
                batchResult.ItemResults.Add(new PostItemResult
                {
                    ItemId           = itemId,
                    IsSuccess        = false,
                    DestinationQueue = invitedFailQueueId,
                    ResponseMessage  = InvitedClubConstants.LogRequesterIdNotFound
                });
                return;
            }

            // Step 4: Build invoice request JSON
            var invoiceRequestJson = BuildInvoiceRequestJson(ds, useTax);
            history.InvoiceRequestJson = invoiceRequestJson;

            // Step 5: POST invoice — first API call attempted
            apiCallAttempted = true;
            var invoiceResponse = await PostInvoiceAsync(config, invoiceRequestJson, ct);
            history.InvoiceResponseJson = invoiceResponse.Result;

            if (invoiceResponse.Status != 0)
            {
                // Invoice POST failed -> set GlDate=NULL, route to InvitedFailPostQueueId
                await _db.UpdateGlDateValueAsync(itemId, config.HeaderTable, ct);

                var invitedFailQueueId = clientConfig.InvitedFailQueueId > 0
                    ? clientConfig.InvitedFailQueueId
                    : config.PrimaryFailQueueId;

                await _db.RouteWorkitemAsync(
                    itemId, invitedFailQueueId, context.UserId,
                    InvitedClubConstants.OperationTypePost,
                    $"{InvitedClubConstants.RouteCommentAutomatic} {invoiceResponse.ErrorMsg}",
                    ct);

                await _db.InsertGeneralLogAsync(
                    InvitedClubConstants.OperationTypePost,
                    InvitedClubConstants.SourceObjectContents,
                    context.UserId,
                    invoiceResponse.ErrorMsg,
                    itemId,
                    ct);

                batchResult.RecordsFailed++;
                batchResult.ItemResults.Add(new PostItemResult
                {
                    ItemId           = itemId,
                    IsSuccess        = false,
                    DestinationQueue = invitedFailQueueId,
                    ResponseMessage  = invoiceResponse.ErrorMsg
                });
                return;
            }

            // Invoice succeeded — update InvoiceId on header
            await _db.UpdateInvoiceIdAsync(itemId, invoiceResponse.InvoiceId, config.HeaderTable, ct);

            // Step 6: POST attachment
            var attachmentRequest = new AttachmentRequest
            {
                Type         = InvitedClubConstants.AttachmentType,
                FileName     = fileName,
                Title        = fileName,
                Category     = InvitedClubConstants.AttachmentCategory,
                FileContents = base64Image
            };
            history.AttachmentRequestJson = JsonConvert.SerializeObject(attachmentRequest);

            var attachmentResponse = await PostInvoiceAttachmentAsync(
                config, invoiceResponse.InvoiceId, attachmentRequest, ct);
            history.AttachmentResponseJson = attachmentResponse.Result;

            if (attachmentResponse.Status != 0)
            {
                // Attachment POST failed -> route to EdenredFailPostQueueId
                var edenredFailQueueId = clientConfig.EdenredFailQueueId > 0
                    ? clientConfig.EdenredFailQueueId
                    : config.PrimaryFailQueueId;

                await _db.RouteWorkitemAsync(
                    itemId, edenredFailQueueId, context.UserId,
                    InvitedClubConstants.OperationTypePost,
                    $"{InvitedClubConstants.RouteCommentAutomatic} {attachmentResponse.ErrorMsg}",
                    ct);

                await _db.InsertGeneralLogAsync(
                    InvitedClubConstants.OperationTypePost,
                    InvitedClubConstants.SourceObjectContents,
                    context.UserId,
                    attachmentResponse.ErrorMsg,
                    itemId,
                    ct);

                batchResult.RecordsFailed++;
                batchResult.ItemResults.Add(new PostItemResult
                {
                    ItemId           = itemId,
                    IsSuccess        = false,
                    DestinationQueue = edenredFailQueueId,
                    ResponseMessage  = attachmentResponse.ErrorMsg
                });
                return;
            }

            // Attachment succeeded — update AttachedDocumentId on header
            await _db.UpdateAttachedDocumentIdAsync(
                itemId, attachmentResponse.AttachedDocumentId, config.HeaderTable, ct);

            // Step 7: POST calculateTax if UseTax = YES
            if (string.Equals(useTax, InvitedClubConstants.UseTaxYes, StringComparison.OrdinalIgnoreCase))
            {
                var calculateTaxRequest = new InvoiceCalculateTaxRequest
                {
                    InvoiceNumber = headerRow["InvoiceNumber"]?.ToString() ?? string.Empty,
                    Supplier      = headerRow["Supplier"]?.ToString() ?? string.Empty
                };
                history.CalculateTaxRequestJson = JsonConvert.SerializeObject(calculateTaxRequest);

                var calculateTaxResponse = await PostCalculateTaxAsync(config, calculateTaxRequest, ct);
                history.CalculateTaxResponseJson = calculateTaxResponse.Result;

                if (calculateTaxResponse.Status != 0)
                {
                    // CalculateTax failed -> route to InvitedFailPostQueueId
                    var invitedFailQueueId = clientConfig.InvitedFailQueueId > 0
                        ? clientConfig.InvitedFailQueueId
                        : config.PrimaryFailQueueId;

                    await _db.RouteWorkitemAsync(
                        itemId, invitedFailQueueId, context.UserId,
                        InvitedClubConstants.OperationTypePost,
                        $"{InvitedClubConstants.RouteCommentAutomatic} CalculateTax failed: {calculateTaxResponse.Result}",
                        ct);

                    await _db.InsertGeneralLogAsync(
                        InvitedClubConstants.OperationTypePost,
                        InvitedClubConstants.SourceObjectContents,
                        context.UserId,
                        $"CalculateTax failed: {calculateTaxResponse.Result}",
                        itemId,
                        ct);

                    batchResult.RecordsFailed++;
                    batchResult.ItemResults.Add(new PostItemResult
                    {
                        ItemId           = itemId,
                        IsSuccess        = false,
                        DestinationQueue = invitedFailQueueId,
                        ResponseMessage  = $"CalculateTax failed: {calculateTaxResponse.Result}"
                    });
                    return;
                }
            }

            // Full success -> route to SuccessQueueId
            var successResponseCode = GetResponseCode(apiResponseTypes, InvitedClubConstants.ResponseTypePostSuccess);
            var successMessage = GetResponseMessage(apiResponseTypes, InvitedClubConstants.ResponseTypePostSuccess,
                $"Invoice Posted Successfully with InvoiceId - {invoiceResponse.InvoiceId}");

            await _db.RouteWorkitemAsync(
                itemId, config.SuccessQueueId, context.UserId,
                InvitedClubConstants.OperationTypePost,
                $"{InvitedClubConstants.RouteCommentAutomatic} {successMessage}",
                ct);

            await _db.InsertGeneralLogAsync(
                InvitedClubConstants.OperationTypePost,
                InvitedClubConstants.SourceObjectContents,
                context.UserId,
                successMessage,
                itemId,
                ct);

            _logger.LogInformation(InvitedClubConstants.LogPostCompleted, itemId);

            batchResult.RecordsSuccess++;
            batchResult.ItemResults.Add(new PostItemResult
            {
                ItemId           = itemId,
                IsSuccess        = true,
                ResponseCode     = successResponseCode,
                ResponseMessage  = successMessage,
                DestinationQueue = config.SuccessQueueId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing ItemId={ItemId}", itemId);

            var failQueueId = clientConfig.InvitedFailQueueId > 0
                ? clientConfig.InvitedFailQueueId
                : config.PrimaryFailQueueId;

            try
            {
                await _db.RouteWorkitemAsync(
                    itemId, failQueueId, context.UserId,
                    InvitedClubConstants.OperationTypePost,
                    $"{InvitedClubConstants.RouteCommentAutomatic} Unhandled exception: {ex.Message}",
                    ct);

                await _db.InsertGeneralLogAsync(
                    InvitedClubConstants.OperationTypePost,
                    InvitedClubConstants.SourceObjectContents,
                    context.UserId,
                    ex.Message,
                    itemId,
                    ct);
            }
            catch (Exception routeEx)
            {
                _logger.LogError(routeEx, "Failed to route ItemId={ItemId} to fail queue after exception", itemId);
            }

            batchResult.RecordsFailed++;
            batchResult.ItemResults.Add(new PostItemResult
            {
                ItemId           = itemId,
                IsSuccess        = false,
                DestinationQueue = failQueueId,
                ResponseMessage  = ex.Message
            });
        }
        finally
        {
            // Always clear PostInProcess in finally block
            try
            {
                await _db.ClearPostInProcessAsync(itemId, config.HeaderTable, ct);
            }
            catch (Exception clearEx)
            {
                _logger.LogError(clearEx, "Failed to clear PostInProcess for ItemId={ItemId}", itemId);
            }

            // Save history only when at least one API call was attempted
            if (apiCallAttempted)
            {
                try
                {
                    await SaveHistoryAsync(history, ct);
                }
                catch (Exception histEx)
                {
                    _logger.LogError(histEx, "Failed to save post history for ItemId={ItemId}", itemId);
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // 12.8 SendImageFailureEmailAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sends an image failure notification email to the helpdesk.
    /// Only sent when:
    /// - <c>EmailTemplateImageFail</c> is not null/whitespace, AND
    /// - <c>EmailToHelpDesk</c> has non-zero length after splitting by ';'.
    /// Replaces the <c>#MissingImagesTable#</c> placeholder in the template with
    /// an HTML table generated from the failed image records.
    /// </summary>
    public virtual async Task SendImageFailureEmailAsync(
        GenericJobConfig config,
        List<(long ItemId, string ImagePath)> failedItems,
        CancellationToken ct)
    {
        try
        {
            var emailConfigTable = await _db.GetEmailConfigAsync(config.Id, ct);
            var emailConfigs = emailConfigTable.ConvertDataTable<EmailConfig>();

            if (emailConfigs.Count == 0)
                return;

            var emailConfig = emailConfigs[0];

            // Guard: only send when template and recipients are configured
            if (string.IsNullOrWhiteSpace(emailConfig.EmailTemplateImageFail))
                return;

            var helpdeskRecipients = SplitEmails(emailConfig.EmailToHelpDesk);
            if (helpdeskRecipients.Length == 0)
                return;

            // Build a DataTable from the failed items for GenerateHtmlTable
            var dt = new System.Data.DataTable();
            dt.Columns.Add("ItemId",    typeof(long));
            dt.Columns.Add("ImagePath", typeof(string));
            foreach (var (itemId, imagePath) in failedItems)
                dt.Rows.Add(itemId, imagePath);

            var htmlTable = dt.GenerateHtmlTable();
            var htmlBody = emailConfig.EmailTemplateImageFail
                .Replace(InvitedClubConstants.EmailMissingImagesPlaceholder, htmlTable);

            await _emailService.SendAsync(
                smtpServer:      emailConfig.SMTPServer,
                smtpPort:        emailConfig.SMTPServerPort,
                fromAddress:     emailConfig.EmailFrom,
                fromDisplayName: emailConfig.EmailFromUser,
                toAddresses:     helpdeskRecipients,
                ccAddresses:     Array.Empty<string>(),
                bccAddresses:    Array.Empty<string>(),
                subject:         emailConfig.EmailSubjectImageFail,
                htmlBody:        htmlBody,
                useSsl:          emailConfig.SMTPUseSSL,
                smtpUsername:    emailConfig.Username,
                smtpPassword:    emailConfig.Password,
                ct:              ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send image failure email for JobId={JobId}", config.JobId);
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private async Task RouteToFailAsync(
        GenericJobConfig config,
        InvitedClubConfig clientConfig,
        long itemId,
        int userId,
        string reason,
        PostBatchResult batchResult,
        CancellationToken ct)
    {
        var failQueueId = clientConfig.InvitedFailQueueId > 0
            ? clientConfig.InvitedFailQueueId
            : config.PrimaryFailQueueId;

        await _db.RouteWorkitemAsync(
            itemId, failQueueId, userId,
            InvitedClubConstants.OperationTypePost,
            $"{InvitedClubConstants.RouteCommentAutomatic} {reason}",
            ct);

        batchResult.RecordsFailed++;
        batchResult.ItemResults.Add(new PostItemResult
        {
            ItemId           = itemId,
            IsSuccess        = false,
            DestinationQueue = failQueueId,
            ResponseMessage  = reason
        });
    }

    private static List<long> ParseItemIds(string itemIds)
    {
        if (string.IsNullOrWhiteSpace(itemIds))
            return new List<long>();

        return itemIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => long.TryParse(id, out var parsed) ? parsed : 0L)
            .Where(id => id > 0)
            .ToList();
    }

    private static int GetResponseCode(List<APIResponseType> types, string responseType)
    {
        var match = types.FirstOrDefault(t =>
            string.Equals(t.ResponseType, responseType, StringComparison.OrdinalIgnoreCase));
        return int.TryParse(match?.ResponseCode, out var code) ? code : 0;
    }

    private static string GetResponseMessage(
        List<APIResponseType> types,
        string responseType,
        string fallback)
    {
        var match = types.FirstOrDefault(t =>
            string.Equals(t.ResponseType, responseType, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(match?.ResponseMessage) ? fallback : match.ResponseMessage;
    }

    private static string BuildBasicAuthHeader(string username, string password)
    {
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{username}:{password}"));
        return $"Basic {credentials}";
    }

    private static string[] SplitEmails(string emailString)
    {
        if (string.IsNullOrWhiteSpace(emailString))
            return Array.Empty<string>();

        return emailString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
