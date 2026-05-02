using System.Data;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using IPS.AutoPost.Core.Interfaces;
using IPS.AutoPost.Core.Models;
using IPS.AutoPost.Core.Services;
using IPS.AutoPost.Plugins.InvitedClub;
using IPS.AutoPost.Plugins.InvitedClub.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IPS.AutoPost.Plugins.Tests.PropertyBased;

/// <summary>
/// PBT Property 27.4 — UseTax Round-Trip Property
///
/// PROPERTY 1: UseTax=NO → ShipToLocation is completely absent from ALL invoice lines
///             (not null, not empty — the JSON property must not exist at all).
///
/// PROPERTY 2: UseTax=YES → ShipToLocation is present and non-empty in ALL invoice lines.
///
/// This is a round-trip property: the UseTax flag on the header row must be faithfully
/// reflected in the serialized JSON payload sent to Oracle Fusion. Any deviation would
/// cause Oracle Fusion to reject the invoice or apply incorrect tax treatment.
///
/// Tested via FsCheck generators that produce arbitrary:
///   - Line counts (1 to 20 lines)
///   - ShipToLocation values (non-empty strings)
///   - UseTax values (YES / NO)
/// </summary>
public class UseTaxRoundTripTests
{
    // -----------------------------------------------------------------------
    // FsCheck generators
    // -----------------------------------------------------------------------

    /// <summary>Generates a non-empty, non-whitespace location string.</summary>
    private static Gen<string> LocationGen =>
        Gen.Elements("LOC-A", "LOC-B", "LOC-C", "WAREHOUSE-1", "SITE-EAST", "SITE-WEST");

    /// <summary>Generates a list of 1–20 location strings (one per invoice line).</summary>
    private static Gen<List<string>> LocationListGen =>
        Gen.Choose(1, 20)
           .SelectMany(count =>
           {
               // Build a list of N generators and combine them
               var gens = Enumerable.Range(0, count).Select(_ => LocationGen).ToList();
               return gens.Aggregate(
                   Gen.Constant(new List<string>()),
                   (acc, gen) => acc.SelectMany(list =>
                       gen.Select(item => { var newList = new List<string>(list) { item }; return newList; })));
           });

    private static Gen<string> UseTaxGen =>
        Gen.Elements("YES", "NO");

    // -----------------------------------------------------------------------
    // 27.4a — FsCheck property: UseTax=NO removes ShipToLocation from all lines
    // -----------------------------------------------------------------------

