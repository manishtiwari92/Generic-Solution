using System.Data;
using System.Text;
using IPS.AutoPost.Core.Extensions;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Plugins.InvitedClub.Constants;
using IPS.AutoPost.Plugins.InvitedClub.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestSharp;

namespace IPS.AutoPost.Plugins.InvitedClub;

/// <summary>
/// Implements the InvitedClub feed download strategy:
/// suppliers → supplier addresses → supplier sites → supplier CSV export → COA.
/// </summary>
public class InvitedClubFeedStrategy
{
    private readonly IEmailService _emailService;
    private readonly ILogger<InvitedClubFeedStrategy> _logger;
    private readonly IInvitedClubFeedDataAccess _db;

    public InvitedClubFeedStrategy(
        IInvitedClubFeedDataAccess db,
        IEmailService emailService,
        ILogger<InvitedClubFeedStrategy> logger)
    {
        _db = db;
        _emailService = emailService;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // 10.1 LoadSupplierAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Fetches all suppliers from Oracle Fusion using paginated GET requests.
    /// Uses Basic Auth and no timeout (InvitedClubConstants.ApiTimeoutMs = -1).
    /// </summary>
    public virtual async Task<List<SupplierResponse>> LoadSupplierAsync(
        GenericJobConfig config,
        CancellationToken ct)
    {
        _logger.LogInformation(InvitedClubConstants.LogLoadSupplierStarted);

        var allSuppliers = new List<SupplierResponse>();
        var offset = 0;
        var hasMore = true;

        var clientOptions = new RestClientOptions(config.PostServiceUrl)
        {
            MaxTimeout = InvitedClubConstants.ApiTimeoutMs
        };
        using var client = new RestClient(clientOptions);

        var authHeader = BuildBasicAuthHeader(config.AuthUsername, config.AuthPassword);

        while (hasMore)
        {
            var uri = $"{InvitedClubConstants.SupplierUri}&limit={InvitedClubConstants.ApiPageSize}&offset={offset}";
            var request = new RestRequest(uri);
            request.AddHeader("Authorization", authHeader);

            var response = await client.ExecuteAsync<SupplierData>(request, ct);

            if (response.Data?.Items is { Count: > 0 })
                allSuppliers.AddRange(response.Data.Items);

            hasMore = response.Data?.HasMore ?? false;
            offset += InvitedClubConstants.ApiPageSize;
        }

        _logger.LogInformation(InvitedClubConstants.LogLoadSupplierCompleted);
        return allSuppliers;
    }

    // -----------------------------------------------------------------------
    // 10.2 IsInitialCallAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns <c>true</c> when the specified table is empty (full refresh needed).
    /// Returns <c>false</c> when the table already has data (incremental update).
    /// </summary>
    public virtual async Task<bool> IsInitialCallAsync(string tableName, CancellationToken ct)
    {
        var count = await _db.GetTableCountAsync(tableName, ct);
        return count == 0;
    }

    // -----------------------------------------------------------------------
    // 10.3 LoadSupplierAddressAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Fetches supplier addresses from Oracle Fusion.
    /// On initial call: fetches addresses for ALL supplier IDs.
    /// On incremental call: only fetches for suppliers updated within the last
    /// <c>LastSupplierDownloadTime - 2 days</c> window.
    /// Injects <c>SupplierId</c> into each item after deserialization.
    /// </summary>
    public virtual async Task<List<SupplierAddressResponse>> LoadSupplierAddressAsync(
        GenericJobConfig config,
        InvitedClubConfig clientConfig,
        List<SupplierResponse> suppliers,
        CancellationToken ct)
    {
        _logger.LogInformation(InvitedClubConstants.LogLoadSupplierAddressStarted);

        var isInitial = await IsInitialCallAsync(InvitedClubConstants.SupplierAddressTableName, ct);

        IEnumerable<SupplierResponse> suppliersToFetch;
        if (isInitial)
        {
            suppliersToFetch = suppliers;
        }
        else
        {
            var cutoff = clientConfig.LastSupplierDownloadTime.AddDays(-2);
            suppliersToFetch = suppliers.Where(s =>
                DateTime.TryParse(s.LastUpdateDate, out var lastUpdate) && lastUpdate >= cutoff);
        }

        var allAddresses = new List<SupplierAddressResponse>();
        var authHeader = BuildBasicAuthHeader(config.AuthUsername, config.AuthPassword);

        var clientOptions = new RestClientOptions(config.PostServiceUrl)
        {
            MaxTimeout = InvitedClubConstants.ApiTimeoutMs
        };
        using var client = new RestClient(clientOptions);

        foreach (var supplier in suppliersToFetch)
        {
            var addresses = await FetchPagedAddressesAsync(
                client, authHeader, supplier.SupplierId, ct);
            allAddresses.AddRange(addresses);
        }

        _logger.LogInformation(InvitedClubConstants.LogLoadSupplierAddressCompleted);
        return allAddresses;
    }

    private static async Task<List<SupplierAddressResponse>> FetchPagedAddressesAsync(
        RestClient client,
        string authHeader,
        string supplierId,
        CancellationToken ct)
    {
        var results = new List<SupplierAddressResponse>();
        var offset = 0;
        var hasMore = true;

        while (hasMore)
        {
            var uri = $"{InvitedClubConstants.SupplierAddressUriPrefix}{supplierId}" +
                      $"{InvitedClubConstants.SupplierAddressUriSuffix}" +
                      $"&limit={InvitedClubConstants.ApiPageSize}&offset={offset}";

            var request = new RestRequest(uri);
            request.AddHeader("Authorization", authHeader);

            var response = await client.ExecuteAsync<SupplierAddressData>(request, ct);

            if (response.Data?.Items is { Count: > 0 })
            {
                foreach (var item in response.Data.Items)
                    item.SupplierId = supplierId;

                results.AddRange(response.Data.Items);
            }

            hasMore = response.Data?.HasMore ?? false;
            offset += InvitedClubConstants.ApiPageSize;
        }

        return results;
    }

    // -----------------------------------------------------------------------
    // 10.4 LoadSupplierSiteAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Fetches supplier sites from Oracle Fusion.
    /// Same pattern as <see cref="LoadSupplierAddressAsync"/>.
    /// After bulk insert, calls <c>InvitedClub_UpdateSupplierSiteInSupplierAddress</c> SP.
    /// </summary>
    public virtual async Task<List<SupplierSiteResponse>> LoadSupplierSiteAsync(
        GenericJobConfig config,
        InvitedClubConfig clientConfig,
        List<SupplierResponse> suppliers,
        CancellationToken ct)
    {
        _logger.LogInformation(InvitedClubConstants.LogLoadSupplierSiteStarted);

        var isInitial = await IsInitialCallAsync(InvitedClubConstants.SupplierSiteTableName, ct);

        IEnumerable<SupplierResponse> suppliersToFetch;
        if (isInitial)
        {
            suppliersToFetch = suppliers;
        }
        else
        {
            var cutoff = clientConfig.LastSupplierDownloadTime.AddDays(-2);
            suppliersToFetch = suppliers.Where(s =>
                DateTime.TryParse(s.LastUpdateDate, out var lastUpdate) && lastUpdate >= cutoff);
        }

        var allSites = new List<SupplierSiteResponse>();
        var authHeader = BuildBasicAuthHeader(config.AuthUsername, config.AuthPassword);

        var clientOptions = new RestClientOptions(config.PostServiceUrl)
        {
            MaxTimeout = InvitedClubConstants.ApiTimeoutMs
        };
        using var client = new RestClient(clientOptions);

        foreach (var supplier in suppliersToFetch)
        {
            var sites = await FetchPagedSitesAsync(
                client, authHeader, supplier.SupplierId, ct);
            allSites.AddRange(sites);
        }

        _logger.LogInformation(InvitedClubConstants.LogLoadSupplierSiteCompleted);
        return allSites;
    }

    private static async Task<List<SupplierSiteResponse>> FetchPagedSitesAsync(
        RestClient client,
        string authHeader,
        string supplierId,
        CancellationToken ct)
    {
        var results = new List<SupplierSiteResponse>();
        var offset = 0;
        var hasMore = true;

        while (hasMore)
        {
            var uri = $"{InvitedClubConstants.SupplierSiteUriPrefix}{supplierId}" +
                      $"{InvitedClubConstants.SupplierSiteUriSuffix}" +
                      $"&limit={InvitedClubConstants.ApiPageSize}&offset={offset}";

            var request = new RestRequest(uri);
            request.AddHeader("Authorization", authHeader);

            var response = await client.ExecuteAsync<SupplierSiteData>(request, ct);

            if (response.Data?.Items is { Count: > 0 })
            {
                foreach (var item in response.Data.Items)
                    item.SupplierId = supplierId;

                results.AddRange(response.Data.Items);
            }

            hasMore = response.Data?.HasMore ?? false;
            offset += InvitedClubConstants.ApiPageSize;
        }

        return results;
    }

    // -----------------------------------------------------------------------
    // 10.5 BulkInsertAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Full refresh (initial call): TRUNCATE TABLE, then bulk copy.
    /// Incremental: DELETE WHERE SupplierId IN (...), then bulk copy.
    /// </summary>
    public virtual async Task BulkInsertAsync<T>(
        string tableName,
        List<T> items,
        bool isInitial,
        IEnumerable<string>? supplierIds = null,
        CancellationToken ct = default)
    {
        if (isInitial)
        {
            await _db.TruncateTableAsync(tableName, ct);
        }
        else if (supplierIds is not null)
        {
            var ids = supplierIds.ToList();
            if (ids.Count > 0)
                await _db.DeleteBySupplierIdsAsync(tableName, ids, ct);
        }

        if (items.Count > 0)
        {
            var dataTable = items.ToDataTable();
            await _db.BulkCopyAsync(tableName, dataTable, ct);
        }
    }

    // -----------------------------------------------------------------------
    // 10.6 LoadCOAAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Fetches all COA records from Oracle Fusion (full refresh only).
    /// Truncates <c>InvitedClubCOA</c>, bulk inserts all pages, writes a
    /// pipe-delimited CSV, then checks for missing CodeCombinationIds and
    /// sends an email if any are found.
    /// Returns the total count of COA records downloaded.
    /// </summary>
    public virtual async Task<int> LoadCOAAsync(
        GenericJobConfig config,
        CancellationToken ct)
    {
        _logger.LogInformation(InvitedClubConstants.LogLoadCoaStarted);

        var allCoa = new List<COAResponse>();
        var offset = 0;
        var hasMore = true;

        var clientOptions = new RestClientOptions(config.PostServiceUrl)
        {
            MaxTimeout = InvitedClubConstants.ApiTimeoutMs
        };
        using var client = new RestClient(clientOptions);

        var authHeader = BuildBasicAuthHeader(config.AuthUsername, config.AuthPassword);

        while (hasMore)
        {
            var uri = $"{InvitedClubConstants.CoaUri}&limit={InvitedClubConstants.ApiPageSize}&offset={offset}";
            var request = new RestRequest(uri);
            request.AddHeader("Authorization", authHeader);

            var response = await client.ExecuteAsync(request, ct);

            if (response.Content is not null)
            {
                var coaData = JsonConvert.DeserializeObject<COAData>(response.Content);
                if (coaData?.Items is { Count: > 0 })
                    allCoa.AddRange(coaData.Items);

                hasMore = coaData?.HasMore ?? false;
            }
            else
            {
                hasMore = false;
            }

            offset += InvitedClubConstants.ApiPageSize;
        }

        // Full refresh: truncate then bulk insert
        await _db.TruncateTableAsync(InvitedClubConstants.CoaTableName, ct);

        if (allCoa.Count > 0)
        {
            var dataTable = allCoa.ToDataTable();
            await _db.BulkCopyAsync(InvitedClubConstants.CoaTableName, dataTable, ct);
        }

        // Write pipe-delimited CSV
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var csvDir = Path.Combine(config.FeedDownloadPath, "COA");
        var csvPath = Path.Combine(csvDir, $"COA_{timestamp}.csv");
        Directory.CreateDirectory(csvDir);
        await WritePipeDelimitedCsvAsync(allCoa, csvPath);

        // Check for missing CodeCombinationIds
        var missingIds = await _db.GetMissingCOAIdsAsync(ct);
        if (missingIds.Count > 0)
        {
            await SendCOAMissingEmailAsync(config, missingIds, ct);
        }

        _logger.LogInformation(InvitedClubConstants.LogLoadCoaCompleted);
        return allCoa.Count;
    }

    private async Task SendCOAMissingEmailAsync(
        GenericJobConfig config,
        List<string> missingIds,
        CancellationToken ct)
    {
        var emailConfigTable = await _db.GetEmailConfigAsync(config.Id, ct);
        var emailConfigs = emailConfigTable.ConvertDataTable<EmailConfig>();

        if (emailConfigs.Count == 0)
            return;

        var emailConfig = emailConfigs[0];

        // Split semicolon-delimited recipient strings
        var toArr = SplitEmails(emailConfig.EmailTo);
        var ccArr = SplitEmails(emailConfig.EmailCC);
        var bccArr = SplitEmails(emailConfig.EmailBCC);

        await _emailService.SendAsync(
            smtpServer: emailConfig.SMTPServer,
            smtpPort: emailConfig.SMTPServerPort,
            fromAddress: emailConfig.EmailFrom,
            fromDisplayName: emailConfig.EmailFromUser,
            toAddresses: toArr,
            ccAddresses: ccArr,
            bccAddresses: bccArr,
            subject: emailConfig.EmailSubject,
            htmlBody: emailConfig.EmailTemplate,
            useSsl: emailConfig.SMTPUseSSL,
            smtpUsername: emailConfig.Username,
            smtpPassword: emailConfig.Password,
            ct: ct);
    }

    // -----------------------------------------------------------------------
    // 10.7 ExportSupplierCsvAsync
    // -----------------------------------------------------------------------

    /// <summary>
    /// Calls <c>InvitedClub_GetSupplierDataToExport</c> SP and writes a
    /// pipe-delimited CSV to <c>{FeedDownloadPath}\Supplier\Supplier_{timestamp}.csv</c>.
    /// After a successful write, calls <c>UpdateSupplierLastDownloadTime</c> SP.
    /// </summary>
    public virtual async Task ExportSupplierCsvAsync(
        GenericJobConfig config,
        InvitedClubConfig clientConfig,
        CancellationToken ct)
    {
        var ds = await _db.GetSupplierDataToExportAsync(ct);

        if (ds.IsEmpty())
            return;

        var dt = ds.Tables[0];
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var csvDir = Path.Combine(config.FeedDownloadPath, "Supplier");
        var csvPath = Path.Combine(csvDir, $"Supplier_{timestamp}.csv");
        Directory.CreateDirectory(csvDir);

        await WriteDataTableToCsvAsync(dt, csvPath);

        // Update last download time in post_to_invitedclub_configuration
        await _db.ExecuteUpdateLastDownloadTimeAsync(config.Id, ct);
    }

    // -----------------------------------------------------------------------
    // 10.8 ExecuteAsync (IClientPlugin.ExecuteFeedDownloadAsync implementation)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Orchestrates all feed download steps in order and returns a <see cref="FeedResult"/>.
    /// </summary>
    public async Task<FeedResult> ExecuteAsync(
        GenericJobConfig config,
        FeedContext context,
        CancellationToken ct)
    {
        _logger.LogInformation(InvitedClubConstants.LogDownloadFeedStarted);

        try
        {
            var clientConfig = config.GetClientConfig<InvitedClubConfig>();

            // Step 1: Load suppliers
            var suppliers = await LoadSupplierAsync(config, ct);
            var isSupplierInitial = await IsInitialCallAsync(InvitedClubConstants.SupplierTableName, ct);
            var supplierIds = suppliers.Select(s => s.SupplierId);
            await BulkInsertAsync(
                InvitedClubConstants.SupplierTableName,
                suppliers,
                isSupplierInitial,
                isSupplierInitial ? null : supplierIds,
                ct);

            // Step 2: Load supplier addresses
            var addresses = await LoadSupplierAddressAsync(config, clientConfig, suppliers, ct);
            var isAddressInitial = await IsInitialCallAsync(InvitedClubConstants.SupplierAddressTableName, ct);
            var addressSupplierIds = addresses.Select(a => a.SupplierId).Distinct();
            await BulkInsertAsync(
                InvitedClubConstants.SupplierAddressTableName,
                addresses,
                isAddressInitial,
                isAddressInitial ? null : addressSupplierIds,
                ct);

            // Step 3: Load supplier sites
            var sites = await LoadSupplierSiteAsync(config, clientConfig, suppliers, ct);
            var isSiteInitial = await IsInitialCallAsync(InvitedClubConstants.SupplierSiteTableName, ct);
            var siteSupplierIds = sites.Select(s => s.SupplierId).Distinct();
            await BulkInsertAsync(
                InvitedClubConstants.SupplierSiteTableName,
                sites,
                isSiteInitial,
                isSiteInitial ? null : siteSupplierIds,
                ct);

            // After site insert: update SupplierSite column in SupplierAddress
            await _db.ExecuteNonQuerySpAsync(
                InvitedClubConstants.SpUpdateSupplierSiteInSupplierAddress, ct);

            // Step 4: Export supplier CSV and update last download time
            await ExportSupplierCsvAsync(config, clientConfig, ct);

            // Step 5: Load COA
            var coaCount = await LoadCOAAsync(config, ct);

            var totalRecords = suppliers.Count + addresses.Count + sites.Count + coaCount;

            _logger.LogInformation(InvitedClubConstants.LogDownloadFeedCompleted);
            return FeedResult.Succeeded(totalRecords);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InvitedClub feed download failed: {Message}", ex.Message);
            return FeedResult.Failed(ex.Message);
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

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

    private static async Task WritePipeDelimitedCsvAsync<T>(List<T> items, string filePath)
    {
        if (items.Count == 0)
        {
            await File.WriteAllTextAsync(filePath, string.Empty);
            return;
        }

        var properties = typeof(T).GetProperties();
        await using var writer = new StreamWriter(filePath, append: false, encoding: Encoding.UTF8);

        // Header row
        await writer.WriteLineAsync(string.Join("|", properties.Select(p => p.Name)));

        // Data rows
        foreach (var item in items)
        {
            var values = properties.Select(p => p.GetValue(item)?.ToString() ?? string.Empty);
            await writer.WriteLineAsync(string.Join("|", values));
        }
    }

    private static async Task WriteDataTableToCsvAsync(DataTable dt, string filePath)
    {
        await using var writer = new StreamWriter(filePath, append: false, encoding: Encoding.UTF8);

        // Header row
        var headers = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
        await writer.WriteLineAsync(string.Join("|", headers));

        // Data rows
        foreach (DataRow row in dt.Rows)
        {
            var values = dt.Columns.Cast<DataColumn>()
                .Select(c => row[c]?.ToString() ?? string.Empty);
            await writer.WriteLineAsync(string.Join("|", values));
        }
    }
}
