using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace IPS.AutoPost.Core.Services;

/// <summary>
/// Provides secret resolution with an in-memory cache backed by AWS Secrets Manager,
/// with a fallback to <see cref="IConfiguration"/> for local development environments
/// where AWS credentials are unavailable.
/// </summary>
/// <remarks>
/// <para>
/// Resolution order for a given <paramref name="secretPath"/>:
/// <list type="number">
///   <item>In-memory cache (<see cref="ConcurrentDictionary{TKey,TValue}"/>)</item>
///   <item>AWS Secrets Manager <c>GetSecretValueAsync</c></item>
///   <item><see cref="IConfiguration"/>[<paramref name="secretPath"/>] (fallback)</item>
/// </list>
/// </para>
/// <para>
/// JSON secret handling: if the returned <c>SecretString</c> is a JSON object that contains
/// an <c>AppConnectionString</c> property, that property's value is used as the resolved
/// secret. Otherwise the raw <c>SecretString</c> is used.
/// </para>
/// <para>
/// Thread safety: a <see cref="SemaphoreSlim"/> per secret path prevents duplicate
/// Secrets Manager calls when multiple threads request the same uncached secret
/// concurrently.
/// </para>
/// </remarks>
public sealed class ConfigurationService
{
    // -----------------------------------------------------------------------
    // JSON property name used to extract the connection string from a JSON secret
    // -----------------------------------------------------------------------
    private const string AppConnectionStringProperty = "AppConnectionString";

    // -----------------------------------------------------------------------
    // Dependencies
    // -----------------------------------------------------------------------
    private readonly IAmazonSecretsManager _secretsManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationService> _logger;

    // -----------------------------------------------------------------------
    // Cache: secretPath → resolved value
    // -----------------------------------------------------------------------
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    // -----------------------------------------------------------------------
    // Per-key semaphores to prevent thundering-herd on cache misses
    // -----------------------------------------------------------------------
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initialises a new <see cref="ConfigurationService"/>.
    /// </summary>
    /// <param name="secretsManager">AWS Secrets Manager client.</param>
    /// <param name="configuration">Application configuration (used as fallback).</param>
    /// <param name="logger">Logger for warning messages.</param>
    public ConfigurationService(
        IAmazonSecretsManager secretsManager,
        IConfiguration configuration,
        ILogger<ConfigurationService> logger)
    {
        _secretsManager = secretsManager;
        _configuration  = configuration;
        _logger         = logger;
    }

    /// <summary>
    /// Returns the resolved value for <paramref name="secretPath"/>.
    /// </summary>
    /// <param name="secretPath">
    /// The AWS Secrets Manager secret ID (e.g. <c>/ips/autopost/prod/db-password</c>)
    /// or a plain <see cref="IConfiguration"/> key used as a fallback.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved secret value, or <c>null</c> when not found anywhere.</returns>
    public async Task<string?> GetSecretAsync(string secretPath, CancellationToken ct = default)
    {
        // 1. Fast path — already cached
        if (_cache.TryGetValue(secretPath, out var cached))
            return cached;

        // 2. Acquire a per-key semaphore to serialise concurrent cache misses for the
        //    same secret path, avoiding duplicate Secrets Manager calls.
        var semaphore = _locks.GetOrAdd(secretPath, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-checked locking: another thread may have populated the cache
            // while we were waiting for the semaphore.
            if (_cache.TryGetValue(secretPath, out cached))
                return cached;

            // 3. Fetch from AWS Secrets Manager
            string? resolved = null;
            try
            {
                var request  = new GetSecretValueRequest { SecretId = secretPath };
                var response = await _secretsManager.GetSecretValueAsync(request, ct).ConfigureAwait(false);
                resolved = ExtractSecretValue(response.SecretString);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 4. Fallback to IConfiguration (e.g. local dev without AWS credentials)
                _logger.LogWarning(
                    ex,
                    "Failed to retrieve secret '{SecretPath}' from AWS Secrets Manager. " +
                    "Falling back to IConfiguration. Error: {ErrorMessage}",
                    secretPath,
                    ex.Message);

                resolved = _configuration[secretPath];
            }

            // 5. Cache and return (even if null — avoids repeated failed lookups)
            if (resolved is not null)
                _cache[secretPath] = resolved;

            return resolved;
        }
        finally
        {
            semaphore.Release();
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Parses <paramref name="secretString"/> and returns the resolved value.
    /// <list type="bullet">
    ///   <item>
    ///     If <paramref name="secretString"/> is a JSON object containing an
    ///     <c>AppConnectionString</c> property, that property's string value is returned.
    ///   </item>
    ///   <item>Otherwise the raw <paramref name="secretString"/> is returned as-is.</item>
    /// </list>
    /// </summary>
    private static string? ExtractSecretValue(string? secretString)
    {
        if (string.IsNullOrWhiteSpace(secretString))
            return secretString;

        // Attempt to parse as JSON — only if it looks like an object
        var trimmed = secretString.TrimStart();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(secretString);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty(AppConnectionStringProperty, out var prop) &&
                    prop.ValueKind == JsonValueKind.String)
                {
                    return prop.GetString();
                }
            }
            catch (JsonException)
            {
                // Not valid JSON — fall through and return the raw string
            }
        }

        return secretString;
    }
}
