// IPS.AutoPost.Api/Middleware/ApiKeyMiddleware.cs
// Task 20.4: API key authentication middleware.
// Validates the x-api-key header against the value stored in IConfiguration
// (resolved from AWS Secrets Manager at startup via SecretsManagerConfigurationProvider).

namespace IPS.AutoPost.Api.Middleware;

/// <summary>
/// Middleware that enforces API key authentication on every request.
/// <para>
/// The expected API key is read from <c>IConfiguration["ApiKey:Value"]</c>,
/// which is resolved from AWS Secrets Manager at startup when the config value
/// starts with "/" (e.g. <c>/IPS/Common/prod/ApiKey</c>).
/// </para>
/// <para>
/// Requests must include the header <c>x-api-key: {key}</c>.
/// Missing or invalid keys receive HTTP 401 Unauthorized.
/// </para>
/// <para>
/// Health-check paths (<c>/health</c>, <c>/healthz</c>) are exempt from
/// authentication to allow load balancer probes to pass without credentials.
/// </para>
/// </summary>
public class ApiKeyMiddleware
{
    private const string ApiKeyHeaderName = "x-api-key";
    private const string ApiKeyConfigPath = "ApiKey:Value";

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Exempt health-check endpoints from API key validation
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/healthz", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Read the expected API key from configuration
        var expectedApiKey = _configuration[ApiKeyConfigPath];

        if (string.IsNullOrWhiteSpace(expectedApiKey))
        {
            // Misconfiguration — fail closed rather than allowing unauthenticated access
            _logger.LogError(
                "API key is not configured. Set '{ConfigPath}' in appsettings.json " +
                "or as a Secrets Manager path.", ApiKeyConfigPath);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "API authentication is not configured on this server."
            });
            return;
        }

        // Validate the incoming header
        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedKey) ||
            !string.Equals(providedKey, expectedApiKey, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Unauthorized request to {Path} — missing or invalid {Header} header.",
                path, ApiKeyHeaderName);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Unauthorized. Provide a valid API key in the x-api-key header."
            });
            return;
        }

        await _next(context);
    }
}
