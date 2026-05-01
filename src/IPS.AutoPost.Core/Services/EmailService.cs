using System.Net;
using System.Net.Mail;
using IPS.AutoPost.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IPS.AutoPost.Core.Services;

/// <summary>
/// Sends HTML emails via SMTP using <see cref="System.Net.Mail.SmtpClient"/>.
/// Supports optional file attachments, CC/BCC recipients, and SMTP authentication.
/// </summary>
public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sends an HTML email via SMTP.
    /// </summary>
    /// <param name="smtpServer">Hostname or IP of the SMTP server.</param>
    /// <param name="smtpPort">Port number of the SMTP server.</param>
    /// <param name="fromAddress">Sender email address.</param>
    /// <param name="fromDisplayName">Display name shown for the sender.</param>
    /// <param name="toAddresses">Primary recipient addresses. Null/empty entries are skipped.</param>
    /// <param name="ccAddresses">CC recipient addresses. Null/empty entries are skipped.</param>
    /// <param name="bccAddresses">BCC recipient addresses. Null/empty entries are skipped.</param>
    /// <param name="subject">Email subject line.</param>
    /// <param name="htmlBody">HTML body content.</param>
    /// <param name="useSsl">Whether to use SSL/TLS for the SMTP connection. Defaults to <c>false</c>.</param>
    /// <param name="smtpUsername">Optional SMTP authentication username.</param>
    /// <param name="smtpPassword">Optional SMTP authentication password.</param>
    /// <param name="attachmentFilePath">Optional path to a file to attach. Ignored if the file does not exist.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SendAsync(
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
        CancellationToken ct = default)
    {
        Attachment? attachment = null;

        try
        {
            using var message = new MailMessage
            {
                From       = new MailAddress(fromAddress, fromDisplayName),
                Subject    = subject,
                Body       = htmlBody,
                IsBodyHtml = true
            };

            foreach (var address in toAddresses)
            {
                if (!string.IsNullOrWhiteSpace(address))
                    message.To.Add(address);
            }

            foreach (var address in ccAddresses)
            {
                if (!string.IsNullOrWhiteSpace(address))
                    message.CC.Add(address);
            }

            foreach (var address in bccAddresses)
            {
                if (!string.IsNullOrWhiteSpace(address))
                    message.Bcc.Add(address);
            }

            if (!string.IsNullOrWhiteSpace(attachmentFilePath) && File.Exists(attachmentFilePath))
            {
                attachment = new Attachment(attachmentFilePath);
                message.Attachments.Add(attachment);
            }

            using var smtp = new SmtpClient(smtpServer, smtpPort)
            {
                EnableSsl = useSsl
            };

            if (!string.IsNullOrWhiteSpace(smtpUsername))
            {
                smtp.Credentials = new NetworkCredential(smtpUsername, smtpPassword);
            }

            await smtp.SendMailAsync(message, ct);
        }
        catch (Exception ex)
        {
            var recipientCount = toAddresses.Count(a => !string.IsNullOrWhiteSpace(a));
            _logger.LogError(ex,
                "Failed to send email. Subject: {Subject}, Recipient count: {RecipientCount}",
                subject,
                recipientCount);
            throw;
        }
        finally
        {
            attachment?.Dispose();
        }
    }
}
