namespace CargoHub.Api.Authentication;

/// <summary>The resources and methods an account can be given access to.</summary>
public static class ApiResources
{
    // The first path segment after /api/v2 of every controller except accounts, which is
    // governed by the account roles instead (see ApiKeyMiddleware).
    public static readonly IReadOnlyList<string> All =
    [
        "warehouses", "locations", "transfers", "items", "item_lines", "item_groups", "item_types",
        "suppliers", "orders", "clients", "shipments", "inventories"
    ];

    public static readonly IReadOnlyList<string> Methods = ["get", "post", "put", "delete"];
}
