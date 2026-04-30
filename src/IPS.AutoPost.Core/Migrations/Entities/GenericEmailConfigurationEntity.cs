namespace IPS.AutoPost.Core.Migrations.Entities;

/// <summary>
/// EF Core entity for the <c>generic_email_configuration</c> table.
/// Stores email notification settings per job and notification type.
/// </summary>
public class GenericEmailConfigurationEntity
{
    public int Id { get; set; }

    /// <summary>Foreign key to <c>generic_job_configuration.id</c>.</summary>
    public int JobConfigId { get; set; }

    /// <summary>
    /// Email notification type.
    /// Values: 'POST_FAILURE', 'IMAGE_FAILURE', 'MISSING_COA', 'FEED_FAILURE'.
    /// </summary>
    public string EmailType { get; set; } = string.Empty;

    public string? EmailTo { get; set; }
    public string? EmailCc { get; set; }
    public string? EmailBcc { get; set; }
    public string? EmailSubject { get; set; }
    public string? EmailTemplate { get; set; }
    public string? SmtpServer { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool SmtpUseSsl { get; set; }
    public bool IsActive { get; set; }

    // Navigation property
    public GenericJobConfigurationEntity JobConfiguration { get; set; } = null!;
}
