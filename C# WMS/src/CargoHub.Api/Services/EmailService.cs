using System.Net;
using System.Net.Mail;
using CargoHub.Api.Settings;
using Microsoft.Extensions.Options;

namespace CargoHub.Api.Services;

// Sends mail over SMTP. The same code serves Resend in production and Mailpit locally;
// only the settings differ.
public sealed class EmailService(IOptions<EmailOptions> options, ILogger<EmailService> logger)
    : IEmailService
{
    private readonly EmailOptions settings = options.Value;

    public async Task<bool> SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            using var message = new MailMessage(settings.From, to, subject, body);
            using var client = new SmtpClient(settings.Host, settings.Port)
            {
                EnableSsl = settings.EnableSsl,
                Timeout = 10_000,
            };

            if (!string.IsNullOrEmpty(settings.Username))
            {
                client.Credentials = new NetworkCredential(settings.Username, settings.Password);
            }

            await client.SendMailAsync(message, cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            // The address is left out of the log because it is personal data.
            logger.LogError(exception, "Could not send email with subject '{Subject}'.", subject);
            return false;
        }
    }
}
