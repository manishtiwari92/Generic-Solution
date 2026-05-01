using System.Data;
using System.Text;
using IPS.AutoPost.Core.Extensions;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Core.Services;
using IPS.AutoPost.Plugins.InvitedClub.Constants;
using IPS.AutoPost.Plugins.InvitedClub.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestSharp;

namespace IPS.AutoPost.Plugins.InvitedClub;

/// <summary>
/// Handles retrying failed image attachment POSTs for InvitedClub invoices.
/// <para>
/// An "orphaned invoice" is one that was successfully created in Oracle Fusion
/// (has an <c>InvoiceId</c>) but whose attachment POST failed, leaving it without
/// an <c>AttachedDocumentId</c>. This service retries those attachment POSTs.
/// </para>
/// <para>
/// Called once per batch via <c>InvitedClubPlugin.OnBeforePostAsync</c> before
/// the main workitem loop begins.
/// </para>
/// </summary>
public class InvitedClubRetryService
{
    private readonly IInvitedClubRetryDataAccess _db;
    private readonly S3ImageService _s3ImageService;
    private readonly ILogger<InvitedClubRetryService> _logger;

    public InvitedClubRetryService(
        IInvitedClubRetryDataAccess db,
        S3ImageService s3ImageService,
        ILogger<InvitedClubRetryService> logger)
    {
        _db = db;
        _s3ImageService = s3ImageService;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // 11.1 RetryPostImagesAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Fetches all orphaned invoices eligible for image retry and attempts to
    /// re-POST the attachment for each one.
    /// <para>
    /// Calls <c>InvitedClub_GetFailedImagesData</c> with:
    /// <list type="bullet">
    ///   <item><c>@HeaderTable</c> — the client-specific header table name</item>
    ///   <item><c>@ImagePostRetryLimit</c> — max retry attempts before giving up</item>
    ///   <item><c>@InvitedFailPostQueueId</c> — the fail queue used to filter eligible records</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="config">Generic job configuration (provides HeaderTable, DefaultUserId, etc.).</param>
    /// <param name="clientConfig">InvitedClub-specific config (provides ImagePostRetryLimit, InvitedFailQueueId, SuccessQueueId).</param>
    /// <param name="s3Config">S3 credentials for image retrieval.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RetryPostImagesAsync(
        GenericJobConfig config,
        InvitedClubConfig clientConfig,
        EdenredApiUrlConfig s3Config,
        CancellationToken ct)
    {
        _logger.LogInformation(InvitedClubConstants.LogRetryPostImagesStarted);

        var failedImagesTable = await _db.GetFailedImagesDataAsync(
            config.HeaderTable,
            clientConfig.ImagePostRetryLimit,
            clientConfig.InvitedFailQueueId,
            ct);

        if (failedImagesTable.Rows.Count == 0)
        {
            _logger.LogInformation(
                "RetryPostImages: No records found to retry for HeaderTable={HeaderTable}",
                config.HeaderTable);
            _logger.LogInformation(InvitedClubConstants.LogRetryPostImagesCompleted);
            return;
        }

        _logger.LogInformation(
            "RetryPostImages: Found {Count} record(s) to retry",
            failedImagesTable.Rows.Count);

        var failedImages = failedImagesTable.ConvertDataTable<FailedImagesData>();

        foreach (var record in failedImages)
        {
            if (ct.IsCancellationRequested)
                break;

            await RetryOneImageAsync(config, clientConfig, s3Config, record, ct);
        }

        _logger.LogInformation(InvitedClubConstants.LogRetryPostImagesCompleted);
    }

    // -----------------------------------------------------------------------
    // 11.2 RetryOneImageAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Retries the attachment POST for a single orphaned invoice record.
    /// <para>
    /// Behaviour:
    /// <list type="bullet">
    ///   <item>Gets the image from S3 (non-legacy) or local file system (legacy).</item>
    ///   <item>POSTs the attachment to Oracle Fusion with
    ///     <c>Content-Type: application/vnd.oracle.adf.resourceitem+json</c>.</item>
    ///   <item>On HTTP 201: updates <c>AttachedDocumentId</c> on the header row and
    ///     routes the workitem to <c>SuccessQueueId</c>.</item>
    ///   <item>Always increments <c>ImagePostRetryCount</c> regardless of outcome.</item>
    ///   <item>Always uses <c>config.DefaultUserId</c> for routing.</item>
    ///   <item>Always uses <c>"Automatic Route:"</c> as the routing comment prefix.</item>
    /// </list>
    /// </para>
    /// </summary>
    public virtual async Task RetryOneImageAsync(
        GenericJobConfig config,
        InvitedClubConfig clientConfig,
        EdenredApiUrlConfig s3Config,
        FailedImagesData record,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "RetryPostImages: Retrying image for ItemId={ItemId}, InvoiceId={InvoiceId}, RetryCount={RetryCount}",
            record.ItemId, record.InvoiceId, record.ImagePostRetryCount);

        AttachmentResponse attachmentResponse;

