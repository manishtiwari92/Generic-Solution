using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace IPS.AutoPost.Core.Infrastructure;

/// <summary>
/// Scans well-known configuration sections for values that start with a forward slash ("/"),
/// treats each such value as an AWS Secrets Manager secret path, fetches the actual secret
/// in parallel, and injects the resolved values back into <see cref="IConfiguration"/> via
/// <see cref="MemoryConfigurationBuilderExtensions.AddInMemoryCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// Sections scanned:
/// <list type="bullet">
///   <item><c>ConnectionStrings:*</c> — all named connection strings</item>
///   <item><c>Email:SmtpPassword</c></item>
///   <item><c>ApiKey:Value</c></item>
/// </list>
/// </para>
/// <para>
/// JSON secret handling: if the returned <c>SecretString</c> is a JSON object that contains
/// an <c>AppConnectionString</c> property, that property's value is used as the resolved
/// secret. Otherwise the raw <c>SecretString</c> is used.
/// </para>
/// <para>
/// Region resolution order:
/// <list type="number">
///   <item><c>AWS_DEFAULT_REGION</c> environment variable</item>
///   <item><c>AWS_REGION</c> environment variable</item>
///   <item>Fallback: <c>"us-east-1"</c></item>
/// </list>
/// </para>
/// <para>
/// Usage in <c>Program.cs</c>:
/// <code>
/// await builder.Configuration.AddSecretsManagerAsync();
/// </code>
/// </para>
/// </remarks>
public static class SecretsManagerConfigurationProvider
{
    // -----------------------------------------------------------------------
    // Config key prefixes / exact keys to scan for "/" prefixed secret paths
    // -----------------------------------------------------------------------
    private const string ConnectionStringsSectionKey = "ConnectionStrings";
    private static readonly string[] ExactKeysToScan =
    [
        "Email:SmtpPassword",
        "ApiKey:Value"
    ];

    // -----------------------------------------------------------------------
    // JSON property name used to extract the connection string from a JSON secret
    // -----------------------------------------------------------------------
    private const string AppConnectionStringProperty = "AppConnectionString";

    // -----------------------------------------------------------------------
    // Timeout for the entire parallel fetch operation
    // -----------------------------------------------------------------------
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Scans the current configuration for "/" prefixed values in the well-known sections,
    /// fetches the corresponding AWS Secrets Manager secrets in parallel (30-second timeout),
    /// and injects the resolved values back into the configuration builder via
    /// <see cref="MemoryConfigurationBuilderExtensions.AddInMemoryCollection"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IConfigurationBuilder"/> to augment.</param>
    /// <param name="secretsManager">
    /// Optional <see cref="IAmazonSecretsManager"/> client. When <c>null</c> a default
    /// <see cref="AmazonSecretsManagerClient"/> is created using the ambient AWS credentials
    /// (IAM role, environment variables, or <c>~/.aws/credentials</c>) and the region
    /// resolved from <c>AWS_DEFAULT_REGION</c> → <c>AWS_REGION</c> → <c>"us-east-1"</c>.
    /// </param>
    /// <param name="timeout">
    /// Optional timeout for the entire parallel fetch. Defaults to 30 seconds.
    /// </param>
    /// <returns>A <see cref="Task"/> that completes when all secrets have been resolved.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the 30-second timeout is exceeded or when a secret path is not found
    /// in Secrets Manager.
    /// </exception>
    public static async Task AddSecretsManagerAsync(
        this IConfigurationBuilder builder,
        IAmazonSecretsManager? secretsManager = null,
        TimeSpan? timeout = null)
    {
        // Build a temporary IConfiguration snapshot so we can read current values
        var config = builder.Build();

        // Collect all config key → secret-path pairs that need resolution
        var secretPaths = CollectSecretPaths(config);

        if (secretPaths.Count == 0)
            return;

        // Create a default client if none was injected (uses ambient AWS credentials)
        var ownsClient = secretsManager is null;
        if (ownsClient)
        {
            var region = ResolveRegion();
            secretsManager = new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(region));
        }

