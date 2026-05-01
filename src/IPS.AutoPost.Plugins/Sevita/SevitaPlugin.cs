using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Plugins.Sevita.Constants;
using IPS.AutoPost.Plugins.Sevita.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace IPS.AutoPost.Plugins.Sevita;

/// <summary>
/// Sevita client plugin — integrates with the Sevita AP system via REST API
/// using OAuth2 client_credentials authentication.
/// <para>
/// Lifecycle per batch:
/// <list type="number">
///   <item>
///     <see cref="OnBeforePostAsync"/> — loads <see cref="ValidIds"/> (VendorIds +
///     EmployeeIds) from <c>Sevita_Supplier_SiteInformation_Feed</c> and
///     <c>Sevita_Employee_Feed</c> in a single round-trip using raw ADO.NET
///     <c>SqlDataReader.NextResult()</c>.
///   </item>
///   <item>
///     <see cref="ExecutePostAsync"/> — validates, builds payload, uploads audit JSON,
///     posts invoice, routes workitem, saves history, calls
///     <c>UpdateSevitaHeaderPostFields</c>.
///   </item>
///   <item>
///     <see cref="ExecuteFeedDownloadAsync"/> — returns
///     <see cref="FeedResult.NotApplicable()"/> (Sevita has no feed download step).
///   </item>
///   <item>
///     <see cref="ClearPostInProcessAsync"/> — overrides the default direct SQL UPDATE
///     to call <c>UpdateSevitaHeaderPostFields(@UID)</c> SP instead.
///   </item>
/// </list>
/// </para>
/// </summary>
public class SevitaPlugin : IClientPlugin
{
    private readonly SevitaPostStrategy _postStrategy;
    private readonly ILogger<SevitaPlugin> _logger;

    // ValidIds is loaded once per batch in OnBeforePostAsync and shared across
    // all workitems in the batch via ExecutePostAsync.
    protected ValidIds _validIds = new();

    /// <inheritdoc />
    public string ClientType => "SEVITA";

    public SevitaPlugin(
        SevitaPostStrategy postStrategy,
        ILogger<SevitaPlugin> logger)
    {
        _postStrategy = postStrategy;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // 17.1 OnBeforePostAsync — load ValidIds from Sevita feed tables
    // -----------------------------------------------------------------------

    /// <summary>
    /// Called ONCE before the workitem loop begins.
    /// Loads <see cref="ValidIds"/> (VendorIds from <c>Sevita_Supplier_SiteInformation_Feed</c>
    /// and EmployeeIds from <c>Sevita_Employee_Feed</c>) in a single database round-trip
    /// using raw ADO.NET <c>SqlDataReader.NextResult()</c>.
    /// </summary>
    /// <remarks>
    /// Uses raw ADO.NET instead of <c>SqlHelper.ExecuteDatasetAsync</c> because
    /// <c>SqlHelper</c> does not support multi-statement queries with <c>NextResult()</c>.
    /// The two SELECT statements are batched in a single command to minimise round-trips.
    /// </remarks>
    public async Task OnBeforePostAsync(GenericJobConfig config, CancellationToken ct)
    {
        _logger.LogInformation(
            "[{ClientType}] OnBeforePostAsync started for Job {JobId} — loading ValidIds",
            ClientType, config.JobId);

        _logger.LogInformation(SevitaConstants.LogLoadValidIdsStarted);

        _validIds = await LoadValidIdsAsync(config.DbConnectionString, ct);

        _logger.LogInformation(
            SevitaConstants.LogLoadValidIdsCompleted,
            _validIds.VendorIds.Count,
            _validIds.EmployeeIds.Count);

        _logger.LogInformation(
            "[{ClientType}] OnBeforePostAsync completed for Job {JobId}",
            ClientType, config.JobId);
    }

    // -----------------------------------------------------------------------
    // 17.2 ExecutePostAsync — delegates to SevitaPostStrategy, passes _validIds
    // -----------------------------------------------------------------------

    /// <summary>
    /// Processes all workitems in the batch by delegating to
    /// <see cref="SevitaPostStrategy.ExecuteAsync"/>.
    /// Passes the <see cref="ValidIds"/> loaded in <see cref="OnBeforePostAsync"/>
    /// so that each workitem can be validated against the same in-memory set.
    /// </summary>
    public async Task<PostBatchResult> ExecutePostAsync(
        GenericJobConfig config,
        PostContext context,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "[{ClientType}] ExecutePostAsync started for Job {JobId}",
            ClientType, config.JobId);

        // Load email / notification configuration from the database.
        // These are loaded here (not in OnBeforePostAsync) because they are only
        // needed for the post flow, not for feed downloads.
        var (failedPostConfig, emailConfig) = await LoadEmailConfigAsync(config.DbConnectionString, ct);

        return await _postStrategy.ExecuteAsync(
            config,
            context,
            _validIds,
            failedPostConfig,
            emailConfig,
            ct);
    }

    // -----------------------------------------------------------------------
    // ExecuteFeedDownloadAsync — Sevita has no feed download step
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sevita has no feed download step.
    /// Returns <see cref="FeedResult.NotApplicable()"/> so the Core_Engine skips
    /// feed processing for this plugin.
    /// </summary>
    public Task<FeedResult> ExecuteFeedDownloadAsync(
        GenericJobConfig config,
        FeedContext context,
        CancellationToken ct)
        => Task.FromResult(FeedResult.NotApplicable());

