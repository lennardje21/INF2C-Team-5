namespace CargoHub.Api.Services;

public interface IEmailService
{
    // Sends one plain-text mail. Never throws: a failed send is logged and returns false,
    // so a mail problem cannot crash the API or a background job.
    Task<bool> SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}
