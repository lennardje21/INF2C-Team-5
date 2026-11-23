namespace CargoHub.Api.Models;

// The response to creating an account: the only time its API key is shown.
public sealed class CreatedAccount : Account
{
    public string ApiKey { get; set; } = string.Empty;
}
