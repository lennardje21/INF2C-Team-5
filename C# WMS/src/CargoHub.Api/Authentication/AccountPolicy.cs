using CargoHub.Api.Models;

namespace CargoHub.Api.Authentication;

/// <summary>Which accounts an account may see and manage, following AccountRoles.</summary>
public static class AccountPolicy
{
    // A strictly higher role, in the same warehouse; the superroot in every warehouse.
    public static bool CanManage(ApiUser caller, Account target) =>
        AccountRoles.IsAbove(caller.Role, target.Role) &&
        (caller.Role == AccountRoles.Superroot || target.WarehouseId == caller.WarehouseId);

    // Your own account, and every account you manage. Others are reported as not found.
    public static bool CanView(ApiUser caller, Account target) =>
        target.Id == caller.AccountId || CanManage(caller, target);
}
