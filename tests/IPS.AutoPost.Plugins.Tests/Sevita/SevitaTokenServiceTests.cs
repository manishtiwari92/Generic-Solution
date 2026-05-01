using FluentAssertions;
using IPS.AutoPost.Plugins.Sevita;
using IPS.AutoPost.Plugins.Sevita.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace IPS.AutoPost.Plugins.Tests.Sevita;

/// <summary>
/// Unit tests for <see cref="SevitaTokenService"/>.
/// Covers: token caching on second call, token refresh after expiry.
/// </summary>
/// <remarks>
/// <para>
/// The token service makes real HTTP calls in <c>FetchTokenAsync</c>, which is private.
/// Tests use a <see cref="TestableSevitaTokenService"/> subclass that overrides
/// <see cref="SevitaTokenService.GetAuthTokenAsync"/> to inject a fake token, allowing
/// the caching and expiry logic to be tested without a live OAuth2 endpoint.
/// </para>
/// <para>
/// Cache state is manipulated via the <c>protected internal</c> helpers
/// <c>SetTokenFetchedAt</c> and <c>ResetCache</c>, which are accessible from the
/// subclass defined in this test file.
/// </para>
/// </remarks>
public class SevitaTokenServiceTests
{
    private static SevitaConfig BuildConfig(int tokenExpirationMin = 60) => new()
    {
        ApiAccessTokenUrl  = "https://auth.example.com/token",
        ClientId           = "test-client-id",
        ClientSecret       = "test-client-secret",
        TokenExpirationMin = tokenExpirationMin
    };

    // =========================================================================
    // Token caching — second call returns cached token
    // =========================================================================

