namespace CargoHub.Api.Authentication;

/// <summary>
/// The account roles, in a strict hierarchy: an account only manages accounts with a lower
/// role. The superroot is above everyone in every warehouse; the other roles only within
/// their own warehouse. The floormanager receives the blockade mails of its warehouse.
/// </summary>
public static class AccountRoles
{
    public const string Superroot = "superroot";
    public const string Root = "root";
    public const string Admin = "admin";
    public const string Floormanager = "floormanager";
    public const string User = "user";

    // Highest first. A new role only needs to be added here and to the CHECK on account.role.
    public static readonly IReadOnlyList<string> Hierarchy = [Superroot, Root, Admin, Floormanager, User];

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
