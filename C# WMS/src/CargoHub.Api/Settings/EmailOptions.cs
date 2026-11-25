using System.ComponentModel.DataAnnotations;

namespace CargoHub.Api.Settings;

// SMTP settings for EmailService, from the "Email" section in appsettings.json. Override
// them with environment variables such as Email__Host. Locally and in CI they point to
// Mailpit, in production to Resend (smtp.resend.com, user "resend", the API key as password).
public class EmailOptions
{
    public const string SectionName = "Email";

    [Required]
    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 1025;

    // Starts TLS on the connection (STARTTLS, port 587). Mailpit needs this off.
    public bool EnableSsl { get; set; } = false;

    // Empty means no login, which is what Mailpit expects.
    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    // The sender address. For Resend this must be on a domain verified in Resend.
    [Required]
    [EmailAddress]
    public string From { get; set; } = "cargohub@localhost";
}
