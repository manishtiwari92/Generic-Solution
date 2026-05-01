namespace IPS.AutoPost.Plugins.InvitedClub.Models;

/// <summary>
/// Single supplier record returned by the Oracle Fusion suppliers REST API.
/// Bulk-inserted into the <c>InvitedClubSupplier</c> table.
/// </summary>
public class SupplierResponse
{
    public string SupplierId { get; set; } = string.Empty;
    public string SupplierPartyId { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public string SupplierNumber { get; set; } = string.Empty;
    public string AlternateName { get; set; } = string.Empty;
    public string TaxOrganizationTypeCode { get; set; } = string.Empty;
    public string TaxOrganizationType { get; set; } = string.Empty;
    public string SupplierTypeCode { get; set; } = string.Empty;
    public string SupplierType { get; set; } = string.Empty;
    public string InactiveDate { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string BusinessRelationshipCode { get; set; } = string.Empty;
    public string BusinessRelationship { get; set; } = string.Empty;
    public string ParentSupplierId { get; set; } = string.Empty;
    public string ParentSupplier { get; set; } = string.Empty;
    public string ParentSupplierNumber { get; set; } = string.Empty;
    public string CreationDate { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string LastUpdateDate { get; set; } = string.Empty;
    public string LastUpdatedBy { get; set; } = string.Empty;
    public string CreationSourceCode { get; set; } = string.Empty;
    public string CreationSource { get; set; } = string.Empty;
    public string DataFoxScore { get; set; } = string.Empty;
    public string DataFoxScoringCriteria { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string DUNSNumber { get; set; } = string.Empty;
    public string OneTimeSupplierFlag { get; set; } = string.Empty;
    public string RegistryId { get; set; } = string.Empty;
    public string CustomerNumber { get; set; } = string.Empty;
    public string StandardIndustryClass { get; set; } = string.Empty;
    public string IndustryCategory { get; set; } = string.Empty;
    public string IndustrySubcategory { get; set; } = string.Empty;
    public string NationalInsuranceNumber { get; set; } = string.Empty;
    public string NationalInsuranceNumberExistsFlag { get; set; } = string.Empty;
    public string CorporateWebsite { get; set; } = string.Empty;
    public string YearEstablished { get; set; } = string.Empty;
    public string MissionStatement { get; set; } = string.Empty;
    public string YearIncorporated { get; set; } = string.Empty;
    public string ChiefExecutiveTitle { get; set; } = string.Empty;
    public string ChiefExecutiveName { get; set; } = string.Empty;
    public string PrincipalTitle { get; set; } = string.Empty;
    public string PrincipalName { get; set; } = string.Empty;
    public string FiscalYearEndMonthCode { get; set; } = string.Empty;
    public string FiscalYearEndMonth { get; set; } = string.Empty;
    public string CurrentFiscalYearPotentialRevenue { get; set; } = string.Empty;
    public string PreferredFunctionalCurrencyCode { get; set; } = string.Empty;
    public string PreferredFunctionalCurrency { get; set; } = string.Empty;
    public string TaxRegistrationCountryCode { get; set; } = string.Empty;
    public string TaxRegistrationCountry { get; set; } = string.Empty;
    public string TaxRegistrationNumber { get; set; } = string.Empty;
    public string TaxpayerCountryCode { get; set; } = string.Empty;
    public string TaxpayerCountry { get; set; } = string.Empty;
    public string TaxpayerId { get; set; } = string.Empty;
    public string TaxpayerIdExistsFlag { get; set; } = string.Empty;
    public string FederalReportableFlag { get; set; } = string.Empty;
    public string FederalIncomeTaxTypeCode { get; set; } = string.Empty;
    public string FederalIncomeTaxType { get; set; } = string.Empty;
    public string StateReportableFlag { get; set; } = string.Empty;
    public string TaxReportingName { get; set; } = string.Empty;
    public string NameControl { get; set; } = string.Empty;
    public string VerificationDate { get; set; } = string.Empty;
    public string UseWithholdingTaxFlag { get; set; } = string.Empty;
    public string WithholdingTaxGroupId { get; set; } = string.Empty;
    public string WithholdingTaxGroup { get; set; } = string.Empty;
    public string BusinessClassificationNotApplicableFlag { get; set; } = string.Empty;
    public string DataFoxId { get; set; } = string.Empty;
    public string DataFoxCompanyName { get; set; } = string.Empty;
    public string DataFoxLegalName { get; set; } = string.Empty;
    public string DataFoxCompanyPrimaryURL { get; set; } = string.Empty;
    public string DataFoxNAICSCode { get; set; } = string.Empty;
    public string DataFoxCountry { get; set; } = string.Empty;
    public string DataFoxEIN { get; set; } = string.Empty;
    public string DataFoxLastSyncDate { get; set; } = string.Empty;
    public string OBNEnabledFlag { get; set; } = string.Empty;
    public string OnOFACListFlag { get; set; } = string.Empty;
    public string OFACSources { get; set; } = string.Empty;
}

/// <summary>
/// Paginated wrapper returned by the Oracle Fusion suppliers REST API.
/// Used to drive the <c>HasMore / offset</c> pagination loop in <c>InvitedClubFeedStrategy</c>.
/// </summary>
public class SupplierData
{
    public List<SupplierResponse> Items { get; set; } = new();
    public int Count { get; set; }
    public bool HasMore { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
}
