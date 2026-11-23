namespace CargoHub.Api.Models;

// The response to issuing a new API key: the only time it is shown.
public sealed class IssuedApiKey
{
    public int AccountId { get; set; }

    public string ApiKey { get; set; } = string.Empty;
}
