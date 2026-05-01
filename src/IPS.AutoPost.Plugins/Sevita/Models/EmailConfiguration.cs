namespace IPS.AutoPost.Plugins.Sevita.Models;

/// <summary>
/// General email configuration for the Sevita plugin, loaded from the
/// <c>get_sevita_configurations</c> stored procedure result.
/// </summary>
public class EmailConfiguration
{
    /// <summary>SMTP server hostname.</summary>
    public string SmtpServer { get; set; } = string.Empty;

    /// <summary>SMTP server port number.</summary>
    public int SmtpServerPort { get; set; }

    /// <summary>SMTP authentication username.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>SMTP authentication password.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Sender email address.</summary>
    public string EmailFrom { get; set; } = string.Empty;

    /// <summary>Sender display name.</summary>
    public string EmailFromUser { get; set; } = string.Empty;

    /// <summary>Whether to use SSL/TLS for the SMTP connection.</summary>
    public bool SmtpUseSsl { get; set; }
}
