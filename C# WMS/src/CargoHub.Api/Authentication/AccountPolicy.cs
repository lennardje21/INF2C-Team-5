using CargoHub.Api.Models;

namespace CargoHub.Api.Authentication;

/// <summary>
/// Which accounts an account may see and manage, following AccountRoles. Nobody manages
/// their own account (only someone above them does), so nobody can raise their own role
/// or access, and nobody can manage the superroot.
/// </summary>
public static class AccountPolicy
{
    // A strictly higher role, in the same warehouse; the superroot in every warehouse.
    public static bool CanManage(ApiUser caller, Account target) =>
        AccountRoles.IsAbove(caller.Role, target.Role) &&
        (caller.Role == AccountRoles.Superroot || target.WarehouseId == caller.WarehouseId);

    // Your own account, and every account you manage. Others are reported as not found.
    public static bool CanView(ApiUser caller, Account target) =>
        target.Id == caller.AccountId || CanManage(caller, target);

    public static void CheckManage(ApiUser caller, Account target)
    {
        if (!CanManage(caller, target))
            throw new ForbiddenException(target.Id == caller.AccountId
                ? "You cannot change or delete your own account; an account above yours can."
                : "You can only manage accounts with a lower role than yours, in your own warehouse.");
    }

    // The new or changed account must also be one the caller manages: a lower role, and
    // the caller's warehouse unless the caller is the superroot.
    public static void CheckManageAs(ApiUser caller, Account account)
    {
        if (!AccountRoles.IsAbove(caller.Role, account.Role))
            throw new ForbiddenException($"You can only give accounts a role below yours ({caller.Role}).");

        if (caller.Role != AccountRoles.Superroot && account.WarehouseId != caller.WarehouseId)
            throw new ForbiddenException("You can only manage accounts in your own warehouse.");
    }

    // New keys: for accounts you manage, and for your own account (e.g. after a leak).
    public static void CheckIssueKey(ApiUser caller, Account target)
    {
        if (target.Id != caller.AccountId)
            CheckManage(caller, target);
    }

    // Nobody can give an account access their own account does not have.
    public static void CheckGrant(ApiUser caller, Dictionary<string, EndpointPermissions> access)
    {
        var notAllowed = Allowed(access)
            .Where(p => !caller.HasAccess(p.Resource, p.Method))
            .Select(p => $"{p.Method.ToUpperInvariant()} {p.Resource}")
            .ToList();

        if (notAllowed.Count > 0)
            throw new ForbiddenException(
                "You cannot grant access your own account does not have: " + string.Join(", ", notAllowed) + ".");
    }

    // The default access of a new account: everything for root and admin, reading for a
    // user; limited to what the caller may do itself.
    public static Dictionary<string, EndpointPermissions> DefaultAccess(ApiUser caller, string role) =>
        ApiResources.All.ToDictionary(resource => resource, resource => new EndpointPermissions
        {
            Get = caller.HasAccess(resource, "get"),
            Post = role != AccountRoles.User && caller.HasAccess(resource, "post"),
            Put = role != AccountRoles.User && caller.HasAccess(resource, "put"),
            Delete = role != AccountRoles.User && caller.HasAccess(resource, "delete")
        });

    public static IEnumerable<(string Resource, string Method)> Allowed(
        Dictionary<string, EndpointPermissions> access)
    {
        foreach (var (resource, permissions) in access)
        {
            if (permissions.Get) yield return (resource, "get");
            if (permissions.Post) yield return (resource, "post");
            if (permissions.Put) yield return (resource, "put");
            if (permissions.Delete) yield return (resource, "delete");
        }
    }
}
