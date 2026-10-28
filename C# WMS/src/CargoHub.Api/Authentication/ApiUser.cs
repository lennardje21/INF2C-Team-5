namespace CargoHub.Api.Authentication;

public sealed record ApiUser(string ApiKey, string App, IReadOnlySet<(string Resource, string Method)> Allowed)
{
    // Resource is the first segment after /api/v1, method is lowercase (get/post/put/delete).
    public bool HasAccess(string resource, string method) => Allowed.Contains((resource, method));
}
