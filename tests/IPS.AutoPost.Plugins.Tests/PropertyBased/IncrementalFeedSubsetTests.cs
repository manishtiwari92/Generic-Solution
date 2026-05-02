using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Plugins.InvitedClub;
using IPS.AutoPost.Plugins.InvitedClub.Constants;
using IPS.AutoPost.Plugins.InvitedClub.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace IPS.AutoPost.Plugins.Tests.PropertyBased;

/// <summary>
/// PBT Property 27.6 — Incremental Feed Subset Property
///
/// PROPERTY 1: The set of supplier IDs fetched in an incremental run is always a
///             subset of the supplier IDs fetched in a full run.
///
/// PROPERTY 2: count(incremental supplier IDs) &lt;= count(full supplier IDs).
///
/// The incremental feed filters suppliers by LastUpdateDate >= (LastSupplierDownloadTime - 2 days).
/// This means:
///   - Suppliers updated recently are included in both full and incremental runs.
///   - Suppliers not updated recently are included only in the full run.
///   - The incremental set is always a subset of the full set.
///
/// Tested via FsCheck generators that produce arbitrary:
///   - Total supplier counts (1–30)
///   - Fractions of recently-updated suppliers (0–100%)
///   - LastSupplierDownloadTime offsets (1–30 days ago)
/// </summary>
public class IncrementalFeedSubsetTests : IDisposable
{
    private readonly WireMockServer _server;

    public IncrementalFeedSubsetTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose()
    {
        _server.Stop();
        _server.Dispose();
    }

    // -----------------------------------------------------------------------
    // FsCheck generators
    // -----------------------------------------------------------------------

    private static Gen<int> SupplierCountGen =>
        Gen.Choose(1, 30);

    /// <summary>Generates a fraction of suppliers that are "recently updated" (0.0 to 1.0).</summary>
    private static Gen<double> RecentFractionGen =>
        Gen.Choose(0, 100).Select(i => i / 100.0);

    /// <summary>Generates a LastSupplierDownloadTime offset in days (1–30 days ago).</summary>
    private static Gen<int> DaysAgoGen =>
        Gen.Choose(1, 30);

