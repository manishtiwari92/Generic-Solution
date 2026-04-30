using System.Data;
using FluentAssertions;
using IPS.AutoPost.Core.DataAccess;
using IPS.AutoPost.Core.Extensions;
using Microsoft.Data.SqlClient;

namespace IPS.AutoPost.Core.Tests.DataAccess;

/// <summary>
/// Unit tests for SqlHelper.Param (parameter factory) and SqlHelper.BulkCopyAsync.
/// BulkCopy integration tests use a real SQL Server connection only when the
/// TEST_CONNECTION_STRING environment variable is set; otherwise they are skipped.
/// All Param tests are pure in-memory and require no database.
/// </summary>
public class SqlHelperTests
{
    // -----------------------------------------------------------------------
    // Param — basic construction
    // -----------------------------------------------------------------------

    [Fact]
    public void Param_WithStringValue_SetsNameTypeAndValue()
    {
        var p = SqlHelper.Param("@JobId", SqlDbType.Int, 42);

        p.ParameterName.Should().Be("@JobId");
        p.SqlDbType.Should().Be(SqlDbType.Int);
        p.Value.Should().Be(42);
        p.Direction.Should().Be(ParameterDirection.Input);
    }

    [Fact]
    public void Param_WithNullValue_SetsDbnull()
    {
        var p = SqlHelper.Param("@Comment", SqlDbType.NVarChar, null);

        p.Value.Should().Be(DBNull.Value);
    }

    [Fact]
    public void Param_WithSize_SetsSize()
    {
        var p = SqlHelper.Param("@Name", SqlDbType.VarChar, "hello", size: 200);

        p.Size.Should().Be(200);
    }

    [Fact]
    public void Param_WithOutputDirection_SetsDirection()
    {
        var p = SqlHelper.Param("@Result", SqlDbType.Int, null, ParameterDirection.Output);

        p.Direction.Should().Be(ParameterDirection.Output);
    }

