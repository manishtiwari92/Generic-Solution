using System.Data;
using IPS.AutoPost.Core.Extensions;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Core.Services;
using IPS.AutoPost.Plugins.Sevita.Constants;
using IPS.AutoPost.Plugins.Sevita.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace IPS.AutoPost.Plugins.Sevita;

public class SevitaPostStrategy
{
    private readonly ISevitaPostDataAccess _db;
    private readonly SevitaTokenService _tokenService;
    private readonly SevitaValidationService _validationService;
    private readonly S3ImageService _s3ImageService;
    private readonly IEmailService _emailService;
    private readonly ILogger<SevitaPostStrategy> _logger;

    public SevitaPostStrategy(
        ISevitaPostDataAccess db,
        SevitaTokenService tokenService,
        SevitaValidationService validationService,
        S3ImageService s3ImageService,
        IEmailService emailService,
        ILogger<SevitaPostStrategy> logger)
    {
        _db = db;
        _tokenService = tokenService;
        _validationService = validationService;
        _s3ImageService = s3ImageService;
        _emailService = emailService;
        _logger = logger;
    }

    // 16.1 BuildLineItems
    public virtual List<InvoiceLine> BuildLineItems(DataTable detailTable, string edenredInvoiceId)
    {
        var groups = new Dictionary<string, (string Alias, string NaturalAccountNumber, decimal Sum)>(StringComparer.Ordinal);
        var groupOrder = new List<string>();

        foreach (DataRow row in detailTable.Rows)
        {
            var alias = row["alias"]?.ToString() ?? string.Empty;
            var naturalAccountNumber = row["naturalAccountNumber"]?.ToString() ?? string.Empty;
            var key = alias + "|" + naturalAccountNumber;

            if (!decimal.TryParse(row["LineAmount"]?.ToString(), out var lineAmount))
                lineAmount = 0m;

            if (groups.TryGetValue(key, out var existing))
            {
                groups[key] = (existing.Alias, existing.NaturalAccountNumber, existing.Sum + lineAmount);
            }
            else
            {
                groups[key] = (alias, naturalAccountNumber, lineAmount);
                groupOrder.Add(key);
            }
        }

        var result = new List<InvoiceLine>();
        int lineItemCount = 1;

        foreach (var key in groupOrder)
        {
            var (alias, naturalAccountNumber, sum) = groups[key];
            result.Add(new InvoiceLine
            {
                alias = alias,
                naturalAccountNumber = naturalAccountNumber,
                amount = Math.Round(sum, 2),
                edenredLineItemId = edenredInvoiceId + "_" + lineItemCount
            });
            lineItemCount++;
        }

        return result;
    }

    // 16.2 SerializePayload
    public virtual string SerializePayload(InvoiceRequest request)
    {
        var json = JsonConvert.SerializeObject(request);
        return "[" + json + "]";
    }

