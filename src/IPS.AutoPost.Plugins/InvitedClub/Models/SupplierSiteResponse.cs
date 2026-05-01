namespace IPS.AutoPost.Plugins.InvitedClub.Models;

/// <summary>
/// Single supplier site record returned by the Oracle Fusion
/// suppliers/{supplierId}/child/sites REST API.
/// Bulk-inserted into the <c>InvitedClubSupplierSite</c> table.
/// Note: <c>SupplierId</c> is injected client-side after deserialization
/// because the API does not include it in the response body.
/// After insert, <c>InvitedClub_UpdateSupplierSiteInSupplierAddress</c> SP is called.
/// </summary>
public class SupplierSiteResponse
{
    /// <summary>Injected after deserialization from the parent supplier loop.</summary>
    public string SupplierId { get; set; } = string.Empty;

    public string SupplierSiteId { get; set; } = string.Empty;
    public string SupplierSite { get; set; } = string.Empty;
    public string ProcurementBUId { get; set; } = string.Empty;
    public string ProcurementBU { get; set; } = string.Empty;
    public string SupplierAddressId { get; set; } = string.Empty;
    public string SupplierAddressName { get; set; } = string.Empty;
    public string InactiveDate { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SitePurposeSourcingOnlyFlag { get; set; } = string.Empty;
    public string SitePurposePurchasingFlag { get; set; } = string.Empty;
    public string SitePurposeProcurementCardFlag { get; set; } = string.Empty;
    public string SitePurposePayFlag { get; set; } = string.Empty;
    public string SitePurposePrimaryPayFlag { get; set; } = string.Empty;
    public string IncomeTaxReportingSiteFlag { get; set; } = string.Empty;
    public string AlternateSiteName { get; set; } = string.Empty;
    public string CustomerNumber { get; set; } = string.Empty;
    public string B2BCommunicationMethodCode { get; set; } = string.Empty;
    public string B2BCommunicationMethod { get; set; } = string.Empty;
    public string B2BSupplierSiteCode { get; set; } = string.Empty;
    public string CommunicationMethodCode { get; set; } = string.Empty;
    public string CommunicationMethod { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FaxCountryCode { get; set; } = string.Empty;
    public string FaxAreaCode { get; set; } = string.Empty;
    public string FaxNumber { get; set; } = string.Empty;
    public string HoldAllNewPurchasingDocumentsFlag { get; set; } = string.Empty;
    public string PurchasingHoldReason { get; set; } = string.Empty;
    public string AllNewPurchasingDocumentsHoldDate { get; set; } = string.Empty;
    public string AllNewPurchasingDocumentsHoldBy { get; set; } = string.Empty;
    public string RequiredAcknowledgmentCode { get; set; } = string.Empty;
    public string RequiredAcknowledgment { get; set; } = string.Empty;
    public string AcknowledgmentWithinDays { get; set; } = string.Empty;
    public string CarrierId { get; set; } = string.Empty;
    public string Carrier { get; set; } = string.Empty;
    public string ModeOfTransportCode { get; set; } = string.Empty;
    public string ModeOfTransport { get; set; } = string.Empty;
    public string ServiceLevelCode { get; set; } = string.Empty;
    public string ServiceLevel { get; set; } = string.Empty;
    public string FreightTermsCode { get; set; } = string.Empty;
    public string FreightTerms { get; set; } = string.Empty;
    public string PayOnReceiptFlag { get; set; } = string.Empty;
    public string FOBCode { get; set; } = string.Empty;
    public string FOB { get; set; } = string.Empty;
    public string CountryOfOriginCode { get; set; } = string.Empty;
    public string CountryOfOrigin { get; set; } = string.Empty;
    public string BuyerManagedTransportationCode { get; set; } = string.Empty;
    public string BuyerManagedTransportation { get; set; } = string.Empty;
    public string PayOnUseFlag { get; set; } = string.Empty;
    public string AgingOnsetPointCode { get; set; } = string.Empty;
    public string AgingOnsetPoint { get; set; } = string.Empty;
    public string AgingPeriodDays { get; set; } = string.Empty;
    public string ConsumptionAdviceFrequencyCode { get; set; } = string.Empty;
    public string ConsumptionAdviceFrequency { get; set; } = string.Empty;
    public string ConsumptionAdviceSummaryCode { get; set; } = string.Empty;
    public string ConsumptionAdviceSummary { get; set; } = string.Empty;
    public string AlternatePaySiteId { get; set; } = string.Empty;
    public string AlternatePaySite { get; set; } = string.Empty;
    public string InvoiceSummaryLevelCode { get; set; } = string.Empty;
    public string InvoiceSummaryLevel { get; set; } = string.Empty;
    public string GaplessInvoiceNumberingFlag { get; set; } = string.Empty;
    public string SellingCompanyIdentifier { get; set; } = string.Empty;
    public string CreateDebitMemoFromReturnFlag { get; set; } = string.Empty;
    public string ShipToExceptionCode { get; set; } = string.Empty;
    public string ShipToException { get; set; } = string.Empty;
    public string ReceiptRoutingId { get; set; } = string.Empty;
    public string ReceiptRouting { get; set; } = string.Empty;
    public string OverReceiptTolerance { get; set; } = string.Empty;
    public string OverReceiptActionCode { get; set; } = string.Empty;
    public string OverReceiptAction { get; set; } = string.Empty;
    public string EarlyReceiptToleranceInDays { get; set; } = string.Empty;
    public string LateReceiptToleranceInDays { get; set; } = string.Empty;
    public string AllowSubstituteReceiptsCode { get; set; } = string.Empty;
    public string AllowSubstituteReceipts { get; set; } = string.Empty;
    public string AllowUnorderedReceiptsFlag { get; set; } = string.Empty;
    public string ReceiptDateExceptionCode { get; set; } = string.Empty;
    public string ReceiptDateException { get; set; } = string.Empty;
    public string InvoiceCurrencyCode { get; set; } = string.Empty;
    public string InvoiceCurrency { get; set; } = string.Empty;
    public string InvoiceAmountLimit { get; set; } = string.Empty;
    public string InvoiceMatchOptionCode { get; set; } = string.Empty;
    public string InvoiceMatchOption { get; set; } = string.Empty;
    public string MatchApprovalLevelCode { get; set; } = string.Empty;
    public string MatchApprovalLevel { get; set; } = string.Empty;
    public string QuantityTolerancesId { get; set; } = string.Empty;
    public string QuantityTolerances { get; set; } = string.Empty;
    public string AmountTolerancesId { get; set; } = string.Empty;
    public string AmountTolerances { get; set; } = string.Empty;
    public string InvoiceChannelCode { get; set; } = string.Empty;
    public string InvoiceChannel { get; set; } = string.Empty;
    public string PaymentCurrencyCode { get; set; } = string.Empty;
    public string PaymentCurrency { get; set; } = string.Empty;
    public string PaymentPriority { get; set; } = string.Empty;
    public string PayGroupCode { get; set; } = string.Empty;
    public string PayGroup { get; set; } = string.Empty;
    public string HoldAllInvoicesFlag { get; set; } = string.Empty;
    public string HoldUnmatchedInvoicesCode { get; set; } = string.Empty;
    public string HoldUnmatchedInvoices { get; set; } = string.Empty;
    public string HoldUnvalidatedInvoicesFlag { get; set; } = string.Empty;
    public string PaymentHoldDate { get; set; } = string.Empty;
    public string PaymentHoldReason { get; set; } = string.Empty;
    public string PaymentTermsId { get; set; } = string.Empty;
    public string PaymentTerms { get; set; } = string.Empty;
    public string PaymentTermsDateBasisCode { get; set; } = string.Empty;
    public string PaymentTermsDateBasis { get; set; } = string.Empty;
    public string PayDateBasisCode { get; set; } = string.Empty;
    public string PayDateBasis { get; set; } = string.Empty;
    public string BankChargeDeductionTypeCode { get; set; } = string.Empty;
    public string BankChargeDeductionType { get; set; } = string.Empty;
    public string AlwaysTakeDiscountCode { get; set; } = string.Empty;
    public string AlwaysTakeDiscount { get; set; } = string.Empty;
    public string ExcludeFreightFromDiscountCode { get; set; } = string.Empty;
    public string ExcludeFreightFromDiscount { get; set; } = string.Empty;
    public string ExcludeTaxFromDiscountCode { get; set; } = string.Empty;
    public string ExcludeTaxFromDiscount { get; set; } = string.Empty;
    public string CreateInterestInvoicesCode { get; set; } = string.Empty;
    public string CreateInterestInvoices { get; set; } = string.Empty;
    public string CreationDate { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string LastUpdateDate { get; set; } = string.Empty;
    public string LastUpdatedBy { get; set; } = string.Empty;
    public string OverrideB2BCommunicationforSpecialHandlingOrdersFlag { get; set; } = string.Empty;
}

/// <summary>
/// Paginated wrapper returned by the Oracle Fusion supplier sites REST API.
/// </summary>
public class SupplierSiteData
{
    public List<SupplierSiteResponse> Items { get; set; } = new();
    public int Count { get; set; }
    public bool HasMore { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
}