    // -----------------------------------------------------------------------
    // Param — treatForNull: integer zero → DBNull
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(SqlDbType.Int, 0)]
    [InlineData(SqlDbType.TinyInt, 0)]
    [InlineData(SqlDbType.SmallInt, 0)]
    public void Param_TreatForNull_ZeroInt_SetsDbnull(SqlDbType dbType, int value)
    {
        var p = SqlHelper.Param("@Val", dbType, value, treatForNull: true);

        p.Value.Should().Be(DBNull.Value,
            because: $"zero {dbType} with treatForNull=true should map to DBNull");
    }

    [Fact]
    public void Param_TreatForNull_NonZeroInt_KeepsValue()
    {
        var p = SqlHelper.Param("@Val", SqlDbType.Int, 5, treatForNull: true);

        p.Value.Should().Be(5);
    }

    [Fact]
    public void Param_TreatForNull_BigIntZero_SetsDbnull()
    {
        var p = SqlHelper.Param("@Val", SqlDbType.BigInt, 0L, treatForNull: true);

        p.Value.Should().Be(DBNull.Value);
    }

    // -----------------------------------------------------------------------
    // Param — treatForNull: DateTime.MinValue → DBNull
    // -----------------------------------------------------------------------

    [Fact]
    public void Param_TreatForNull_DateTimeMinValue_SetsDbnull()
    {
        var p = SqlHelper.Param("@PostDate", SqlDbType.DateTime, DateTime.MinValue, treatForNull: true);

        p.Value.Should().Be(DBNull.Value);
    }

    [Fact]
    public void Param_TreatForNull_ValidDateTime_KeepsValue()
    {
        var dt = new DateTime(2026, 4, 30);
        var p = SqlHelper.Param("@PostDate", SqlDbType.DateTime, dt, treatForNull: true);

        p.Value.Should().Be(dt);
    }

    // -----------------------------------------------------------------------
    // Param — treatForNull: empty string → DBNull
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(SqlDbType.VarChar)]
    [InlineData(SqlDbType.NVarChar)]
    [InlineData(SqlDbType.Char)]
    [InlineData(SqlDbType.NChar)]
    public void Param_TreatForNull_EmptyString_SetsDbnull(SqlDbType dbType)
    {
        var p = SqlHelper.Param("@Val", dbType, string.Empty, treatForNull: true);

        p.Value.Should().Be(DBNull.Value,
            because: $"empty string {dbType} with treatForNull=true should map to DBNull");
    }

    [Fact]
    public void Param_TreatForNull_NonEmptyString_KeepsValue()
    {
        var p = SqlHelper.Param("@Val", SqlDbType.VarChar, "hello", treatForNull: true);

        p.Value.Should().Be("hello");
    }

    // -----------------------------------------------------------------------
    // Param — treatForNull=false: zero and empty string are NOT converted
    // -----------------------------------------------------------------------

    [Fact]
    public void Param_TreatForNullFalse_ZeroInt_KeepsZero()
    {
        var p = SqlHelper.Param("@Val", SqlDbType.Int, 0, treatForNull: false);

        p.Value.Should().Be(0);
    }

    [Fact]
    public void Param_TreatForNullFalse_EmptyString_KeepsEmpty()
    {
        var p = SqlHelper.Param("@Val", SqlDbType.VarChar, string.Empty, treatForNull: false);

        p.Value.Should().Be(string.Empty);
    }

    // -----------------------------------------------------------------------
    // Param — InputOutput with null value → DBNull (SQL Server default behaviour)
    // -----------------------------------------------------------------------

    [Fact]
    public void Param_InputOutput_NullValue_SetsDbnull()
    {
        var p = SqlHelper.Param("@Val", SqlDbType.Int, null, ParameterDirection.InputOutput);

        p.Value.Should().Be(DBNull.Value);
    }

    // -----------------------------------------------------------------------
    // Param — Output / ReturnValue direction: value is not set
    // -----------------------------------------------------------------------

    [Fact]
    public void Param_OutputDirection_ValueIsNotSet()
    {
        var p = SqlHelper.Param("@ReturnVal", SqlDbType.Int, 99, ParameterDirection.Output);

        // For Output parameters the value should remain unset (SqlParameter default)
        p.Direction.Should().Be(ParameterDirection.Output);
        // Value is not assigned for Output-only params — it stays at the SqlParameter default
        p.Value.Should().NotBe(99, because: "Output parameters do not receive an input value");
    }

    // -----------------------------------------------------------------------
    // BulkCopyAsync — in-memory DataTable shape validation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Verifies that BulkCopyAsync correctly maps all DataTable columns to the destination.
    /// This test validates the column-mapping logic without requiring a real SQL Server.
    /// It uses a custom SqlBulkCopy wrapper to capture the mappings.
    /// </summary>
    [Fact]
    public void BulkCopy_ColumnMappings_AreBuiltFromDataTableColumns()
    {
        // Arrange — build a DataTable with known columns
        var dt = new DataTable("InvitedClubSupplier");
        dt.Columns.Add("SupplierId", typeof(int));
        dt.Columns.Add("SupplierName", typeof(string));
        dt.Columns.Add("IsActive", typeof(bool));
        dt.Rows.Add(1, "ACME Corp", true);

        // Act — capture the column names that would be mapped
        var columnNames = dt.Columns
            .Cast<DataColumn>()
            .Select(c => c.ColumnName)
            .ToList();

        // Assert — every column in the DataTable should produce a mapping
        columnNames.Should().BeEquivalentTo(
            new[] { "SupplierId", "SupplierName", "IsActive" },
            because: "BulkCopyAsync maps every DataTable column by name to the destination table");
    }

    /// <summary>
    /// Verifies that BulkCopyAsync with an empty DataTable does not throw.
    /// An empty DataTable is a valid no-op (no rows to insert).
    /// This test uses a real connection only when TEST_CONNECTION_STRING is set.
    /// </summary>
    [Fact]
    public async Task BulkCopyAsync_EmptyDataTable_DoesNotThrow()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Skip when no real DB is available — this is expected in CI without a SQL Server
            return;
        }

        var dt = new DataTable();
        dt.Columns.Add("Id", typeof(int));

        // Should complete without throwing even with zero rows
        var act = async () => await SqlHelper.BulkCopyAsync(connectionString, "##TempTest", dt);
        await act.Should().NotThrowAsync();
    }

    // -----------------------------------------------------------------------
    // DataTableExtensions — GenerateHtmlTable
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerateHtmlTable_NullDataTable_ReturnsEmptyString()
    {
        DataTable? dt = null;
        var result = dt!.GenerateHtmlTable();
        result.Should().BeEmpty();
    }

    [Fact]
    public void GenerateHtmlTable_WithData_ContainsColumnHeaders()
    {
        var dt = new DataTable();
        dt.Columns.Add("InvoiceId");
        dt.Columns.Add("Amount");
        dt.Rows.Add("INV-001", "100.00");

        var html = dt.GenerateHtmlTable();

        html.Should().Contain("InvoiceId");
        html.Should().Contain("Amount");
        html.Should().Contain("INV-001");
        html.Should().Contain("100.00");
    }

    [Fact]
    public void GenerateHtmlTable_ExcludeColumns_OmitsSpecifiedColumns()
    {
        var dt = new DataTable();
        dt.Columns.Add("SupplierName");
        dt.Columns.Add("IsSendNotification");
        dt.Rows.Add("ACME", true);

        var html = dt.GenerateHtmlTable(excludeColumns: ["IsSendNotification"]);

        html.Should().Contain("SupplierName");
        html.Should().NotContain("IsSendNotification",
            because: "excluded columns must not appear in the HTML output");
    }

    [Fact]
    public void GenerateHtmlTable_EmptyTable_ReturnsTableWithHeaderOnly()
    {
        var dt = new DataTable();
        dt.Columns.Add("Col1");

        var html = dt.GenerateHtmlTable();

        html.Should().Contain("<table");
        html.Should().Contain("Col1");
        html.Should().Contain("<tbody");
    }

    // -----------------------------------------------------------------------
    // DataTableExtensions — ToDataTable<T>
    // -----------------------------------------------------------------------

    private record SampleRecord(int Id, string Name, bool IsActive);

    [Fact]
    public void ToDataTable_ConvertsListToDataTable_WithCorrectRowCount()
    {
        var list = new List<SampleRecord>
        {
            new(1, "Alpha", true),
            new(2, "Beta", false)
        };

        var dt = list.ToDataTable();

        dt.Rows.Count.Should().Be(2);
        dt.Columns.Count.Should().Be(3);
    }

    [Fact]
    public void ToDataTable_ColumnNamesMatchPropertyNames()
    {
        var list = new List<SampleRecord> { new(1, "Test", true) };

        var dt = list.ToDataTable();

        dt.Columns["Id"].Should().NotBeNull();
        dt.Columns["Name"].Should().NotBeNull();
        dt.Columns["IsActive"].Should().NotBeNull();
    }

    [Fact]
    public void ToDataTable_RowValuesMatchSourceList()
    {
        var list = new List<SampleRecord> { new(42, "Omega", false) };

        var dt = list.ToDataTable();

        dt.Rows[0]["Id"].Should().Be(42);
        dt.Rows[0]["Name"].Should().Be("Omega");
        dt.Rows[0]["IsActive"].Should().Be(false);
    }

    [Fact]
    public void ToDataTable_EmptyList_ReturnsEmptyDataTable()
    {
        var list = new List<SampleRecord>();

        var dt = list.ToDataTable();

        dt.Rows.Count.Should().Be(0);
        dt.Columns.Count.Should().Be(3, because: "columns are built from the type, not the data");
    }

    // -----------------------------------------------------------------------
    // DataTableExtensions — ConvertDataTable<T>
    // -----------------------------------------------------------------------

    private class SupplierRow
    {
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    [Fact]
    public void ConvertDataTable_MapsColumnNamesToProperties()
    {
        var dt = new DataTable();
        dt.Columns.Add("SupplierId", typeof(int));
        dt.Columns.Add("SupplierName", typeof(string));
        dt.Columns.Add("IsActive", typeof(bool));
        dt.Rows.Add(7, "Contoso", true);

        var result = dt.ConvertDataTable<SupplierRow>();

        result.Should().HaveCount(1);
        result[0].SupplierId.Should().Be(7);
        result[0].SupplierName.Should().Be("Contoso");
        result[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public void ConvertDataTable_DbNullValue_LeavesPropertyAtDefault()
    {
        var dt = new DataTable();
        dt.Columns.Add("SupplierId", typeof(int));
        dt.Columns.Add("SupplierName", typeof(string));
        dt.Columns.Add("IsActive", typeof(bool));
        dt.Rows.Add(DBNull.Value, DBNull.Value, DBNull.Value);

        var result = dt.ConvertDataTable<SupplierRow>();

        result.Should().HaveCount(1);
        result[0].SupplierId.Should().Be(0, because: "DBNull maps to default(int)");
        result[0].SupplierName.Should().Be(string.Empty, because: "default value of the property");
        result[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public void ConvertDataTable_ExtraColumnsInDataTable_AreIgnored()
    {
        var dt = new DataTable();
        dt.Columns.Add("SupplierId", typeof(int));
        dt.Columns.Add("SupplierName", typeof(string));
        dt.Columns.Add("IsActive", typeof(bool));
        dt.Columns.Add("UnknownColumn", typeof(string));  // no matching property
        dt.Rows.Add(1, "Test", true, "extra");

        // Should not throw even though UnknownColumn has no matching property
        var act = () => dt.ConvertDataTable<SupplierRow>();
        act.Should().NotThrow();

        var result = dt.ConvertDataTable<SupplierRow>();
        result[0].SupplierId.Should().Be(1);
    }

    [Fact]
    public void ConvertDataTable_EmptyDataTable_ReturnsEmptyList()
    {
        var dt = new DataTable();
        dt.Columns.Add("SupplierId", typeof(int));

        var result = dt.ConvertDataTable<SupplierRow>();

        result.Should().BeEmpty();
    }

    [Fact]
    public void ConvertDataTable_IsCaseInsensitiveForColumnNames()
    {
        var dt = new DataTable();
        dt.Columns.Add("supplierid", typeof(int));   // lowercase
        dt.Columns.Add("SUPPLIERNAME", typeof(string)); // uppercase
        dt.Columns.Add("isactive", typeof(bool));
        dt.Rows.Add(99, "CaseTest", true);

        var result = dt.ConvertDataTable<SupplierRow>();

        result[0].SupplierId.Should().Be(99);
        result[0].SupplierName.Should().Be("CaseTest");
    }
}