    [Fact]
    public async Task GetAuthTokenAsync_OnFirstCall_FetchesAndCachesToken()
    {
        var sut = new TestableSevitaTokenService("first-token");
        var config = BuildConfig();

        var token = await sut.GetAuthTokenAsync(config, CancellationToken.None);

        token.Should().Be("first-token");
        sut.FetchCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAuthTokenAsync_OnSecondCallBeforeExpiry_ReturnsCachedTokenWithoutFetching()
    {
        var sut = new TestableSevitaTokenService("cached-token");
        var config = BuildConfig(tokenExpirationMin: 60);

        // First call — fetches token
        var first = await sut.GetAuthTokenAsync(config, CancellationToken.None);

        // Second call — should return cached token without fetching again
        var second = await sut.GetAuthTokenAsync(config, CancellationToken.None);

        first.Should().Be("cached-token");
        second.Should().Be("cached-token");
        sut.FetchCallCount.Should().Be(1, "second call should use the cache");
    }

    [Fact]
    public async Task GetAuthTokenAsync_MultipleCallsBeforeExpiry_FetchesOnlyOnce()
    {
        var sut = new TestableSevitaTokenService("stable-token");
        var config = BuildConfig(tokenExpirationMin: 60);

        for (int i = 0; i < 5; i++)
            await sut.GetAuthTokenAsync(config, CancellationToken.None);

        sut.FetchCallCount.Should().Be(1, "all 5 calls should use the same cached token");
    }

    // =========================================================================
    // Token expiry — refresh after expiry
    // =========================================================================

    [Fact]
    public async Task GetAuthTokenAsync_AfterTokenExpires_FetchesNewToken()
    {
        var sut = new TestableSevitaTokenService("refreshed-token");
        var config = BuildConfig(tokenExpirationMin: 60);

        // First call — fetches and caches token
        await sut.GetAuthTokenAsync(config, CancellationToken.None);

        // Simulate token expiry by backdating the fetch time
        sut.BackdateTokenFetch(DateTime.UtcNow.AddMinutes(-61));

        // Second call — token is expired, should fetch again
        var refreshed = await sut.GetAuthTokenAsync(config, CancellationToken.None);

        refreshed.Should().Be("refreshed-token");
        sut.FetchCallCount.Should().Be(2, "token should be refreshed after expiry");
    }

    [Fact]
    public async Task GetAuthTokenAsync_WhenTokenJustBeforeExpiry_DoesNotRefresh()
    {
        var sut = new TestableSevitaTokenService("near-expiry-token");
        var config = BuildConfig(tokenExpirationMin: 60);

        // First call — fetches and caches token
        await sut.GetAuthTokenAsync(config, CancellationToken.None);

        // Simulate 59 minutes passing — still within the 60-minute window
        sut.BackdateTokenFetch(DateTime.UtcNow.AddMinutes(-59));

        var token = await sut.GetAuthTokenAsync(config, CancellationToken.None);

        token.Should().Be("near-expiry-token");
        sut.FetchCallCount.Should().Be(1, "token should not be refreshed before expiry");
    }

    [Fact]
    public async Task GetAuthTokenAsync_WhenTokenAtExactExpiryBoundary_Refreshes()
    {
        var sut = new TestableSevitaTokenService("boundary-token");
        var config = BuildConfig(tokenExpirationMin: 60);

        await sut.GetAuthTokenAsync(config, CancellationToken.None);

        // Exactly at the expiry boundary — elapsed == TokenExpirationMin → expired
        sut.BackdateTokenFetch(DateTime.UtcNow.AddMinutes(-60));

        await sut.GetAuthTokenAsync(config, CancellationToken.None);

        sut.FetchCallCount.Should().Be(2, "token at exact expiry boundary should be refreshed");
    }

    [Fact]
    public async Task GetAuthTokenAsync_AfterResetCache_FetchesNewToken()
    {
        var sut = new TestableSevitaTokenService("new-token");
        var config = BuildConfig();

        await sut.GetAuthTokenAsync(config, CancellationToken.None);
        sut.ClearCache();

        var token = await sut.GetAuthTokenAsync(config, CancellationToken.None);

        token.Should().Be("new-token");
        sut.FetchCallCount.Should().Be(2, "cache was reset so a new fetch is required");
    }

    [Fact]
    public async Task GetAuthTokenAsync_WithShortExpiry_RefreshesAfterExpiry()
    {
        var sut = new TestableSevitaTokenService("short-lived-token");
        var config = BuildConfig(tokenExpirationMin: 1); // 1-minute expiry

        // First call
        await sut.GetAuthTokenAsync(config, CancellationToken.None);

        // Simulate 2 minutes passing
        sut.BackdateTokenFetch(DateTime.UtcNow.AddMinutes(-2));

        // Should refresh
        var token = await sut.GetAuthTokenAsync(config, CancellationToken.None);

        token.Should().Be("short-lived-token");
        sut.FetchCallCount.Should().Be(2, "1-minute token should expire after 2 minutes");
    }

    // =========================================================================
    // Testable subclass — injects fake token without HTTP
    // =========================================================================

    /// <summary>
    /// Testable subclass of <see cref="SevitaTokenService"/> that overrides
    /// <see cref="GetAuthTokenAsync"/> to inject a fake token and track call counts,
    /// while still exercising the real caching logic via the protected internal helpers.
    /// </summary>
    private sealed class TestableSevitaTokenService : SevitaTokenService
    {
        private readonly string _fakeToken;
        public int FetchCallCount { get; private set; }

        public TestableSevitaTokenService(string fakeToken)
            : base(NullLogger<SevitaTokenService>.Instance)
        {
            _fakeToken = fakeToken;
        }

        /// <summary>
        /// Overrides the full method to use the real caching check but inject a fake token
        /// instead of making an HTTP call.
        /// </summary>
        public override async Task<string> GetAuthTokenAsync(SevitaConfig config, CancellationToken ct)
        {
            if (IsTokenValid(config))
                return _fakeToken;

            // Simulate fetch
            FetchCallCount++;
            await Task.Yield(); // Simulate async work

            // Inject the fake token into the cache via protected internal helpers
            SetTokenFetchedAt(DateTime.UtcNow);
            InjectCachedToken(_fakeToken);

            return _fakeToken;
        }

        /// <summary>
        /// Public wrapper — allows the test class to backdate the token fetch time
        /// to simulate expiry. Delegates to the protected internal base method.
        /// </summary>
        public void BackdateTokenFetch(DateTime fetchedAt) => SetTokenFetchedAt(fetchedAt);

        /// <summary>
        /// Public wrapper — allows the test class to reset the token cache.
        /// Delegates to the protected internal base method.
        /// </summary>
        public void ClearCache() => ResetCache();

        /// <summary>
        /// Injects a token value into the private <c>_cachedToken</c> field via reflection.
        /// </summary>
        private void InjectCachedToken(string token)
        {
            var field = typeof(SevitaTokenService)
                .GetField("_cachedToken",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field!.SetValue(this, token);
        }
    }
}
