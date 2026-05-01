using Newtonsoft.Json;

namespace IPS.AutoPost.Plugins.InvitedClub.Models;

/// <summary>
/// Single Chart of Accounts (COA) record returned by the Oracle Fusion
/// accountCombinationsLOV REST API.
/// Bulk-inserted into the <c>InvitedClubCOA</c> table.
/// Fields prefixed with <c>_</c> in the JSON response require explicit
/// <see cref="JsonPropertyAttribute"/> mappings.
/// </summary>
public class COAResponse
{
    public string AccountType { get; set; } = string.Empty;
    public string DetailBudgetingAllowedFlag { get; set; } = string.Empty;
    public string JgzzReconFlag { get; set; } = string.Empty;
    public string FinancialCategory { get; set; } = string.Empty;
    public string DetailPostingAllowedFlag { get; set; } = string.Empty;
    public string Reference3 { get; set; } = string.Empty;
    public string SummaryFlag { get; set; } = string.Empty;
    public string StartDateActive { get; set; } = string.Empty;
    public string EndDateActive { get; set; } = string.Empty;
    public string EnabledFlag { get; set; } = string.Empty;

    [JsonProperty("_CODE_COMBINATION_ID")]
    public string CodeCombinationId { get; set; } = string.Empty;

    [JsonProperty("_CHART_OF_ACCOUNTS_ID")]
    public string ChartOfAccountsId { get; set; } = string.Empty;

    [JsonProperty("entity")]
    public string Entity { get; set; } = string.Empty;

    [JsonProperty("department")]
    public string Department { get; set; } = string.Empty;

    [JsonProperty("account")]
    public string Account { get; set; } = string.Empty;

    [JsonProperty("subAccount")]
    public string SubAccount { get; set; } = string.Empty;

    [JsonProperty("location")]
    public string Location { get; set; } = string.Empty;

    [JsonProperty("future1")]
    public string Future1 { get; set; } = string.Empty;

    [JsonProperty("future2")]
    public string Future2 { get; set; } = string.Empty;
}

/// <summary>
/// Paginated wrapper returned by the Oracle Fusion accountCombinationsLOV REST API.
/// </summary>
public class COAData
{
    public List<COAResponse> Items { get; set; } = new();
    public int Count { get; set; }
    public bool HasMore { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
}
