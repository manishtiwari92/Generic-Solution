namespace IPS.AutoPost.Plugins.Sevita.Models;

/// <summary>
/// Database error email notification configuration loaded from the
/// <c>get_sevita_configurations</c> stored procedure result.
/// </summary>
/// <remarks>
/// Used for sending email notifications when database errors occur during Sevita processing.
/// This is separate from the post failure notification email sent via
/// <c>FailedPostConfiguration</c> after a batch completes with failed workitems.
/// </remarks>
public class DBErrorEmailConfiguration
{
    /// <summary>
    /// Primary recipient email address for database error notifications.
    /// Maps to <c>get_sevita_configurations result: db_error_to_email_address</c>.
    /// </summary>
    public string ToEmailAddress { get; set; } = string.Empty;

    /// <summary>
    /// CC recipient email address for database error notifications.
    /// Maps to <c>get_sevita_configurations result: db_error_cc_email_address</c>.
    /// </summary>
    public string CcEmailAddress { get; set; } = string.Empty;

    /// <summary>
    /// Subject line for database error notification emails.
    /// Maps to <c>get_sevita_configurations result: db_error_email_subject</c>.
    /// </summary>
    public string EmailSubject { get; set; } = string.Empty;

    /// <summary>
    /// HTML email template body for database error notifications.
    /// Maps to <c>get_sevita_configurations result: db_error_email_template</c>.
    /// </summary>
    public string EmailTemplate { get; set; } = string.Empty;
}
