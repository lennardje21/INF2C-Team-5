namespace CargoHub.Api.Authentication;

/// <summary>The account whose API key was sent with the current request.</summary>
public sealed record ApiUser(
    int AccountId,
    string Role,
    int? WarehouseId,
    string App,
    IReadOnlySet<(string Resource, string Method)> Allowed)
{
    // Resource is the first segment after /api/v2, method is lowercase (get/post/put/delete).
    public bool HasAccess(string resource, string method) => Allowed.Contains((resource, method));
}
