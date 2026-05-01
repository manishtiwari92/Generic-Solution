namespace IPS.AutoPost.Plugins.InvitedClub.Models;

/// <summary>
/// InvitedClub email notification configuration loaded from the database
/// via the <c>GetInvitedClubsEmailConfigPerJob</c> stored procedure.
/// Used for both COA missing-feed notifications and image-failure notifications.
/// </summary>
public class EmailConfig
{
    // -----------------------------------------------------------------------
    // SMTP connection settings
    // -----------------------------------------------------------------------

    public string SMTPServer { get; set; } = string.Empty;
    public int SMTPServerPort { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string EmailFrom { get; set; } = string.Empty;
    public string EmailFromUser { get; set; } = string.Empty;
    public bool SMTPUseSSL { get; set; }

    // -----------------------------------------------------------------------
    // Recipient strings (semicolon-delimited, as stored in the database)
    // -----------------------------------------------------------------------

    /// <summary>Semicolon-delimited TO recipients for COA missing-feed emails.</summary>
    public string EmailTo { get; set; } = string.Empty;

    /// <summary>Semicolon-delimited CC recipients.</summary>
    public string EmailCC { get; set; } = string.Empty;

    /// <summary>Semicolon-delimited BCC recipients.</summary>
    public string EmailBCC { get; set; } = string.Empty;

    // -----------------------------------------------------------------------
    // Parsed recipient arrays (populated at runtime by splitting the strings above)
    // -----------------------------------------------------------------------

    public string[] EmailToArr { get; set; } = Array.Empty<string>();
    public string[] EmailCCArr { get; set; } = Array.Empty<string>();
    public string[] EmailBCCArr { get; set; } = Array.Empty<string>();

    // -----------------------------------------------------------------------
    // COA missing-feed email content
    // -----------------------------------------------------------------------

    public string EmailSubject { get; set; } = string.Empty;
    public string EmailTemplate { get; set; } = string.Empty;

    // -----------------------------------------------------------------------
    // Image-failure email content
    // Sent to EmailToHelpDesk (split by ';') when images cannot be retrieved.
    // Only sent when EmailTemplateImageFail is not null/whitespace
    // AND EmailToHelpDesk has non-zero length.
    // -----------------------------------------------------------------------

    /// <summary>Semicolon-delimited helpdesk recipients for image-failure emails.</summary>
    public string EmailToHelpDesk { get; set; } = string.Empty;

    /// <summary>Subject line for image-failure notification emails.</summary>
    public string EmailSubjectImageFail { get; set; } = string.Empty;

    /// <summary>
    /// HTML template path for image-failure emails.
    /// Contains the <c>#MissingImagesTable#</c> placeholder replaced at runtime
    /// with the HTML table generated from failed image records.
    /// </summary>
    public string EmailTemplateImageFail { get; set; } = string.Empty;
}