        var effectiveTimeout = timeout ?? DefaultTimeout;

        try
        {
            using var cts = new CancellationTokenSource(effectiveTimeout);

            // Fetch all secrets in parallel with a shared timeout CancellationToken
            var fetchTasks = secretPaths.Select(kvp =>
                FetchSecretAsync(secretsManager!, kvp.Key, kvp.Value, cts.Token));

            KeyValuePair<string, string?>[] resolvedPairs;
            try
            {
                resolvedPairs = await Task.WhenAll(fetchTasks);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"Secrets Manager request timed out after {(int)effectiveTimeout.TotalSeconds}s");
            }

            // Filter out any entries that failed to resolve (null value)
            var resolvedValues = resolvedPairs
                .Where(pair => pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            if (resolvedValues.Count > 0)
                builder.AddInMemoryCollection(resolvedValues!);
        }
        finally
        {
            // Dispose the client only if we created it
            if (ownsClient)
                secretsManager!.Dispose();
        }
    }

    // -----------------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolves the AWS region from environment variables.
    /// Priority: <c>AWS_DEFAULT_REGION</c> → <c>AWS_REGION</c> → <c>"us-east-1"</c>.
    /// </summary>
    public static string ResolveRegion()
    {
        var region = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");
        if (!string.IsNullOrWhiteSpace(region))
            return region;

        region = Environment.GetEnvironmentVariable("AWS_REGION");
        if (!string.IsNullOrWhiteSpace(region))
            return region;

        return "us-east-1";
    }

    /// <summary>
    /// Walks the current configuration snapshot and returns a dictionary of
    /// <c>configKey → secretPath</c> for every value that starts with "/".
    /// </summary>
    public static Dictionary<string, string> CollectSecretPaths(IConfiguration config)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Scan all ConnectionStrings:* entries
        var connectionStringsSection = config.GetSection(ConnectionStringsSectionKey);
        foreach (var child in connectionStringsSection.GetChildren())
        {
            var value = child.Value;
            if (IsSecretPath(value))
                result[child.Path] = value!;
        }

        // Scan exact keys
        foreach (var key in ExactKeysToScan)
        {
            var value = config[key];
            if (IsSecretPath(value))
                result[key] = value!;
        }

        return result;
    }

    /// <summary>Returns <c>true</c> when <paramref name="value"/> is a non-empty string starting with "/".</summary>
    private static bool IsSecretPath(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.StartsWith('/');

    /// <summary>
    /// Fetches a single secret from AWS Secrets Manager and returns a
    /// <c>(configKey, resolvedValue)</c> pair.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the secret is not found in Secrets Manager.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Propagated when the shared <paramref name="ct"/> is cancelled (timeout).
    /// </exception>
    private static async Task<KeyValuePair<string, string?>> FetchSecretAsync(
        IAmazonSecretsManager secretsManager,
        string configKey,
        string secretPath,
        CancellationToken ct)
    {
        try
        {
            var request = new GetSecretValueRequest { SecretId = secretPath };
            var response = await secretsManager.GetSecretValueAsync(request, ct);

            var resolvedValue = ExtractSecretValue(response.SecretString);
            return new KeyValuePair<string, string?>(configKey, resolvedValue);
        }
        catch (ResourceNotFoundException)
        {
            throw new InvalidOperationException(
                $"Secret '{secretPath}' not found in Secrets Manager");
        }
        catch (OperationCanceledException)
        {
            // Re-throw so the caller's cts.IsCancellationRequested check fires
            throw;
        }
        catch (Exception ex)
        {
            // Re-throw with context so the caller can decide whether to fail fast or skip
            throw new InvalidOperationException(
                $"Failed to fetch secret '{secretPath}' for config key '{configKey}': {ex.Message}", ex);
        }
    }

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
    public static string? ExtractSecretValue(string? secretString)
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
