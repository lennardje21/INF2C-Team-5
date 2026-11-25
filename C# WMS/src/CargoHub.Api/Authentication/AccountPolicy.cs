using CargoHub.Api.Models;

namespace CargoHub.Api.Authentication;

/// <summary>
/// Which accounts an account may see and manage, following AccountRoles: it manages only the
/// roles directly below it, and if it manages any, it also sees every account below it; both
/// within its own warehouse, or in every warehouse for the superroot. Every account sees
/// itself. Nobody manages their own account,
/// so nobody can raise their own role or access. Anything else is refused with a
/// ForbiddenException (403).
/// </summary>
public static class AccountPolicy
{
    public static bool InScope(ApiUser caller, Account target) =>
        caller.Role == AccountRoles.Superroot || target.WarehouseId == caller.WarehouseId;

    public static bool CanView(ApiUser caller, Account target) =>
        target.Id == caller.AccountId ||
        (SeesBelow(caller, target) && InScope(caller, target));

    public static bool CanManage(ApiUser caller, Account target) =>
        target.Id != caller.AccountId &&
        AccountRoles.CanManageRole(caller.Role, target.Role) && InScope(caller, target);

    public static void CheckView(ApiUser caller, Account target)
    {
        if (CanView(caller, target))
            return;

        throw new ForbiddenException(SeesBelow(caller, target)
            ? "This account belongs to another warehouse."
            : AccountRoles.Manages.ContainsKey(caller.Role)
                ? "You can only see your own account and the accounts below yours."
                : "You can only see your own account.");
    }

    public static void CheckManage(ApiUser caller, Account target)
    {
        if (CanManage(caller, target))
            return;

        if (target.Id == caller.AccountId)
            throw new ForbiddenException("You cannot change or delete your own account; an account above yours can.");
        if (!AccountRoles.CanManageRole(caller.Role, target.Role))
            throw new ForbiddenException(RoleError(caller));
        throw new ForbiddenException("This account belongs to another warehouse.");
    }

    // The new or changed account must also be one the caller manages: a role it manages,
    // in its own warehouse unless the caller is the superroot.
    public static void CheckManageAs(ApiUser caller, Account account)
    {
        if (!AccountRoles.CanManageRole(caller.Role, account.Role))
            throw new ForbiddenException(RoleError(caller));

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
    // floormanager or user; limited to what the caller may do itself.
    public static Dictionary<string, EndpointPermissions> DefaultAccess(ApiUser caller, string role)
    {
        var write = role is AccountRoles.Root or AccountRoles.Admin;
        return ApiResources.All.ToDictionary(resource => resource, resource => new EndpointPermissions
        {
            Get = caller.HasAccess(resource, "get"),
            Post = write && caller.HasAccess(resource, "post"),
            Put = write && caller.HasAccess(resource, "put"),
            Delete = write && caller.HasAccess(resource, "delete")
        });
    }

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

    // Only roles that manage accounts see the accounts below them.
    private static bool SeesBelow(ApiUser caller, Account target) =>
        AccountRoles.Manages.ContainsKey(caller.Role) && AccountRoles.IsAbove(caller.Role, target.Role);

    private static string RoleError(ApiUser caller) =>
        AccountRoles.Manages.TryGetValue(caller.Role, out var roles)
            ? $"A {caller.Role} can only manage accounts with role {string.Join(" or ", roles)}."
            : $"A {caller.Role} cannot manage accounts.";
}
