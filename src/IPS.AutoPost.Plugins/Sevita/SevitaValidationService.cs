using IPS.AutoPost.Plugins.Sevita.Constants;
using IPS.AutoPost.Plugins.Sevita.Models;
using Microsoft.Extensions.Logging;

namespace IPS.AutoPost.Plugins.Sevita;

/// <summary>
/// Validates Sevita invoice request payloads before calling the external API.
/// </summary>
/// <remarks>
/// <para>
/// All validation methods return a <c>(bool IsValid, string ErrorMessage)</c> tuple.
/// When validation fails, the workitem is routed to the configured fail queue with
/// the error message written to the <c>question</c> field — the external API is never called.
/// </para>
/// <para>
/// Validation is split into four concerns:
/// <list type="bullet">
///   <item><see cref="ValidateLineSum"/> — line amounts must sum to the header invoice amount.</item>
///   <item><see cref="ValidatePO"/> — PO-record required fields and vendor ID lookup.</item>
///   <item><see cref="ValidateNonPO"/> — Non-PO required fields, both ID lookups, and CERF rule.</item>
///   <item><see cref="ValidateAttachments"/> — all four attachment fields must be present.</item>
/// </list>
/// </para>
/// </remarks>
public class SevitaValidationService
{
    private readonly ILogger<SevitaValidationService> _logger;