    // 16.3 UploadAuditJsonAsync
    public virtual async Task UploadAuditJsonAsync(
        string invoiceRequestJson,
        long itemId,
        SevitaConfig clientConfig,
        EdenredApiUrlConfig s3Config,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientConfig.PostJsonPath))
            return;

        _logger.LogInformation(SevitaConstants.LogUploadingAuditJson, itemId);

        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, invoiceRequestJson, ct);

            var timestamp = DateTime.UtcNow.ToString(SevitaConstants.AuditJsonTimestampFormat);
            var s3Key = $"{clientConfig.PostJsonPath}/{itemId}_{timestamp}.json";

            await _s3ImageService.UploadFileAsync(tempFile, s3Key, s3Config, ct);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    // 16.4 PostInvoiceAsync
    public virtual async Task<InvoiceResponse> PostInvoiceAsync(
        GenericJobConfig config,
        SevitaConfig clientConfig,
        string invoiceRequestJson,
        bool isManual,
        CancellationToken ct)
    {
        var token = await _tokenService.GetAuthTokenAsync(clientConfig, ct);

        var clientOptions = new RestClientOptions(config.PostServiceUrl)
        {
            MaxTimeout = SevitaConstants.ApiTimeoutMs
        };
        using var client = new RestClient(clientOptions);

        var restRequest = new RestRequest();
        restRequest.AddHeader("Authorization", "Bearer " + token);
        restRequest.AddParameter("application/json", invoiceRequestJson, ParameterType.RequestBody);

        var response = await client.ExecutePostAsync(restRequest, ct);

        _logger.LogInformation(SevitaConstants.LogResponseCode, (int)response.StatusCode);

        if ((int)response.StatusCode == 201)
        {
            var invoiceId = string.Empty;
            if (!string.IsNullOrWhiteSpace(response.Content))
            {
                try
                {
                    var json = JObject.Parse(response.Content);
                    var invoiceIds = json["invoiceIds"] as JObject;
                    if (invoiceIds != null)
                    {
                        var firstProp = invoiceIds.Properties().FirstOrDefault();
                        if (firstProp != null)
                            invoiceId = firstProp.Name;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse InvoiceId from Sevita POST response");
                }
            }

            _logger.LogInformation(SevitaConstants.LogInvoicePostedSuccess, invoiceId);

            return new InvoiceResponse
            {
                Status = 0,
                InvoiceId = invoiceId,
                Result = response.Content ?? string.Empty
            };
        }

        if ((int)response.StatusCode == 500)
        {
            _logger.LogWarning(SevitaConstants.LogInvoicePostFailed + " HTTP 500");
            return new InvoiceResponse
            {
                Status = -1,
                Result = response.Content ?? string.Empty,
                ErrorMsg = SevitaConstants.ErrorInternalServerError
            };
        }

        // Other non-201 responses: extract error details
        var errorMsg = ExtractErrorMessage(response.Content);
        _logger.LogWarning(SevitaConstants.LogInvoicePostFailed + " HTTP {StatusCode}: {Content}",
            (int)response.StatusCode, response.Content);

        return new InvoiceResponse
        {
            Status = -1,
            Result = response.Content ?? string.Empty,
            ErrorMsg = errorMsg
        };
    }

    // 16.5 SaveHistoryAsync
    public virtual async Task SaveHistoryAsync(
        PostHistory history,
        CancellationToken ct)
    {
        // Null out fileBase on all attachments before saving to prevent storing large base64 strings
        if (!string.IsNullOrWhiteSpace(history.InvoiceRequestJson))
        {
            try
            {
                var arr = JArray.Parse(history.InvoiceRequestJson);
                foreach (var item in arr)
                {
                    var attachments = item["attachments"] as JArray;
                    if (attachments != null)
                    {
                        foreach (var attachment in attachments)
                        {
                            (attachment as JObject)?.Property("fileBase")?.Remove();
                            (attachment as JObject)?.Add("fileBase", JValue.CreateNull());
                        }
                    }
                }
                history.InvoiceRequestJson = arr.ToString(Formatting.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to null out fileBase in history JSON for ItemId={ItemId}", history.ItemId);
            }
        }

        await _db.SavePostHistoryAsync(history, ct);
    }

    // 16.6 SendNotificationEmailAsync
    public virtual async Task SendNotificationEmailAsync(
        FailedPostConfiguration failedPostConfig,
        EmailConfiguration emailConfig,
        List<PostFailedRecord> failedRecords,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(failedPostConfig.EmailTo))
            return;

        var notifyRecords = failedRecords.Where(r => r.IsSendNotification).ToList();
        if (notifyRecords.Count == 0)
            return;

        _logger.LogInformation(SevitaConstants.LogSendingFailureEmail, notifyRecords.Count);

        var dt = notifyRecords.ToDataTable();
        var htmlTable = dt.GenerateHtmlTable(excludeColumns: new[] { nameof(PostFailedRecord.IsSendNotification) });

        var emailBody = failedPostConfig.EmailTemplate
            .Replace(SevitaConstants.EmailAppendTablePlaceholder, htmlTable);

        var toAddresses = failedPostConfig.EmailTo
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        await _emailService.SendAsync(
            smtpServer: emailConfig.SmtpServer,
            smtpPort: emailConfig.SmtpServerPort,
            fromAddress: emailConfig.EmailFrom,
            fromDisplayName: emailConfig.EmailFromUser,
            toAddresses: toAddresses,
            ccAddresses: Array.Empty<string>(),
            bccAddresses: Array.Empty<string>(),
            subject: failedPostConfig.EmailSubject,
            htmlBody: emailBody,
            useSsl: emailConfig.SmtpUseSsl,
            smtpUsername: emailConfig.Username,
            smtpPassword: emailConfig.Password,
            ct: ct);
    }

    // 16.7 ExecuteAsync main loop
    public async Task<PostBatchResult> ExecuteAsync(
        GenericJobConfig config,
        PostContext context,
        ValidIds validIds,
        FailedPostConfiguration failedPostConfig,
        EmailConfiguration emailConfig,
        CancellationToken ct)
    {
        var clientConfig = config.GetClientConfig<SevitaConfig>();
        var result = new PostBatchResult();
        var failedRecords = new List<PostFailedRecord>();

        // Load API response types for manual posts
        List<PostResponseType> apiResponseTypes = new();
        if (context.ProcessManually)
        {
            apiResponseTypes = await _db.GetApiResponseTypesAsync(config.JobId, ct);
        }

        var itemIds = ParseItemIds(context.ItemIds);

        if (itemIds.Count == 0)
        {
            _logger.LogInformation(SevitaConstants.LogNoRecordsToProcess, config.JobId, config.SourceQueueId);
            return result;
        }

        foreach (var itemId in itemIds)
        {
            if (ct.IsCancellationRequested)
                break;

            result.RecordsProcessed++;
            await ProcessWorkitemAsync(
                config, clientConfig, context, validIds,
                itemId, result, failedRecords, ct);
        }

        // After loop: send notification email if any failed records with IsSendNotification=true
        if (failedRecords.Any(r => r.IsSendNotification))
        {
            await SendNotificationEmailAsync(failedPostConfig, emailConfig, failedRecords, ct);
        }

        return result;
    }

    private async Task ProcessWorkitemAsync(
        GenericJobConfig config,
        SevitaConfig clientConfig,
        PostContext context,
        ValidIds validIds,
        long itemId,
        PostBatchResult batchResult,
        List<PostFailedRecord> failedRecords,
        CancellationToken ct)
    {
        _logger.LogInformation(SevitaConstants.LogPostStarted, itemId);

        var history = new PostHistory
        {
            ItemId = itemId,
            ManuallyPosted = context.ProcessManually,
            PostedBy = context.UserId
        };

        await _db.SetPostInProcessAsync(itemId, config.HeaderTable, ct);

        try
        {
            // Step 1: Load header + detail data
            var ds = await _db.GetHeaderAndDetailDataAsync(itemId, ct);
            if (ds.IsEmpty())
            {
                _logger.LogWarning("No header/detail data found for ItemId={ItemId}", itemId);
                await RouteToFailAsync(config, itemId, context.UserId,
                    SevitaConstants.ErrorImageNotAvailable, history, batchResult, failedRecords, null, ct);
                return;
            }

            var headerRow = ds.Tables[0].Rows[0];
            var detailTable = ds.Tables[1];

            // Extract header fields
            var imagePath = headerRow["ImagePath"]?.ToString() ?? string.Empty;
            var edenredInvoiceId = headerRow["documentId"]?.ToString()?.Trim() ?? string.Empty;
            var supplierName = headerRow["SupplierName"]?.ToString() ?? string.Empty;
            var approverName = headerRow["ApproverName"]?.ToString() ?? string.Empty;
            var invoiceDate = headerRow["InvoiceDate"]?.ToString() ?? string.Empty;
            var isSendNotification = Convert.ToBoolean(headerRow["IsSendNotification"] == DBNull.Value ? false : headerRow["IsSendNotification"]);

            // Step 2: Get image from S3 (Sevita ALWAYS uses S3)
            var base64Image = await GetImageAsync(imagePath, context.S3Config, ct);
            var fileName = Path.GetFileName(imagePath);

            if (base64Image is null)
            {
                _logger.LogWarning(SevitaConstants.LogImageNotAvailable + " ItemId={ItemId}", itemId);
                history.Comment = SevitaConstants.RouteCommentAutomatic + " " + SevitaConstants.ErrorImageNotAvailable;
                await RouteToFailAsync(config, itemId, context.UserId,
                    SevitaConstants.ErrorImageNotAvailable, history, batchResult, failedRecords,
                    new PostFailedRecord
                    {
                        SupplierName = supplierName,
                        ApproverName = approverName,
                        InvoiceDate = invoiceDate,
                        DocumentId = edenredInvoiceId,
                        IsSendNotification = isSendNotification,
                        FailureReason = SevitaConstants.ErrorImageNotAvailable
                    }, ct);
                return;
            }

            // Step 3: Build InvoiceRequest payload
            var lineItems = BuildLineItems(detailTable, edenredInvoiceId);

            var invoiceRequest = new InvoiceRequest
            {
                vendorId = headerRow["vendorId"]?.ToString() ?? string.Empty,
                edenredInvoiceId = edenredInvoiceId,
                employeeId = headerRow["employeeId"]?.ToString() ?? string.Empty,
                payAlone = Convert.ToBoolean(headerRow["payAlone"] == DBNull.Value ? false : headerRow["payAlone"]),
                invoiceRelatedToZycusPurchase = Convert.ToBoolean(headerRow["invoiceRelatedToZycusPurchase"] == DBNull.Value ? false : headerRow["invoiceRelatedToZycusPurchase"]),
                zycusInvoiceNumber = headerRow["zycusInvoiceNumber"]?.ToString(),
                invoiceNumber = headerRow["invoiceNumber"]?.ToString() ?? string.Empty,
                invoiceDate = invoiceDate,
                expensePeriod = headerRow["expensePeriod"]?.ToString() ?? string.Empty,
                checkMemo = headerRow["checkMemo"]?.ToString() ?? string.Empty,
                cerfTrackingNumber = headerRow["cerfTrackingNumber"]?.ToString(),
                remittanceRequired = Convert.ToBoolean(headerRow["remittanceRequired"] == DBNull.Value ? false : headerRow["remittanceRequired"]),
                attachments = new List<AttachmentRequest>
                {
                    new AttachmentRequest
                    {
                        fileName = fileName,
                        fileBase = base64Image,
                        fileUrl = headerRow["fileUrl"]?.ToString() ?? string.Empty,
                        docid = headerRow["docid"]?.ToString() ?? string.Empty
                    }
                },
                lineItems = lineItems
            };

            // Step 4: Validate
            var headerInvoiceAmount = decimal.TryParse(headerRow["InvoiceAmount"]?.ToString(), out var amt) ? amt : 0m;
            var (lineSumValid, lineSumError) = _validationService.ValidateLineSum(invoiceRequest, headerInvoiceAmount);
            if (!lineSumValid)
            {
                _logger.LogWarning(SevitaConstants.LogValidationFailed, itemId, lineSumError);
                history.Comment = SevitaConstants.RouteCommentAutomatic + " " + lineSumError;
                await RouteToFailAsync(config, itemId, context.UserId,
                    lineSumError, history, batchResult, failedRecords,
                    new PostFailedRecord
                    {
                        SupplierName = supplierName,
                        ApproverName = approverName,
                        InvoiceDate = invoiceDate,
                        DocumentId = edenredInvoiceId,
                        IsSendNotification = isSendNotification,
                        FailureReason = lineSumError
                    }, ct);
                return;
            }

            (bool isValid, string validationError) validationResult;
            if (clientConfig.IsPORecord)
                validationResult = _validationService.ValidatePO(invoiceRequest, validIds);
            else
                validationResult = _validationService.ValidateNonPO(invoiceRequest, validIds);

            if (!validationResult.isValid)
            {
                _logger.LogWarning(SevitaConstants.LogValidationFailed, itemId, validationResult.validationError);
                history.Comment = SevitaConstants.RouteCommentAutomatic + " " + validationResult.validationError;
                await RouteToFailAsync(config, itemId, context.UserId,
                    validationResult.validationError, history, batchResult, failedRecords,
                    new PostFailedRecord
                    {
                        SupplierName = supplierName,
                        ApproverName = approverName,
                        InvoiceDate = invoiceDate,
                        DocumentId = edenredInvoiceId,
                        IsSendNotification = isSendNotification,
                        FailureReason = validationResult.validationError
                    }, ct);
                return;
            }

            var (attachValid, attachError) = _validationService.ValidateAttachments(invoiceRequest);
            if (!attachValid)
            {
                _logger.LogWarning(SevitaConstants.LogValidationFailed, itemId, attachError);
                history.Comment = SevitaConstants.RouteCommentAutomatic + " " + attachError;
                await RouteToFailAsync(config, itemId, context.UserId,
                    attachError, history, batchResult, failedRecords,
                    new PostFailedRecord
                    {
                        SupplierName = supplierName,
                        ApproverName = approverName,
                        InvoiceDate = invoiceDate,
                        DocumentId = edenredInvoiceId,
                        IsSendNotification = isSendNotification,
                        FailureReason = attachError
                    }, ct);
                return;
            }

            // Step 5: Serialize payload
            var invoiceRequestJson = SerializePayload(invoiceRequest);
            history.InvoiceRequestJson = invoiceRequestJson;

            // Step 6: Upload audit JSON if configured
            await UploadAuditJsonAsync(invoiceRequestJson, itemId, clientConfig, context.S3Config, ct);

            // Step 7: POST invoice
            var invoiceResponse = await PostInvoiceAsync(config, clientConfig, invoiceRequestJson, context.ProcessManually, ct);
            history.InvoiceResponseJson = invoiceResponse.Result ?? string.Empty;

            if (invoiceResponse.Status != 0)
            {
                history.Comment = SevitaConstants.RouteCommentAutomatic + " " + invoiceResponse.ErrorMsg;
                await RouteToFailAsync(config, itemId, context.UserId,
                    invoiceResponse.ErrorMsg ?? string.Empty, history, batchResult, failedRecords,
                    new PostFailedRecord
                    {
                        SupplierName = supplierName,
                        ApproverName = approverName,
                        InvoiceDate = invoiceDate,
                        DocumentId = edenredInvoiceId,
                        IsSendNotification = isSendNotification,
                        FailureReason = invoiceResponse.ErrorMsg ?? string.Empty
                    }, ct);
                return;
            }

            // Step 8: Success — route to SuccessQueueId
            _logger.LogInformation(SevitaConstants.LogInvoicePostedSuccess, invoiceResponse.InvoiceId);
            history.Comment = SevitaConstants.RouteCommentAutomatic + " Posted successfully. InvoiceId: " + invoiceResponse.InvoiceId;

            await _db.RouteWorkitemAsync(
                itemId, config.SuccessQueueId, context.UserId,
                SevitaConstants.OperationTypePost,
                history.Comment,
                ct);

            await _db.InsertGeneralLogAsync(
                SevitaConstants.OperationTypePost,
                SevitaConstants.SourceObjectContents,
                context.UserId,
                history.Comment,
                itemId,
                ct);

            batchResult.RecordsSuccess++;
            batchResult.ItemResults.Add(new PostItemResult
            {
                ItemId = itemId,
                IsSuccess = true,
                ResponseCode = 201,
                DestinationQueue = config.SuccessQueueId,
                ResponseMessage = history.Comment
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing ItemId={ItemId}", itemId);
            history.Comment = SevitaConstants.RouteCommentAutomatic + " " + ex.Message;
            await RouteToFailAsync(config, itemId, context.UserId,
                ex.Message, history, batchResult, failedRecords, null, ct);
        }
        finally
        {
            // Always save history
            await SaveHistoryAsync(history, ct);

            // Always call UpdateSevitaHeaderPostFields SP
            await _db.UpdateHeaderPostFieldsAsync(itemId, ct);
        }
    }

    /// <summary>
    /// Retrieves the invoice image from S3 as a base64-encoded string.
    /// Virtual to allow test subclasses to override without a real S3 connection.
    /// Returns <c>null</c> when the image cannot be retrieved.
    /// </summary>
    protected virtual Task<string?> GetImageAsync(
        string imagePath,
        EdenredApiUrlConfig s3Config,
        CancellationToken ct)
        => _s3ImageService.GetBase64ImageAsync(imagePath, s3Config, ct);

    private async Task RouteToFailAsync(
        GenericJobConfig config,
        long itemId,
        int userId,
        string errorMessage,
        PostHistory history,
        PostBatchResult batchResult,
        List<PostFailedRecord> failedRecords,
        PostFailedRecord? failedRecord,
        CancellationToken ct)
    {
        var failQueueId = config.PrimaryFailQueueId;
        var comment = string.IsNullOrEmpty(history.Comment)
            ? SevitaConstants.RouteCommentAutomatic + " " + errorMessage
            : history.Comment;

        await _db.RouteWorkitemAsync(
            itemId, failQueueId, userId,
            SevitaConstants.OperationTypePost,
            comment,
            ct);

        await _db.InsertGeneralLogAsync(
            SevitaConstants.OperationTypePost,
            SevitaConstants.SourceObjectContents,
            userId,
            errorMessage,
            itemId,
            ct);

        batchResult.RecordsFailed++;
        batchResult.ItemResults.Add(new PostItemResult
        {
            ItemId = itemId,
            IsSuccess = false,
            DestinationQueue = failQueueId,
            ResponseMessage = errorMessage
        });

        if (failedRecord != null)
            failedRecords.Add(failedRecord);
    }

    private static List<long> ParseItemIds(string itemIds)
    {
        if (string.IsNullOrWhiteSpace(itemIds))
            return new List<long>();

        return itemIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s, out var id) ? id : 0L)
            .Where(id => id > 0)
            .ToList();
    }

    private static string ExtractErrorMessage(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "Unknown error";

        try
        {
            var json = JObject.Parse(content);
            var recordErrors = json["recordErrors"]?.ToString();
            var message = json["message"]?.ToString();
            var invoiceIds = json["invoiceIds"]?.ToString();
            var failedRecords = json["failedRecords"]?.ToString();

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(recordErrors)) parts.Add("recordErrors: " + recordErrors);
            if (!string.IsNullOrWhiteSpace(message)) parts.Add("message: " + message);
            if (!string.IsNullOrWhiteSpace(invoiceIds)) parts.Add("invoiceIds: " + invoiceIds);
            if (!string.IsNullOrWhiteSpace(failedRecords)) parts.Add("failedRecords: " + failedRecords);

            return parts.Count > 0 ? string.Join("; ", parts) : content;
        }
        catch
        {
            return content;
        }
    }
}