    // -----------------------------------------------------------------------
    // 17.3 ClearPostInProcessAsync — override to call UpdateSevitaHeaderPostFields SP
    // -----------------------------------------------------------------------

    /// <summary>
    /// Overrides the default <c>UPDATE {header_table} SET PostInProcess=0</c> behaviour.
    /// Calls <c>UpdateSevitaHeaderPostFields(@UID)</c> stored procedure instead,
    /// which clears <c>PostInProcess</c> and updates additional post fields on the
    /// Sevita header record in a single SP call.
    /// </summary>
    /// <remarks>
    /// This override is called from a <c>finally</c> block in
    /// <see cref="SevitaPostStrategy"/> via <see cref="ISevitaPostDataAccess.UpdateHeaderPostFieldsAsync"/>.
    /// The <see cref="IRoutingRepository"/> parameter is not used here — the SP call
    /// is made directly through <see cref="ISevitaPostDataAccess"/> which is already
    /// wired to the correct connection string.
    /// </remarks>
    public async Task ClearPostInProcessAsync(
        long itemId,
        GenericJobConfig config,
        IRoutingRepository routingRepo,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "[{ClientType}] ClearPostInProcessAsync — calling {Sp} for ItemId={ItemId}",
            ClientType, SevitaConstants.SpUpdateHeaderPostFields, itemId);

        // Delegate to the routing repository's ExecuteSpAsync which calls the SP
        // with the @UID parameter. This keeps the plugin decoupled from SqlHelper directly.
        await routingRepo.ExecuteSpAsync(
            SevitaConstants.SpUpdateHeaderPostFields,
            config.DbConnectionString,
            ct,
            new SqlParameter("@UID", System.Data.SqlDbType.BigInt) { Value = itemId });
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Loads VendorIds and EmployeeIds from the Sevita feed tables in a single
    /// database round-trip using raw ADO.NET with <c>SqlDataReader.NextResult()</c>.
    /// Virtual to allow test subclasses to override without a real SQL connection.
    /// </summary>
    protected virtual async Task<ValidIds> LoadValidIdsAsync(
        string connectionString,
        CancellationToken ct)
    {
        var validIds = new ValidIds();

        const string sql =
            $"SELECT Supplier FROM {SevitaConstants.SupplierFeedTable};" +
            $"SELECT EmployeeID FROM {SevitaConstants.EmployeeFeedTable}";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand(sql, connection)
        {
            CommandType = System.Data.CommandType.Text
        };

        await using var reader = await command.ExecuteReaderAsync(ct);

        // First result set: VendorIds from Sevita_Supplier_SiteInformation_Feed
        while (await reader.ReadAsync(ct))
        {
            var supplier = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(supplier))
                validIds.VendorIds.Add(supplier);
        }

        // Second result set: EmployeeIds from Sevita_Employee_Feed
        if (await reader.NextResultAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var employeeId = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(employeeId))
                    validIds.EmployeeIds.Add(employeeId);
            }
        }

        return validIds;
    }

    /// <summary>
    /// Loads <see cref="FailedPostConfiguration"/> and <see cref="EmailConfiguration"/>
    /// from the <c>get_sevita_configurations</c> stored procedure result.
    /// Returns defaults when the SP returns no rows.
    /// Virtual to allow test subclasses to override without a real SQL connection.
    /// </summary>
    protected virtual async Task<(FailedPostConfiguration, EmailConfiguration)> LoadEmailConfigAsync(
        string connectionString,
        CancellationToken ct)
    {
        var failedPostConfig = new FailedPostConfiguration();
        var emailConfig = new EmailConfiguration();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = new SqlCommand(SevitaConstants.SpGetConfiguration, connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };

        await using var reader = await command.ExecuteReaderAsync(ct);

        if (await reader.ReadAsync(ct))
        {
            // Email configuration columns
            emailConfig.SmtpServer     = GetString(reader, "SMTPServer");
            emailConfig.SmtpServerPort = GetInt(reader, "SMTPServerPort");
            emailConfig.Username       = GetString(reader, "Username");
            emailConfig.Password       = GetString(reader, "Password");
            emailConfig.EmailFrom      = GetString(reader, "EmailFrom");
            emailConfig.EmailFromUser  = GetString(reader, "EmailFromUser");
            emailConfig.SmtpUseSsl     = GetBool(reader, "SMTPUseSSL");

            // Failed post notification configuration columns
            failedPostConfig.EmailTo       = GetString(reader, "EmailTo");
            failedPostConfig.EmailTemplate = GetString(reader, "EmailTemplate");
            failedPostConfig.EmailSubject  = GetString(reader, "EmailSubject");
        }

        return (failedPostConfig, emailConfig);
    }

    private static string GetString(SqlDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }
        catch (IndexOutOfRangeException)
        {
            return string.Empty;
        }
    }

    private static int GetInt(SqlDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
        }
        catch (IndexOutOfRangeException)
        {
            return 0;
        }
    }

    private static bool GetBool(SqlDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            return !reader.IsDBNull(ordinal) && reader.GetBoolean(ordinal);
        }
        catch (IndexOutOfRangeException)
        {
            return false;
        }
    }
}
