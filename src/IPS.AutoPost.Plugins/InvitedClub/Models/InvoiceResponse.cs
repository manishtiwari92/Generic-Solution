namespace IPS.AutoPost.Plugins.InvitedClub.Models;

/// <summary>
/// Internal result of the Oracle Fusion invoice POST call.
/// <c>Status = 0</c> indicates success (HTTP 201); <c>Status = -1</c> indicates failure.
/// </summary>
public class InvoiceResponse
{
    /// <summary>0 = success, -1 = failure.</summary>
    public int Status { get; set; }

    /// <summary>Oracle Fusion InvoiceId returned on HTTP 201. Stored in WFInvitedClubsIndexHeader.InvoiceId.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>Raw response body from Oracle Fusion (used for history logging).</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>Error message extracted from the response body on failure.</summary>
    public string ErrorMsg { get; set; } = string.Empty;
}

/// <summary>
/// Internal result of the Oracle Fusion attachment POST call.
/// <c>Status = 0</c> indicates success (HTTP 201); <c>Status = -1</c> indicates failure.
/// </summary>
public class AttachmentResponse
{
    /// <summary>0 = success, -1 = failure.</summary>
    public int Status { get; set; }

    /// <summary>Oracle Fusion AttachedDocumentId returned on HTTP 201. Stored in WFInvitedClubsIndexHeader.AttachedDocumentId.</summary>
    public string AttachedDocumentId { get; set; } = string.Empty;

    /// <summary>Raw response body from Oracle Fusion (used for history logging).</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>Error message extracted from the response body on failure.</summary>
    public string ErrorMsg { get; set; } = string.Empty;
}

/// <summary>
/// Internal result of the Oracle Fusion calculateTax action call.
/// <c>Status = 0</c> indicates success (HTTP 200); <c>Status = -1</c> indicates failure.
/// </summary>
public class InvoiceCalculateTaxResponse
{
    /// <summary>0 = success, -1 = failure.</summary>
    public int Status { get; set; }

    /// <summary>Raw response body from Oracle Fusion (used for history logging).</summary>
    public string Result { get; set; } = string.Empty;
}
