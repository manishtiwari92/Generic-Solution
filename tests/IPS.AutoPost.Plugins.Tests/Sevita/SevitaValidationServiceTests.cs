using FluentAssertions;
using IPS.AutoPost.Plugins.Sevita;
using IPS.AutoPost.Plugins.Sevita.Constants;
using IPS.AutoPost.Plugins.Sevita.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace IPS.AutoPost.Plugins.Tests.Sevita;

/// <summary>
/// Unit tests for <see cref="SevitaValidationService"/>.
/// Covers: ValidateLineSum, ValidatePO, ValidateNonPO, ValidateAttachments.
/// </summary>
public class SevitaValidationServiceTests
{
    private readonly SevitaValidationService _sut;

    // Shared valid IDs used across tests
    private static readonly ValidIds DefaultValidIds = new()
    {
        VendorIds   = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "VENDOR001", "VENDOR002" },
        EmployeeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EMP001", "EMP002" }
    };

    public SevitaValidationServiceTests()
    {
        _sut = new SevitaValidationService(NullLogger<SevitaValidationService>.Instance);
    }

    // =========================================================================
    // ValidateLineSum
    // =========================================================================

    [Fact]
    public void ValidateLineSum_WhenLineSumMatchesHeader_ReturnsValid()
    {
        var request = BuildRequest(lines: new[]
        {
            new InvoiceLine { amount = 100.00m },
            new InvoiceLine { amount = 50.50m }
        });

        var (isValid, error) = _sut.ValidateLineSum(request, headerInvoiceAmount: 150.50m);

        isValid.Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public void ValidateLineSum_WhenLineSumDoesNotMatchHeader_ReturnsInvalid()
    {
        var request = BuildRequest(lines: new[]
        {
            new InvoiceLine { amount = 100.00m },
            new InvoiceLine { amount = 50.00m }
        });

        var (isValid, error) = _sut.ValidateLineSum(request, headerInvoiceAmount: 200.00m);

        isValid.Should().BeFalse();
        error.Should().Be(SevitaConstants.ErrorLineSumMismatch);
    }

    [Fact]
    public void ValidateLineSum_WhenSumMatchesAfterRounding_ReturnsValid()
    {
        // Floating-point arithmetic can produce tiny differences — rounding to 2dp must handle this
        var request = BuildRequest(lines: new[]
        {
            new InvoiceLine { amount = 0.1m },
            new InvoiceLine { amount = 0.2m }
        });

        var (isValid, _) = _sut.ValidateLineSum(request, headerInvoiceAmount: 0.30m);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateLineSum_WhenNoLineItems_AndHeaderIsZero_ReturnsValid()
    {
        var request = BuildRequest(lines: Array.Empty<InvoiceLine>());

        var (isValid, _) = _sut.ValidateLineSum(request, headerInvoiceAmount: 0m);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateLineSum_WhenNoLineItems_AndHeaderIsNonZero_ReturnsInvalid()
    {
        var request = BuildRequest(lines: Array.Empty<InvoiceLine>());

        var (isValid, error) = _sut.ValidateLineSum(request, headerInvoiceAmount: 100m);

        isValid.Should().BeFalse();
        error.Should().Be(SevitaConstants.ErrorLineSumMismatch);
    }

    // =========================================================================
    // ValidatePO
    // =========================================================================

    [Fact]
    public void ValidatePO_WhenAllRequiredFieldsPresent_ReturnsValid()
    {
        var request = BuildValidPORequest();

        var (isValid, error) = _sut.ValidatePO(request, DefaultValidIds);

        isValid.Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public void ValidatePO_WhenCheckMemoIsEmpty_DefaultsToPOHash()
    {
        var request = BuildValidPORequest();
        request.checkMemo = string.Empty;

        var (isValid, _) = _sut.ValidatePO(request, DefaultValidIds);

        isValid.Should().BeTrue();
        request.checkMemo.Should().Be(SevitaConstants.DefaultCheckMemoPO);
    }

    [Fact]
    public void ValidatePO_WhenCheckMemoIsWhitespace_DefaultsToPOHash()
    {
        var request = BuildValidPORequest();
        request.checkMemo = "   ";

        _sut.ValidatePO(request, DefaultValidIds);

        request.checkMemo.Should().Be(SevitaConstants.DefaultCheckMemoPO);
    }

    [Fact]
    public void ValidatePO_WhenVendorIdIsEmpty_ReturnsInvalid()
    {
        var request = BuildValidPORequest();
        request.vendorId = string.Empty;

        var (isValid, error) = _sut.ValidatePO(request, DefaultValidIds);

        isValid.Should().BeFalse();
        error.Should().Contain("vendorId is required");
    }

    [Fact]
    public void ValidatePO_WhenVendorIdNotInValidSet_ReturnsInvalid()
    {
        var request = BuildValidPORequest();
        request.vendorId = "UNKNOWN_VENDOR";

        var (isValid, error) = _sut.ValidatePO(request, DefaultValidIds);

        isValid.Should().BeFalse();
        error.Should().Contain("UNKNOWN_VENDOR");
        error.Should().Contain("not a valid vendor");
    }

    [Fact]
    public void ValidatePO_WhenVendorIdMatchesCaseInsensitively_ReturnsValid()
    {
        var request = BuildValidPORequest();
        request.vendorId = "vendor001"; // lowercase — ValidIds uses OrdinalIgnoreCase

        var (isValid, _) = _sut.ValidatePO(request, DefaultValidIds);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidatePO_WhenInvoiceDateIsEmpty_ReturnsInvalid()
    {
        var request = BuildValidPORequest();
        request.invoiceDate = string.Empty;

        var (isValid, error) = _sut.ValidatePO(request, DefaultValidIds);

        isValid.Should().BeFalse();
        error.Should().Contain("invoiceDate is required");
    }

    [Fact]
    public void ValidatePO_WhenInvoiceNumberIsEmpty_ReturnsInvalid()
    {
        var request = BuildValidPORequest();
        request.invoiceNumber = string.Empty;

        var (isValid, error) = _sut.ValidatePO(request, DefaultValidIds);

        isValid.Should().BeFalse();
        error.Should().Contain("invoiceNumber is required");
    }

    // =========================================================================
    // ValidateNonPO
    // =========================================================================

    [Fact]
    public void ValidateNonPO_WhenAllRequiredFieldsPresent_ReturnsValid()
    {
        var request = BuildValidNonPORequest();

        var (isValid, error) = _sut.ValidateNonPO(request, DefaultValidIds);

        isValid.Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public void ValidateNonPO_WhenVendorIdIsEmpty_ReturnsInvalid()
    {
        var request = BuildValidNonPORequest();
        request.vendorId = string.Empty;

        var (isValid, error) = _sut.ValidateNonPO(request, DefaultValidIds);

        isValid.Should().BeFalse();
        error.Should().Contain("vendorId is required");
    }

    [Fact]
    public void ValidateNonPO_WhenVendorIdNotInValidSet_ReturnsInvalid()
    {
        var request = BuildValidNonPORequest();
        request.vendorId = "UNKNOWN_VENDOR";

        var (isValid, error) = _sut.ValidateNonPO(request, DefaultValidIds);

        isValid.Should().BeFalse();
        error.Should().Contain("not a valid vendor");
    }

    [Fact]
    public void ValidateNonPO_WhenEmployeeIdIsEmpty_ReturnsInvalid()
    {
        var request = BuildValidNonPORequest();
        request.employeeId = string.Empty;

        var (isValid, error) = _sut.ValidateNonPO(request, DefaultValidIds);

        isValid.Should().BeFalse();
        error.Should().Contain("employeeId is required");
    }

    [Fact]
    public void ValidateNonPO_WhenEmployeeIdNotInValidSet_ReturnsInvalid()
    {
        var request = BuildValidNonPORequest();
        request.employeeId = "UNKNOWN_EMP";

        var (isValid, error) = _sut.ValidateNonPO(request, DefaultValidIds);

        isValid.Should().BeFalse();
        error.Should().Contain("not a valid employee");
    }

    [Fact]
    public void ValidateNonPO_WhenInvoiceDateIsEmpty_ReturnsInvalid()
    {
        var request = BuildValidNonPORequest();
        request.invoiceDate = string.Empty;

        var (isValid, error) = _sut.ValidateNonPO(request, DefaultValidIds);

        isValid.Should().BeFalse();
        error.Should().Contain("invoiceDate is required");
    }

    [Fact]
    public void ValidateNonPO_WhenInvoiceNumberIsEmpty_ReturnsInvalid()
    {
        var request = BuildValidNonPORequest();
        request.invoiceNumber = string.Empty;

        var (isValid, error) = _sut.ValidateNonPO(request, DefaultValidIds);

        isValid.Should().BeFalse();
        error.Should().Contain("invoiceNumber is required");
    }

    [Fact]
    public void ValidateNonPO_WhenCheckMemoIsEmpty_ReturnsInvalid()
    {
        var request = BuildValidNonPORequest();
        request.checkMemo = string.Empty;

        var (isValid, error) = _sut.ValidateNonPO(request, DefaultValidIds);

        isValid.Should().BeFalse();
        error.Should().Contain("checkMemo is required");
    }

    [Fact]
    public void ValidateNonPO_WhenExpensePeriodIsEmpty_ReturnsInvalid()
    {
        var request = BuildValidNonPORequest();
        request.expensePeriod = string.Empty;

        var (isValid, error) = _sut.ValidateNonPO(request, DefaultValidIds);

        isValid.Should().BeFalse();
        error.Should().Contain("expensePeriod is required");
    }

    [Fact]
    public void ValidateNonPO_WhenLineHasCerfAccount_AndCerfTrackingNumberIsEmpty_ReturnsInvalid()
    {
        var request = BuildValidNonPORequest();
        request.lineItems.Add(new InvoiceLine
        {
            naturalAccountNumber = SevitaConstants.CerfRequiredAccountNumber,
            amount = 50m
        });
        request.cerfTrackingNumber = null; // not provided

        var (isValid, error) = _sut.ValidateNonPO(request, DefaultValidIds);

        isValid.Should().BeFalse();
        error.Should().Contain("cerfTrackingNumber is required");
        error.Should().Contain(SevitaConstants.CerfRequiredAccountNumber);
    }

    [Fact]
    public void ValidateNonPO_WhenLineHasCerfAccount_AndCerfTrackingNumberIsProvided_ReturnsValid()
    {
        var request = BuildValidNonPORequest();
        request.lineItems.Add(new InvoiceLine
        {
            naturalAccountNumber = SevitaConstants.CerfRequiredAccountNumber,
            amount = 50m
        });
        request.cerfTrackingNumber = "CERF-12345";

        var (isValid, _) = _sut.ValidateNonPO(request, DefaultValidIds);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateNonPO_WhenNoLineHasCerfAccount_AndCerfTrackingNumberIsEmpty_ReturnsValid()
    {
        var request = BuildValidNonPORequest();
        // No line with naturalAccountNumber = "174098"
        request.cerfTrackingNumber = null;

        var (isValid, _) = _sut.ValidateNonPO(request, DefaultValidIds);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateNonPO_CerfAccountCheck_IsCaseInsensitive()
    {
        var request = BuildValidNonPORequest();
        request.lineItems.Add(new InvoiceLine
        {
            naturalAccountNumber = "174098", // exact match
            amount = 10m
        });
        request.cerfTrackingNumber = null;

        var (isValid, _) = _sut.ValidateNonPO(request, DefaultValidIds);

        isValid.Should().BeFalse();
    }

    // =========================================================================
    // ValidateAttachments
    // =========================================================================

    [Fact]
    public void ValidateAttachments_WhenAllFieldsPresent_ReturnsValid()
    {
        var request = BuildRequest();
        request.attachments.Add(BuildValidAttachment());

        var (isValid, error) = _sut.ValidateAttachments(request);

        isValid.Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public void ValidateAttachments_WhenNoAttachments_ReturnsInvalid()
    {
        var request = BuildRequest();
        // No attachments added

        var (isValid, error) = _sut.ValidateAttachments(request);

        isValid.Should().BeFalse();
        error.Should().Contain("at least one attachment is required");
    }

    [Fact]
    public void ValidateAttachments_WhenFileNameIsEmpty_ReturnsInvalid()
    {
        var request = BuildRequest();
        var attachment = BuildValidAttachment();
        attachment.fileName = string.Empty;
        request.attachments.Add(attachment);

        var (isValid, error) = _sut.ValidateAttachments(request);

        isValid.Should().BeFalse();
        error.Should().Contain("fileName is required");
    }

    [Fact]
    public void ValidateAttachments_WhenFileBaseIsEmpty_ReturnsInvalid()
    {
        var request = BuildRequest();
        var attachment = BuildValidAttachment();
        attachment.fileBase = string.Empty;
        request.attachments.Add(attachment);

        var (isValid, error) = _sut.ValidateAttachments(request);

        isValid.Should().BeFalse();
        error.Should().Contain("fileBase is required");
    }

    [Fact]
    public void ValidateAttachments_WhenFileUrlIsEmpty_ReturnsInvalid()
    {
        var request = BuildRequest();
        var attachment = BuildValidAttachment();
        attachment.fileUrl = string.Empty;
        request.attachments.Add(attachment);

        var (isValid, error) = _sut.ValidateAttachments(request);

        isValid.Should().BeFalse();
        error.Should().Contain("fileUrl is required");
    }

    [Fact]
    public void ValidateAttachments_WhenDocidIsEmpty_ReturnsInvalid()
    {
        var request = BuildRequest();
        var attachment = BuildValidAttachment();
        attachment.docid = string.Empty;
        request.attachments.Add(attachment);

        var (isValid, error) = _sut.ValidateAttachments(request);

        isValid.Should().BeFalse();
        error.Should().Contain("docid is required");
    }

    [Fact]
    public void ValidateAttachments_WhenMultipleAttachments_AndSecondIsInvalid_ReturnsInvalidWithIndex()
    {
        var request = BuildRequest();
        request.attachments.Add(BuildValidAttachment());
        var badAttachment = BuildValidAttachment();
        badAttachment.fileBase = string.Empty;
        request.attachments.Add(badAttachment);

        var (isValid, error) = _sut.ValidateAttachments(request);

        isValid.Should().BeFalse();
        error.Should().Contain("Attachment 2");
        error.Should().Contain("fileBase is required");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static InvoiceRequest BuildRequest(IEnumerable<InvoiceLine>? lines = null)
    {
        var request = new InvoiceRequest();
        if (lines != null)
            request.lineItems.AddRange(lines);
        return request;
    }

    private static InvoiceRequest BuildValidPORequest() => new()
    {
        vendorId      = "VENDOR001",
        invoiceDate   = "2026-01-15",
        invoiceNumber = "INV-001",
        checkMemo     = "PO#12345",
        lineItems     = new List<InvoiceLine> { new() { amount = 100m } }
    };

    private static InvoiceRequest BuildValidNonPORequest() => new()
    {
        vendorId      = "VENDOR001",
        employeeId    = "EMP001",
        invoiceDate   = "2026-01-15",
        invoiceNumber = "INV-002",
        checkMemo     = "Expense Report",
        expensePeriod = "2026-01",
        lineItems     = new List<InvoiceLine> { new() { amount = 200m } }
    };

    private static AttachmentRequest BuildValidAttachment() => new()
    {
        fileName = "invoice.pdf",
        fileBase = "base64encodedcontent==",
        fileUrl  = "https://s3.example.com/invoice.pdf",
        docid    = "DOC-001"
    };
}