        try
        {
            // Step 1: Retrieve image (S3 for non-legacy, local file for legacy)
            var (base64Image, fileName, imageFailed) = await GetImageAsync(
                config, s3Config, record.ImagePath, ct);

            if (imageFailed || string.IsNullOrEmpty(base64Image))
            {
                _logger.LogWarning(
                    "RetryPostImages: Image not available for ItemId={ItemId}, ImagePath={ImagePath}",
                    record.ItemId, record.ImagePath);

                // Still increment retry count even when image is unavailable
                await _db.IncrementImagePostRetryCountAsync(record.ItemId, config.HeaderTable, ct);
                return;
            }

            // Step 2: Build attachment request
            var attachmentRequest = new AttachmentRequest
            {
                Type         = InvitedClubConstants.AttachmentType,
                FileName     = fileName,
                Title        = fileName,
                Category     = InvitedClubConstants.AttachmentCategory,
                FileContents = base64Image
            };

            // Step 3: POST attachment to Oracle Fusion
            attachmentResponse = await PostAttachmentAsync(
                config, record.InvoiceId, attachmentRequest, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "RetryPostImages: Unhandled exception for ItemId={ItemId}",
                record.ItemId);

            // Always increment retry count even on unexpected exceptions
            await _db.IncrementImagePostRetryCountAsync(record.ItemId, config.HeaderTable, ct);
            return;
        }

        // Step 4: Always increment retry count (success or failure)
        await _db.IncrementImagePostRetryCountAsync(record.ItemId, config.HeaderTable, ct);

        // Step 5: On success (HTTP 201) — update AttachedDocumentId and route to success queue
        if (attachmentResponse.Status == 0)
        {
            _logger.LogInformation(
                InvitedClubConstants.LogImagePostedSuccess,
                attachmentResponse.AttachedDocumentId);

            await _db.UpdateAttachedDocumentIdAsync(
                record.ItemId,
                attachmentResponse.AttachedDocumentId,
                config.HeaderTable,
                ct);

            await _db.RouteWorkitemAsync(
                record.ItemId,
                config.SuccessQueueId,
                config.DefaultUserId,
                InvitedClubConstants.OperationTypePost,
                $"{InvitedClubConstants.RouteCommentAutomatic} {attachmentResponse.Result}",
                ct);
        }
        else
        {
            _logger.LogWarning(
                "RetryPostImages: Attachment POST failed for ItemId={ItemId}. Error: {Error}",
                record.ItemId, attachmentResponse.ErrorMsg);
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Retrieves the invoice image as a base64 string.
    /// Non-legacy jobs: fetches from S3 using the image path as the S3 key.
    /// Legacy jobs: reads from the local file system using
    ///   <c>{config.ImageParentPath}{record.ImagePath}</c>.
    /// Returns (base64, fileName, failed) where <c>failed = true</c> means the image
    /// could not be retrieved.
    /// </summary>
    private async Task<(string? Base64, string FileName, bool Failed)> GetImageAsync(
        GenericJobConfig config,
        EdenredApiUrlConfig s3Config,
        string imagePath,
        CancellationToken ct)
    {
        var fileName = Path.GetFileName(imagePath);

        if (config.IsLegacyJob)
        {
            // Legacy: read from local file system
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
                _logger.LogWarning(ex, "RetryPostImages: Failed to read local image file: {FullPath}", fullPath);
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

    /// <summary>
    /// POSTs the attachment to Oracle Fusion at
    /// <c>{PostServiceURL}/{invoiceId}/child/attachments</c>.
    /// Uses Basic Auth, no timeout, and
    /// <c>Content-Type: application/vnd.oracle.adf.resourceitem+json</c>.
    /// Returns an <see cref="AttachmentResponse"/> with Status=0 on HTTP 201,
    /// Status=-1 on any other response.
    /// </summary>
    private async Task<AttachmentResponse> PostAttachmentAsync(
        GenericJobConfig config,
        string invoiceId,
        AttachmentRequest attachmentRequest,
        CancellationToken ct)
    {
        var attachmentUrl = $"{config.PostServiceUrl}{invoiceId}{InvitedClubConstants.AttachmentUriSuffix}";

        _logger.LogInformation(
            InvitedClubConstants.LogResponseCode,
            "Attachment Retry",
            $"Posting to {attachmentUrl}");

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
            "Attachment Retry",
            (int)response.StatusCode);

        if ((int)response.StatusCode == 201)
        {
            // Extract AttachedDocumentId from the response JSON
            var attachedDocumentId = string.Empty;
            if (!string.IsNullOrWhiteSpace(response.Content))
            {
                try
                {
                    var json = Newtonsoft.Json.Linq.JObject.Parse(response.Content);
                    attachedDocumentId = json["AttachedDocumentId"]?.ToString() ?? string.Empty;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RetryPostImages: Failed to parse AttachedDocumentId from response");
                }
            }

            return new AttachmentResponse
            {
                Status              = 0,
                AttachedDocumentId  = attachedDocumentId,
                Result              = response.Content ?? string.Empty
            };
        }

        return new AttachmentResponse
        {
            Status   = -1,
            Result   = response.Content ?? string.Empty,
            ErrorMsg = $"HTTP {(int)response.StatusCode}: {response.Content}"
        };
    }

    private static string BuildBasicAuthHeader(string username, string password)
    {
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{username}:{password}"));
        return $"Basic {credentials}";
    }
}
