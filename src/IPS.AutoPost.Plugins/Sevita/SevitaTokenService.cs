using IPS.AutoPost.Plugins.Sevita.Constants;
using IPS.AutoPost.Plugins.Sevita.Models;
using Microsoft.Extensions.Logging;
using RestSharp;

namespace IPS.AutoPost.Plugins.Sevita;

/// <summary>
/// Obtains and caches an OAuth2 Bearer token for the Sevita API using the
/// <c>client_credentials</c> grant type.
/// </summary>
/// <remarks>
/// <para>
/// The token is cached in memory until <see cref="SevitaConfig.TokenExpirationMin"/>
/// minutes have elapsed since the last successful token fetch. On the next call after
/// expiry, a new token request is made automatically.
/// </para>
/// <para>
/// Token request details:
/// <list type="bullet">
///   <item>Method: POST</item>
///   <item>URL: <c>SevitaConfig.ApiAccessTokenUrl</c></item>
///   <item>Content-Type: <c>application/x-www-form-urlencoded</c></item>
///   <item>Body parameters: <c>grant_type=client_credentials</c>, <c>client_id</c>, <c>client_secret</c></item>
/// </list>
/// </para>
/// </remarks>
public class SevitaTokenService
{
    private readonly ILogger<SevitaTokenService> _logger;

    // Cached token state — protected by a SemaphoreSlim to prevent concurrent token fetches
    private string? _cachedToken;
    private DateTime _tokenFetchedAt = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SevitaTokenService(ILogger<SevitaTokenService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns a valid Bearer token for the Sevita API.
    /// Returns the cached token if it has not yet expired; otherwise fetches a new one.
    /// </summary>
    /// <param name="config">Sevita-specific configuration containing OAuth2 credentials and token URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Bearer token string (without the "Bearer " prefix).</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the token endpoint returns a non-success response or an empty token.
    /// </exception>
    public virtual async Task<string> GetAuthTokenAsync(SevitaConfig config, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (IsTokenValid(config))
            {
                _logger.LogDebug("SevitaTokenService: Returning cached token (expires in {RemainingMin:F1} min)",
                    GetRemainingMinutes(config));
                return _cachedToken!;
            }

            _logger.LogInformation("SevitaTokenService: Fetching new OAuth2 token from {Url}",
                config.ApiAccessTokenUrl);

            var token = await FetchTokenAsync(config, ct);

            _cachedToken = token;
            _tokenFetchedAt = DateTime.UtcNow;

            _logger.LogInformation("SevitaTokenService: Token fetched successfully, valid for {ExpirationMin} min",
                config.TokenExpirationMin);

            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    // -----------------------------------------------------------------------
    // Protected helpers — virtual to allow override in tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns <c>true</c> when a cached token exists and has not yet expired.
    /// Protected to allow test subclasses to inspect cache state.
    /// </summary>
    protected internal bool IsTokenValid(SevitaConfig config)
    {
        if (string.IsNullOrEmpty(_cachedToken))
            return false;

        var elapsed = DateTime.UtcNow - _tokenFetchedAt;
        return elapsed.TotalMinutes < config.TokenExpirationMin;
    }

    /// <summary>
    /// Returns the number of minutes remaining before the cached token expires.
    /// </summary>
    protected internal double GetRemainingMinutes(SevitaConfig config)
    {
        var elapsed = DateTime.UtcNow - _tokenFetchedAt;
        return config.TokenExpirationMin - elapsed.TotalMinutes;
    }

    /// <summary>
    /// POSTs to the token endpoint with <c>grant_type=client_credentials</c> form body
    /// and returns the raw access token string.
    /// </summary>
    private async Task<string> FetchTokenAsync(SevitaConfig config, CancellationToken ct)
    {
        var clientOptions = new RestClientOptions(config.ApiAccessTokenUrl)
        {
            MaxTimeout = SevitaConstants.ApiTimeoutMs
        };
        using var client = new RestClient(clientOptions);

        var request = new RestRequest();
        request.AddHeader("Content-Type", SevitaConstants.ContentTypeFormUrlEncoded);
        request.AddParameter("grant_type", SevitaConstants.OAuth2GrantType);
        request.AddParameter("client_id", config.ClientId);
        request.AddParameter("client_secret", config.ClientSecret);

        var response = await client.ExecutePostAsync(request, ct);

        _logger.LogInformation("SevitaTokenService: Token endpoint responded with HTTP {StatusCode}",
            (int)response.StatusCode);

        if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
        {
            throw new InvalidOperationException(
                $"SevitaTokenService: Token request failed. HTTP {(int)response.StatusCode}: {response.Content}");
        }

        // Parse the access_token from the JSON response
        // Expected shape: { "access_token": "...", "token_type": "Bearer", "expires_in": 3600 }
        var json = Newtonsoft.Json.Linq.JObject.Parse(response.Content);
        var accessToken = json["access_token"]?.ToString();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                $"SevitaTokenService: Token response did not contain 'access_token'. Response: {response.Content}");
        }

        return accessToken;
    }

    /// <summary>
    /// Resets the cached token state. Used in tests to simulate token expiry.
    /// </summary>
    protected internal void ResetCache()
    {
        _cachedToken = null;
        _tokenFetchedAt = DateTime.MinValue;
    }

    /// <summary>
    /// Forces the cached token fetch time to a specific value. Used in tests to simulate expiry.
    /// </summary>
    protected internal void SetTokenFetchedAt(DateTime fetchedAt)
    {
        _tokenFetchedAt = fetchedAt;
    }
}