    public SevitaValidationService(ILogger<SevitaValidationService> logger)
    {
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // 15.2 ValidateLineSum
    // -----------------------------------------------------------------------

    /// <summary>
    /// Validates that the sum of all line item amounts equals the header invoice amount.
    /// </summary>
    /// <param name="request">The invoice request whose line items are summed.</param>
    /// <param name="headerInvoiceAmount">
    /// The expected total invoice amount from the header row
    /// (e.g., <c>WFSevitaIndexHeader.InvoiceAmount</c>).
    /// </param>
    /// <returns>
    /// <c>(true, "")</c> when the sum matches; otherwise
    /// <c>(false, SevitaConstants.ErrorLineSumMismatch)</c>.
    /// </returns>
    public virtual (bool IsValid, string ErrorMessage) ValidateLineSum(
        InvoiceRequest request,
        decimal headerInvoiceAmount)
    {
        var lineSum = request.lineItems.Sum(l => l.amount);

        // Round both values to 2 decimal places to avoid floating-point drift
        var roundedLineSum = Math.Round(lineSum, 2);
        var roundedHeader = Math.Round(headerInvoiceAmount, 2);

        if (roundedLineSum == roundedHeader)
            return (true, string.Empty);

        _logger.LogWarning(
            "Line sum mismatch. LineSum={LineSum}, HeaderAmount={HeaderAmount}",
            roundedLineSum, roundedHeader);

        return (false, SevitaConstants.ErrorLineSumMismatch);
    }

    // -----------------------------------------------------------------------
    // 15.3 ValidatePO
    // -----------------------------------------------------------------------

    /// <summary>
    /// Validates a PO invoice record.
    /// </summary>
    /// <remarks>
    /// Rules:
    /// <list type="bullet">
    ///   <item><c>vendorId</c> must be non-empty and present in <paramref name="validIds"/>.VendorIds.</item>
    ///   <item><c>invoiceDate</c> must be non-empty.</item>
    ///   <item><c>invoiceNumber</c> must be non-empty.</item>
    ///   <item><c>checkMemo</c> defaults to <c>"PO#"</c> when empty (mutates the request in place).</item>
    /// </list>
    /// </remarks>
    /// <param name="request">The invoice request to validate. <c>checkMemo</c> may be mutated.</param>
    /// <param name="validIds">Runtime-loaded valid vendor and employee ID sets.</param>
    /// <returns>
    /// <c>(true, "")</c> when all rules pass; otherwise <c>(false, errorMessage)</c>.
    /// </returns>
    public virtual (bool IsValid, string ErrorMessage) ValidatePO(
        InvoiceRequest request,
        ValidIds validIds)
    {
        // Default checkMemo to "PO#" when empty
        if (string.IsNullOrWhiteSpace(request.checkMemo))
            request.checkMemo = SevitaConstants.DefaultCheckMemoPO;

        // Required: vendorId
        if (string.IsNullOrWhiteSpace(request.vendorId))
            return (false, "PO validation failed: vendorId is required.");

        // Required: vendorId must be in the valid set
        if (!validIds.VendorIds.Contains(request.vendorId))
            return (false, $"PO validation failed: vendorId '{request.vendorId}' is not a valid vendor.");

        // Required: invoiceDate
        if (string.IsNullOrWhiteSpace(request.invoiceDate))
            return (false, "PO validation failed: invoiceDate is required.");

        // Required: invoiceNumber
        if (string.IsNullOrWhiteSpace(request.invoiceNumber))
            return (false, "PO validation failed: invoiceNumber is required.");

        // checkMemo is guaranteed non-empty at this point (defaulted above)

        return (true, string.Empty);
    }

    // -----------------------------------------------------------------------
    // 15.4 ValidateNonPO
    // -----------------------------------------------------------------------

    /// <summary>
    /// Validates a Non-PO invoice record.
    /// </summary>
    /// <remarks>
    /// Rules:
    /// <list type="bullet">
    ///   <item><c>vendorId</c> must be non-empty and present in <paramref name="validIds"/>.VendorIds.</item>
    ///   <item><c>employeeId</c> must be non-empty and present in <paramref name="validIds"/>.EmployeeIds.</item>
    ///   <item><c>invoiceDate</c> must be non-empty.</item>
    ///   <item><c>invoiceNumber</c> must be non-empty.</item>
    ///   <item><c>checkMemo</c> must be non-empty.</item>
    ///   <item><c>expensePeriod</c> must be non-empty.</item>
    ///   <item>
    ///     <c>cerfTrackingNumber</c> is required when any line item has
    ///     <c>naturalAccountNumber == "174098"</c>.
    ///   </item>
    /// </list>
    /// </remarks>
    /// <param name="request">The invoice request to validate.</param>
    /// <param name="validIds">Runtime-loaded valid vendor and employee ID sets.</param>
    /// <returns>
    /// <c>(true, "")</c> when all rules pass; otherwise <c>(false, errorMessage)</c>.
    /// </returns>
    public virtual (bool IsValid, string ErrorMessage) ValidateNonPO(
        InvoiceRequest request,
        ValidIds validIds)
    {
        // Required: vendorId
        if (string.IsNullOrWhiteSpace(request.vendorId))
            return (false, "Non-PO validation failed: vendorId is required.");

        // Required: vendorId must be in the valid set
        if (!validIds.VendorIds.Contains(request.vendorId))
            return (false, $"Non-PO validation failed: vendorId '{request.vendorId}' is not a valid vendor.");

        // Required: employeeId
        if (string.IsNullOrWhiteSpace(request.employeeId))
            return (false, "Non-PO validation failed: employeeId is required.");

        // Required: employeeId must be in the valid set
        if (!validIds.EmployeeIds.Contains(request.employeeId))
            return (false, $"Non-PO validation failed: employeeId '{request.employeeId}' is not a valid employee.");

        // Required: invoiceDate
        if (string.IsNullOrWhiteSpace(request.invoiceDate))
            return (false, "Non-PO validation failed: invoiceDate is required.");

        // Required: invoiceNumber
        if (string.IsNullOrWhiteSpace(request.invoiceNumber))
            return (false, "Non-PO validation failed: invoiceNumber is required.");

        // Required: checkMemo
        if (string.IsNullOrWhiteSpace(request.checkMemo))
            return (false, "Non-PO validation failed: checkMemo is required.");

        // Required: expensePeriod
        if (string.IsNullOrWhiteSpace(request.expensePeriod))
            return (false, "Non-PO validation failed: expensePeriod is required.");

        // CERF rule: cerfTrackingNumber required when any line has naturalAccountNumber = "174098"
        bool hasCerfAccount = request.lineItems.Any(
            l => string.Equals(l.naturalAccountNumber, SevitaConstants.CerfRequiredAccountNumber,
                StringComparison.OrdinalIgnoreCase));

        if (hasCerfAccount && string.IsNullOrWhiteSpace(request.cerfTrackingNumber))
        {
            return (false,
                $"Non-PO validation failed: cerfTrackingNumber is required when any line has " +
                $"naturalAccountNumber '{SevitaConstants.CerfRequiredAccountNumber}'.");
        }

        return (true, string.Empty);
    }

    // -----------------------------------------------------------------------
    // 15.5 ValidateAttachments
    // -----------------------------------------------------------------------

    /// <summary>
    /// Validates that every attachment in the invoice request has all four required fields:
    /// <c>fileName</c>, <c>fileBase</c>, <c>fileUrl</c>, and <c>docid</c>.
    /// </summary>
    /// <param name="request">The invoice request whose attachments are validated.</param>
    /// <returns>
    /// <c>(true, "")</c> when all attachments are valid; otherwise
    /// <c>(false, errorMessage)</c> describing the first invalid attachment.
    /// </returns>
    public virtual (bool IsValid, string ErrorMessage) ValidateAttachments(InvoiceRequest request)
    {
        if (request.attachments.Count == 0)
            return (false, "Attachment validation failed: at least one attachment is required.");

        for (int i = 0; i < request.attachments.Count; i++)
        {
            var attachment = request.attachments[i];
            var index = i + 1;

            if (string.IsNullOrWhiteSpace(attachment.fileName))
                return (false, $"Attachment {index} validation failed: fileName is required.");

            if (string.IsNullOrWhiteSpace(attachment.fileBase))
                return (false, $"Attachment {index} validation failed: fileBase is required.");

            if (string.IsNullOrWhiteSpace(attachment.fileUrl))
                return (false, $"Attachment {index} validation failed: fileUrl is required.");

            if (string.IsNullOrWhiteSpace(attachment.docid))
                return (false, $"Attachment {index} validation failed: docid is required.");
        }

        return (true, string.Empty);
    }
}