    [Fact]
    public void UseTaxNo_RemovesShipToLocation_FromAllLines_ForAnyNumberOfLines()
    {
        var property = Prop.ForAll(
            LocationListGen.ToArbitrary(),
            locations =>
            {
                var strategy = BuildStrategy();
                var ds = BuildDataSet(locations, useTax: "NO");
                var json = strategy.BuildInvoiceRequestJson(ds, "NO");

                var jObj = JObject.Parse(json);
                var lines = (JArray?)jObj["invoiceLines"];

                if (lines is null) return false;
                if (lines.Count != locations.Count) return false;

                // Every line must have ShipToLocation completely absent
                return lines.All(line => ((JObject)line)["ShipToLocation"] is null);
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.4b — FsCheck property: UseTax=YES preserves ShipToLocation on all lines
    // -----------------------------------------------------------------------

    [Fact]
    public void UseTaxYes_PreservesShipToLocation_OnAllLines_ForAnyNumberOfLines()
    {
        var property = Prop.ForAll(
            LocationListGen.ToArbitrary(),
            locations =>
            {
                var strategy = BuildStrategy();
                var ds = BuildDataSet(locations, useTax: "YES");
                var json = strategy.BuildInvoiceRequestJson(ds, "YES");

                var jObj = JObject.Parse(json);
                var lines = (JArray?)jObj["invoiceLines"];

                if (lines is null) return false;
                if (lines.Count != locations.Count) return false;

                // Every line must have ShipToLocation present and matching the input
                for (var i = 0; i < locations.Count; i++)
                {
                    var lineLocation = ((JObject)lines[i])["ShipToLocation"]?.ToString();
                    if (lineLocation != locations[i]) return false;
                }

                return true;
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.4c — FsCheck property: UseTax flag is the ONLY difference between YES and NO payloads
    // -----------------------------------------------------------------------

    [Fact]
    public void UseTaxNo_OnlyDifference_IsAbsenceOfShipToLocation_AllOtherFieldsPreserved()
    {
        var property = Prop.ForAll(
            LocationListGen.ToArbitrary(),
            locations =>
            {
                var strategy = BuildStrategy();
                var dsYes = BuildDataSet(locations, useTax: "YES");
                var dsNo = BuildDataSet(locations, useTax: "NO");

                var jsonYes = strategy.BuildInvoiceRequestJson(dsYes, "YES");
                var jsonNo = strategy.BuildInvoiceRequestJson(dsNo, "NO");

                var jYes = JObject.Parse(jsonYes);
                var jNo = JObject.Parse(jsonNo);

                var linesYes = (JArray?)jYes["invoiceLines"];
                var linesNo = (JArray?)jNo["invoiceLines"];

                if (linesYes is null || linesNo is null) return false;
                if (linesYes.Count != linesNo.Count) return false;

                for (var i = 0; i < linesYes.Count; i++)
                {
                    var lineYes = (JObject)linesYes[i];
                    var lineNo = (JObject)linesNo[i];

                    // ShipToLocation must be absent in NO payload
                    if (lineNo["ShipToLocation"] is not null) return false;

                    // All other fields must be identical
                    foreach (var prop in lineYes.Properties())
                    {
                        if (prop.Name == "ShipToLocation") continue;

                        var noValue = lineNo[prop.Name]?.ToString();
                        var yesValue = prop.Value.ToString();
                        if (noValue != yesValue) return false;
                    }
                }

                return true;
            });

        property.QuickCheckThrowOnFailure();
    }

    // -----------------------------------------------------------------------
    // 27.4d — Explicit parametric tests (deterministic, always run in CI)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void UseTaxNo_RemovesShipToLocation_ForNLines(int lineCount)
    {
        var locations = Enumerable.Range(1, lineCount).Select(i => $"LOC-{i}").ToList();
        var strategy = BuildStrategy();
        var ds = BuildDataSet(locations, useTax: "NO");

        var json = strategy.BuildInvoiceRequestJson(ds, "NO");

        var jObj = JObject.Parse(json);
        var lines = (JArray?)jObj["invoiceLines"];

        lines.Should().NotBeNull();
        lines!.Should().HaveCount(lineCount);
        lines.Should().AllSatisfy(line =>
            ((JObject)line)["ShipToLocation"].Should().BeNull(
                $"ShipToLocation must be absent from all {lineCount} lines when UseTax=NO"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void UseTaxYes_KeepsShipToLocation_ForNLines(int lineCount)
    {
        var locations = Enumerable.Range(1, lineCount).Select(i => $"LOC-{i}").ToList();
        var strategy = BuildStrategy();
        var ds = BuildDataSet(locations, useTax: "YES");

        var json = strategy.BuildInvoiceRequestJson(ds, "YES");

        var jObj = JObject.Parse(json);
        var lines = (JArray?)jObj["invoiceLines"];

        lines.Should().NotBeNull();
        lines!.Should().HaveCount(lineCount);

        for (var i = 0; i < lineCount; i++)
        {
            var lineLocation = ((JObject)lines[i])["ShipToLocation"]?.ToString();
            lineLocation.Should().Be(locations[i],
                $"ShipToLocation must be preserved on line {i + 1} when UseTax=YES");
        }
    }

    [Fact]
    public void UseTaxNo_WithSingleLine_RemovesShipToLocation()
    {
        var strategy = BuildStrategy();
        var ds = BuildDataSet(new List<string> { "WAREHOUSE-EAST" }, useTax: "NO");

        var json = strategy.BuildInvoiceRequestJson(ds, "NO");

        var jObj = JObject.Parse(json);
        var line = (JObject?)((JArray?)jObj["invoiceLines"])?[0];

        line.Should().NotBeNull();
        line!["ShipToLocation"].Should().BeNull(
            "ShipToLocation must be completely removed (not null, not empty) when UseTax=NO");
    }

    [Fact]
    public void UseTaxYes_WithSingleLine_KeepsShipToLocation()
    {
        var strategy = BuildStrategy();
        var ds = BuildDataSet(new List<string> { "WAREHOUSE-EAST" }, useTax: "YES");

        var json = strategy.BuildInvoiceRequestJson(ds, "YES");

        var jObj = JObject.Parse(json);
        var line = (JObject?)((JArray?)jObj["invoiceLines"])?[0];

        line.Should().NotBeNull();
        line!["ShipToLocation"]?.ToString().Should().Be("WAREHOUSE-EAST",
            "ShipToLocation must be preserved when UseTax=YES");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static InvitedClubPostStrategy BuildStrategy()
    {
        return new InvitedClubPostStrategy(
            new Mock<IInvitedClubPostDataAccess>().Object,
            new S3ImageService(NullLogger<S3ImageService>.Instance),
            new Mock<IEmailService>().Object,
            NullLogger<InvitedClubPostStrategy>.Instance);
    }

    /// <summary>
    /// Builds a DataSet with one header row and N detail rows, one per location.
    /// </summary>
    private static DataSet BuildDataSet(List<string> locations, string useTax)
    {
        var ds = new DataSet();

        var header = new DataTable("Header");
        header.Columns.Add("UID", typeof(long));
        header.Columns.Add("InvoiceNumber", typeof(string));
        header.Columns.Add("InvoiceCurrency", typeof(string));
        header.Columns.Add("PaymentCurrency", typeof(string));
        header.Columns.Add("InvoiceAmount", typeof(string));
        header.Columns.Add("InvoiceDate", typeof(string));
        header.Columns.Add("BusinessUnit", typeof(string));
        header.Columns.Add("Supplier", typeof(string));
        header.Columns.Add("SupplierSite", typeof(string));
        header.Columns.Add("RequesterId", typeof(string));
        header.Columns.Add("AccountingDate", typeof(string));
        header.Columns.Add("Description", typeof(string));
        header.Columns.Add("InvoiceType", typeof(string));
        header.Columns.Add("LegalEntity", typeof(string));
        header.Columns.Add("LegalEntityIdentifier", typeof(string));
        header.Columns.Add("LiabilityDistribution", typeof(string));
        header.Columns.Add("RoutingAttribute2", typeof(string));
        header.Columns.Add("InvoiceSource", typeof(string));
        header.Columns.Add("Payor", typeof(string));
        header.Columns.Add("ImagePath", typeof(string));
        header.Columns.Add("UseTax", typeof(string));
        header.Rows.Add(
            1L, "INV-001", "USD", "USD", "500.00",
            "2026-01-01", "BU1", "TEST-SUPPLIER", "SITE1",
            "REQ-123", "2026-01-01", "Test Invoice", "Standard",
            "LE1", "LE-ID-1", "LIAB-DIST", "ATTR2", "SOURCE",
            "PAYOR1", "invoice.pdf", useTax);

        var detail = new DataTable("Detail");
        detail.Columns.Add("LineNumber", typeof(string));
        detail.Columns.Add("LineAmount", typeof(string));
        detail.Columns.Add("ShipToLocation", typeof(string));
        detail.Columns.Add("DistributionCombination", typeof(string));
        detail.Columns.Add("DistributionLineNumber", typeof(string));
        detail.Columns.Add("DistributionLineType", typeof(string));
        detail.Columns.Add("DistributionAmount", typeof(string));

        for (var i = 0; i < locations.Count; i++)
        {
            detail.Rows.Add(
                (i + 1).ToString(),
                "100.00",
                locations[i],
                "DIST-COMBO",
                (i + 1).ToString(),
                "Item",
                "100.00");
        }

        ds.Tables.Add(header);
        ds.Tables.Add(detail);
        return ds;
    }
}
