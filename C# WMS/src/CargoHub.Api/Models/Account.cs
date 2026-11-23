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
}
