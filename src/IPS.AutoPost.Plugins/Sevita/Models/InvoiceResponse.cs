namespace IPS.AutoPost.Plugins.Sevita.Models;

/// <summary>
/// Internal result of a Sevita invoice POST call, produced by
/// <c>SevitaPostStrategy.PostInvoiceAsync()</c>.
/// </summary>
/// <remarks>
/// <para><c>Status = 0</c> indicates success (HTTP 201 Created).</para>
/// <para><c>Status = -1</c> indicates failure (any non-201 response).</para>
/// </remarks>
public class InvoiceResponse
{
    /// <summary>
    /// <c>0</c> = success (HTTP 201); <c>-1</c> = failure.
    /// </summary>
    public int Status { get; set; }

    /// <summary>
    /// Sevita invoice identifier extracted from the first property name of the
    /// <c>invoiceIds</c> object in the HTTP 201 response JSON.
    /// Stored in the history record on success.
    /// </summary>
    public string? InvoiceId { get; set; }

    /// <summary>
    /// Raw response body from the Sevita API (used for history logging).
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Human-readable error message extracted from the response on failure.
    /// <para>
    /// For HTTP 500: set to <c>"Internal Server error occurred while posting invoice."</c>
    /// </para>
    /// <para>
    /// For other non-201 responses: extracted from <c>recordErrors</c>, <c>message</c>,
    /// <c>invoiceIds</c>, or <c>failedRecords</c> fields in the response JSON.
    /// </para>
    /// </summary>
    public string? ErrorMsg { get; set; }
}

/// <summary>
/// Deserialized shape of the Sevita API HTTP 201 response body.
/// Used internally by <c>SevitaPostStrategy.PostInvoiceAsync()</c> to extract
/// the <c>InvoiceId</c> from the <c>invoiceIds</c> object's first property name.
/// </summary>
public class InvoicePostResponse
{
    /// <summary>
    /// Dictionary of invoice IDs returned by the Sevita API on success.
    /// The <c>InvoiceId</c> is extracted from the first key of this dictionary.
    /// </summary>
    public Dictionary<string, object>? invoiceIds { get; set; }

    /// <summary>
    /// Error records returned by the Sevita API on non-201 responses.
    /// </summary>
    public object? recordErrors { get; set; }

    /// <summary>
    /// Error message returned by the Sevita API on non-201 responses.
    /// </summary>
    public string? message { get; set; }

    /// <summary>
    /// Failed record details returned by the Sevita API on non-201 responses.
    /// </summary>
    public object? failedRecords { get; set; }
}