    // -----------------------------------------------------------------------
    // 27.6a — FsCheck property: incremental IDs ⊆ full IDs
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementalSupplierIds_AreSubset_OfFullSupplierIds()
    {
        var property = Prop.ForAll(
            SupplierCountGen.ToArbitrary(),
            RecentFractionGen.ToArbitrary(),
            DaysAgoGen.ToArbitrary(),
            (supplierCount, recentFraction, daysAgo) =>
            {
                var (fullIds, incrementalIds) =
                    RunFullAndIncrementalFetch(supplierCount, recentFraction, daysAgo)
                        .GetAwaiter().GetResult();

                // Incremental IDs must be a subset of full IDs
                var isSubset = incrementalIds.All(id => fullIds.Contains(id));

                // Count constraint: incremental <= full
                var countValid = incrementalIds.Count <= fullIds.Count;

                return isSubset && countValid;
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.6b — FsCheck property: count(incremental) <= count(full)
    // -----------------------------------------------------------------------

    [Fact]
    public void IncrementalCount_IsLessThanOrEqualTo_FullCount()
    {
        var property = Prop.ForAll(
            SupplierCountGen.ToArbitrary(),
            RecentFractionGen.ToArbitrary(),
            DaysAgoGen.ToArbitrary(),
            (supplierCount, recentFraction, daysAgo) =>
            {
                var (fullIds, incrementalIds) =
                    RunFullAndIncrementalFetch(supplierCount, recentFraction, daysAgo)
                        .GetAwaiter().GetResult();

                return incrementalIds.Count <= fullIds.Count;
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.6c — Explicit parametric tests
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(10, 0.5, 5)]   // 10 suppliers, 50% recent, 5 days ago
    [InlineData(10, 1.0, 5)]   // 10 suppliers, 100% recent → incremental == full
    [InlineData(10, 0.0, 5)]   // 10 suppliers, 0% recent → incremental is empty
    [InlineData(20, 0.3, 10)]  // 20 suppliers, 30% recent, 10 days ago
    public async Task IncrementalIds_AreSubset_OfFullIds(
        int supplierCount,
        double recentFraction,
        int daysAgo)
    {
        var (fullIds, incrementalIds) =
            await RunFullAndIncrementalFetch(supplierCount, recentFraction, daysAgo);

        incrementalIds.Should().BeSubsetOf(fullIds,
            "incremental supplier IDs must always be a subset of full supplier IDs");

        incrementalIds.Count.Should().BeLessThanOrEqualTo(fullIds.Count,
            "incremental count must be <= full count");
    }

    [Fact]
    public async Task WhenAllSuppliersAreRecent_IncrementalEqualsFullSet()
    {
        // All suppliers updated recently → incremental should fetch all of them
        var (fullIds, incrementalIds) =
            await RunFullAndIncrementalFetch(supplierCount: 10, recentFraction: 1.0, daysAgo: 5);

        incrementalIds.Should().BeEquivalentTo(fullIds,
            "when all suppliers are recently updated, incremental set should equal full set");
    }

    [Fact]
    public async Task WhenNoSuppliersAreRecent_IncrementalIsEmpty()
    {
        // No suppliers updated recently → incremental should fetch none
        var (fullIds, incrementalIds) =
            await RunFullAndIncrementalFetch(supplierCount: 10, recentFraction: 0.0, daysAgo: 5);

        incrementalIds.Should().BeEmpty(
            "when no suppliers are recently updated, incremental set should be empty");
        fullIds.Should().HaveCount(10,
            "full set should still contain all suppliers");
    }

    // -----------------------------------------------------------------------
    // Core test runner
    // -----------------------------------------------------------------------

    private async Task<(HashSet<string> FullIds, List<string> IncrementalIds)>
        RunFullAndIncrementalFetch(
            int supplierCount,
            double recentFraction,
            int daysAgo)
    {
        var lastDownloadTime = DateTime.UtcNow.AddDays(-daysAgo);
        var cutoff = lastDownloadTime.AddDays(-2);

        // Build supplier list: some recently updated, some not
        var recentCount = (int)Math.Round(supplierCount * recentFraction);
        var suppliers = new List<SupplierResponse>();

        for (var i = 0; i < supplierCount; i++)
        {
            var isRecent = i < recentCount;
            suppliers.Add(new SupplierResponse
            {
                SupplierId = $"S{i:D4}",
                // Recent suppliers: updated after cutoff; old suppliers: updated before cutoff
                LastUpdateDate = isRecent
                    ? cutoff.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ss")
                    : cutoff.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss")
            });
        }

        var fullIds = suppliers.Select(s => s.SupplierId).ToHashSet();

        // Build DB mock
        var dbMock = new Mock<IInvitedClubFeedDataAccess>(MockBehavior.Loose);

        // For incremental: table is NOT empty
        dbMock
            .Setup(db => db.GetTableCountAsync(InvitedClubConstants.SupplierAddressTableName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        var strategy = BuildStrategy(dbMock.Object);

        var config = BuildConfig();
        var clientConfig = new InvitedClubConfig
        {
            LastSupplierDownloadTime = lastDownloadTime
        };

        // Capture which supplier IDs are fetched in the incremental run
        // by setting up WireMock to record which address endpoints are called
        var fetchedSupplierIds = new List<string>();

        foreach (var supplier in suppliers)
        {
            var supplierId = supplier.SupplierId;
            _server
                .Given(Request.Create()
                    .WithPath($"/suppliers/{supplierId}/child/addresses")
                    .UsingGet())
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        items = new[] { new { SupplierAddressId = $"ADDR-{supplierId}", AddressName = "Main" } },
                        count = 1,
                        hasMore = false,
                        limit = 500,
                        offset = 0
                    })));
        }

        // Run incremental fetch
        var addresses = await strategy.LoadSupplierAddressAsync(
            config, clientConfig, suppliers, CancellationToken.None);

        // The incremental IDs are the supplier IDs that actually had addresses fetched
        var incrementalIds = addresses.Select(a => a.SupplierId).Distinct().ToList();

        _server.Reset();

        return (fullIds, incrementalIds);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private InvitedClubFeedStrategy BuildStrategy(IInvitedClubFeedDataAccess db)
    {
        return new InvitedClubFeedStrategy(
            db,
            new Mock<IEmailService>().Object,
            NullLogger<InvitedClubFeedStrategy>.Instance);
    }

    private GenericJobConfig BuildConfig() => new()
    {
        Id = 1,
        JobId = 42,
        AuthUsername = "user",
        AuthPassword = "pass",
        PostServiceUrl = _server.Urls[0] + "/"
    };
}
