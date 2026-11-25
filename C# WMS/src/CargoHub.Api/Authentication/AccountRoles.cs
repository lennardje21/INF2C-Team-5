namespace CargoHub.Api.Authentication;

/// <summary>
/// The account roles, from high to low. Each role manages (creates, changes and deletes)
/// only the role or roles directly below it, and sees every account below it; the superroot
/// in every warehouse, the others within their own warehouse.
/// </summary>
public static class AccountRoles
{
    public const string Superroot = "superroot";
    public const string Root = "root";
    public const string Admin = "admin";
    public const string Floormanager = "floormanager";
    public const string User = "user";

    // Highest first. A new role also needs to be added to Manages and to the CHECK
    // constraint account_role (with a migration).
    public static readonly IReadOnlyList<string> Hierarchy = [Superroot, Root, Admin, Floormanager, User];

    // The roles each role manages. Roles that are not listed manage nothing.
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Manages =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [Superroot] = [Root],
            [Root] = [Admin],
            [Admin] = [Floormanager, User]
        };

    public static bool CanManageRole(string role, string target) =>
        Manages.TryGetValue(role, out var roles) && roles.Contains(target);

    // Whether role is strictly higher than other; unknown roles are never higher or lower.
    public static bool IsAbove(string role, string other)
    {
        var rank = IndexOf(role);
        var otherRank = IndexOf(other);
        return rank >= 0 && otherRank >= 0 && rank < otherRank;
    }

    private static int IndexOf(string role)
    {
        for (var i = 0; i < Hierarchy.Count; i++)
        {
            if (Hierarchy[i] == role)
                return i;
        }
        return -1;
    }
}