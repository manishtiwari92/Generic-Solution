namespace IPS.AutoPost.Core.Interfaces;

/// <summary>
/// Abstraction over SMTP email sending.
/// Allows unit tests to mock email delivery without a real SMTP server.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an HTML email via SMTP.
    /// </summary>
    Task SendAsync(
        string smtpServer,
        int smtpPort,
        string fromAddress,
        string fromDisplayName,
        string[] toAddresses,
        string[] ccAddresses,
        string[] bccAddresses,
        string subject,
        string htmlBody,
        bool useSsl = false,
        string? smtpUsername = null,
        string? smtpPassword = null,
        string? attachmentFilePath = null,
        CancellationToken ct = default);
}
