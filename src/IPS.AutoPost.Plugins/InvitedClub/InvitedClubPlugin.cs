using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Plugins.InvitedClub.Models;
using Microsoft.Extensions.Logging;

namespace IPS.AutoPost.Plugins.InvitedClub;

/// <summary>
/// InvitedClub client plugin — integrates with Oracle Fusion Cloud Payables via REST API
/// using Basic Authentication.
/// <para>
/// Lifecycle per batch:
/// <list type="number">
///   <item><see cref="OnBeforePostAsync"/> — retries any orphaned invoice image attachments.</item>
///   <item><see cref="ExecutePostAsync"/> — posts invoices, attachments, and calculateTax calls.</item>
///   <item><see cref="ExecuteFeedDownloadAsync"/> — downloads suppliers, addresses, sites, and COA.</item>
/// </list>
/// </para>
/// </summary>
public class InvitedClubPlugin : IClientPlugin
{
    private readonly InvitedClubRetryService _retryService;
    private readonly InvitedClubPostStrategy _postStrategy;
    private readonly InvitedClubFeedStrategy _feedStrategy;
    private readonly ILogger<InvitedClubPlugin> _logger;

    /// <inheritdoc />
    public string ClientType => "INVITEDCLUB";

    public InvitedClubPlugin(
        InvitedClubRetryService retryService,
        InvitedClubPostStrategy postStrategy,
        InvitedClubFeedStrategy feedStrategy,
        ILogger<InvitedClubPlugin> logger)
    {
        _retryService = retryService;
        _postStrategy = postStrategy;
        _feedStrategy = feedStrategy;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // 13.1 OnBeforePostAsync — retry orphaned image attachments before the main loop
    // -----------------------------------------------------------------------

    /// <summary>
    /// Called ONCE before the workitem loop begins.
    /// Retries any orphaned invoice image attachments (invoices that have an
    /// <c>InvoiceId</c> but no <c>AttachedDocumentId</c>) by delegating to
    /// <see cref="InvitedClubRetryService.RetryPostImagesAsync"/>.
    /// </summary>
    public async Task OnBeforePostAsync(GenericJobConfig config, CancellationToken ct)
    {
        _logger.LogInformation(
            "[{ClientType}] OnBeforePostAsync started for Job {JobId}",
            ClientType, config.JobId);

        var clientConfig = config.GetClientConfig<InvitedClubConfig>();

        // S3 config is loaded from EdenredApiUrlConfig at orchestrator level and
        // passed via PostContext. For the retry service we need it separately —
        // the orchestrator passes it through the context, but OnBeforePostAsync
        // only receives GenericJobConfig. We use a default EdenredApiUrlConfig here;
        // the retry service will use the S3 credentials from the config's client_config_json
        // or fall back to the legacy local-file path when IsLegacyJob = true.
        var s3Config = new EdenredApiUrlConfig();

        await _retryService.RetryPostImagesAsync(config, clientConfig, s3Config, ct);

        _logger.LogInformation(
            "[{ClientType}] OnBeforePostAsync completed for Job {JobId}",
            ClientType, config.JobId);
    }

    // -----------------------------------------------------------------------
    // 13.2 ExecutePostAsync — delegates to InvitedClubPostStrategy
    // -----------------------------------------------------------------------

    /// <summary>
    /// Processes all workitems in the batch by delegating to
    /// <see cref="InvitedClubPostStrategy.ExecuteAsync"/>.
    /// Each workitem goes through: GetImage → BuildInvoiceRequest → PostInvoice →
    /// PostAttachment → PostCalculateTax (if UseTax=YES).
    /// </summary>
    public Task<PostBatchResult> ExecutePostAsync(
        GenericJobConfig config,
        PostContext context,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "[{ClientType}] ExecutePostAsync started for Job {JobId}",
            ClientType, config.JobId);

        return _postStrategy.ExecuteAsync(config, context, ct);
    }

    // -----------------------------------------------------------------------
    // 13.3 ExecuteFeedDownloadAsync — delegates to InvitedClubFeedStrategy
    // -----------------------------------------------------------------------

    /// <summary>
    /// Downloads feed data by delegating to
    /// <see cref="InvitedClubFeedStrategy.ExecuteAsync"/>.
    /// Steps: LoadSupplier → LoadSupplierAddress → LoadSupplierSite →
    /// ExportSupplierCsv → LoadCOA.
    /// </summary>
    public Task<FeedResult> ExecuteFeedDownloadAsync(
        GenericJobConfig config,
        FeedContext context,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "[{ClientType}] ExecuteFeedDownloadAsync started for Job {JobId}",
            ClientType, config.JobId);

        return _feedStrategy.ExecuteAsync(config, context, ct);
    }
}
