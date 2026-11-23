namespace CargoHub.Api.Models;

// The API key hash is deliberately not a property: it never leaves the database.
public class Account : BaseModel
{
    // superroot, root, admin or user; see Authentication.AccountRoles.
    public string Role { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    // Empty only for the superroot.
    public int? WarehouseId { get; set; }

    // The application that uses the account's API key, e.g. "handheld_scanner".
    public string App { get; set; } = string.Empty;

    // Resource (the path segment after /api/v2, e.g. "item_lines") -> allowed methods.
    // When creating an account, leave it out for the default of the role; when updating,
    // leave it out to keep the current access.
    public Dictionary<string, EndpointPermissions>? EndpointAccess { get; set; }
}

public sealed class EndpointPermissions
{
    public bool Get { get; set; }

    public bool Post { get; set; }

    public bool Put { get; set; }

    public bool Delete { get; set; }
}

// The response to creating an account: the only time its API key is shown.
public sealed class CreatedAccount : Account
{
    public string ApiKey { get; set; } = string.Empty;
}

// The response to issuing a new API key: the only time it is shown.
public sealed class IssuedApiKey
{
    public int AccountId { get; set; }

    public string ApiKey { get; set; } = string.Empty;
}
