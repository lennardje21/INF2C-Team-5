using CargoHub.Api.Authentication;
using CargoHub.Api.Data;
using CargoHub.Api.Models;

namespace CargoHub.Api.Services;

/// <summary>
/// Accounts as the calling account may see them (see AccountPolicy): accounts it may not
/// see behave as if they do not exist.
/// </summary>
public sealed class AccountsService(MySqlRepository<Account> accounts)
{
    // Get every account the caller may see
    public async Task<IReadOnlyList<Account>> GetAllAsync(
        ApiUser caller, CancellationToken cancellationToken = default)
    {
        // Only the superroot sees other warehouses, so the others only need their own.
        var result = await accounts.GetAsync(
            caller.Role == AccountRoles.Superroot
                ? null
                : new Dictionary<string, object?> { ["warehouse_id"] = caller.WarehouseId },
            cancellationToken);

        return result.Where(account => AccountPolicy.CanView(caller, account)).ToList();
    }

    // Get account by id, or null when it does not exist or the caller may not see it
    public async Task<Account?> GetByIdAsync(
        ApiUser caller, int id, CancellationToken cancellationToken = default)
    {
        var result = await accounts.GetAsync(
            new Dictionary<string, object?> { ["id"] = id },
            cancellationToken);

        var account = result.SingleOrDefault();

        return account is not null && AccountPolicy.CanView(caller, account) ? account : null;
    }
}
