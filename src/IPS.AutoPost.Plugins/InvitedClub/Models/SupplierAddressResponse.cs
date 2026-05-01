namespace IPS.AutoPost.Plugins.InvitedClub.Models;

/// <summary>
/// Single supplier address record returned by the Oracle Fusion
/// suppliers/{supplierId}/child/addresses REST API.
/// Bulk-inserted into the <c>InvitedClubSupplierAddress</c> table.
/// Note: <c>SupplierId</c> is injected client-side after deserialization
/// because the API does not include it in the response body.
/// </summary>
public class SupplierAddressResponse
{
    public string SupplierAddressId { get; set; } = string.Empty;

    /// <summary>Injected after deserialization from the parent supplier loop.</summary>
    public string SupplierId { get; set; } = string.Empty;

    public string AddressName { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string AddressLine2 { get; set; } = string.Empty;
    public string AddressLine3 { get; set; } = string.Empty;
    public string AddressLine4 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string PostalCodeExtension { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string County { get; set; } = string.Empty;
    public string Building { get; set; } = string.Empty;
    public string FloorNumber { get; set; } = string.Empty;
    public string PhoneticAddress { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Addressee { get; set; } = string.Empty;
    public string GlobalLocationNumber { get; set; } = string.Empty;
    public string AdditionalAddressAttribute1 { get; set; } = string.Empty;
    public string AdditionalAddressAttribute2 { get; set; } = string.Empty;
    public string AdditionalAddressAttribute3 { get; set; } = string.Empty;
    public string AdditionalAddressAttribute4 { get; set; } = string.Empty;
    public string AdditionalAddressAttribute5 { get; set; } = string.Empty;
    public string AddressPurposeOrderingFlag { get; set; } = string.Empty;
    public string AddressPurposeRemitToFlag { get; set; } = string.Empty;
    public string AddressPurposeRFQOrBiddingFlag { get; set; } = string.Empty;
    public string PhoneCountryCode { get; set; } = string.Empty;
    public string PhoneAreaCode { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PhoneExtension { get; set; } = string.Empty;
    public string FaxCountryCode { get; set; } = string.Empty;
    public string FaxAreaCode { get; set; } = string.Empty;
    public string FaxNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string InactiveDate { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CreationDate { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string LastUpdateDate { get; set; } = string.Empty;
    public string LastUpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Paginated wrapper returned by the Oracle Fusion supplier addresses REST API.
/// </summary>
public class SupplierAddressData
{
    public List<SupplierAddressResponse> Items { get; set; } = new();
    public int Count { get; set; }
    public bool HasMore { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
}
