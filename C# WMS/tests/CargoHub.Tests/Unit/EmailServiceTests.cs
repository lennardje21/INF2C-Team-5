using CargoHub.Api.Services;
using CargoHub.Api.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CargoHub.Tests.Unit;

public sealed class EmailServiceTests
{
    private static EmailService CreateService(EmailOptions options) =>
        new(Options.Create(options), NullLogger<EmailService>.Instance);

    [Fact]
    public async Task SendAsync_ReturnsFalseInsteadOfThrowing_WhenTheMailServerIsUnreachable()
    {
        // Nothing listens on port 1, so the connection is refused.
        var service = CreateService(new EmailOptions { Host = "localhost", Port = 1 });

        var sent = await service.SendAsync("floor@example.com", "Blocked order", "Order 1 is blocked.");

        Assert.False(sent);
    }

    [Fact]
    public async Task SendAsync_ReturnsFalseInsteadOfThrowing_WhenTheAddressIsInvalid()
    {
        var service = CreateService(new EmailOptions());

        var sent = await service.SendAsync("not-an-address", "Blocked order", "Order 1 is blocked.");

        Assert.False(sent);
    }
}
