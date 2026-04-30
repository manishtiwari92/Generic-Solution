namespace IPS.AutoPost.Core.Migrations.Entities;

/// <summary>
/// EF Core entity for the <c>generic_post_history</c> table.
/// Replaces all <c>xxx_posted_records_history</c> tables.
/// Written by the Core_Engine after every workitem is processed.
/// </summary>
public class GenericPostHistoryEntity
{
    public long Id { get; set; }
    public string ClientType { get; set; } = string.Empty;
    public int JobId { get; set; }
    public long ItemId { get; set; }

    /// <summary>
    /// Processing step name (e.g. 'InvoicePost', 'AttachmentPost', 'CalculateTax').
    /// Allows multi-step plugins to record each API call separately.
    /// </summary>
    public string? StepName { get; set; }

    public string? PostRequest { get; set; }
    public string? PostResponse { get; set; }
    public DateTime PostDate { get; set; }
    public int PostedBy { get; set; }
    public bool ManuallyPosted { get; set; }
    public string? OutputFilePath { get; set; }
}
