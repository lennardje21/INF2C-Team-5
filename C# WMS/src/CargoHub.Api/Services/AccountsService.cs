using CargoHub.Api.Authentication;
using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Validation;
using MySqlConnector;

namespace CargoHub.Api.Services;

/// <summary>
/// Accounts as the calling account may see and manage them (see AccountPolicy): accounts
/// it may not see behave as if they do not exist (null/false), operations it may not
/// perform throw ForbiddenException.
/// </summary>
public sealed class AccountsService(
    MySqlConnectionFactory factory,
    MySqlRepository<Account> accounts)
{
    // Get every account the caller may see
    public async Task<IReadOnlyList<Account>> GetAllAsync(
        ApiUser caller, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);

        // Only the superroot sees other warehouses, so the others only need their own.
        var result = (await accounts.GetAsync(
                connection,
                null,
                caller.Role == AccountRoles.Superroot
                    ? null
                    : new Dictionary<string, object?> { ["warehouse_id"] = caller.WarehouseId },
                cancellationToken))
            .Where(account => AccountPolicy.CanView(caller, account))
            .ToList();

        await LoadAccessAsync(connection, null, result, cancellationToken);

        return result;
    }

    // Get account by id, or null when it does not exist or the caller may not see it
    public async Task<Account?> GetByIdAsync(
        ApiUser caller, int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);

        var result = await accounts.GetAsync(
            connection,
            null,
            new Dictionary<string, object?> { ["id"] = id },
            cancellationToken);

        var account = result.SingleOrDefault();

        if (account is null || !AccountPolicy.CanView(caller, account))
        {
            return null;
        }

        await LoadAccessAsync(connection, null, [account], cancellationToken);

        return account;
    }

    // Create an account below the caller, with a new API key
    public async Task<CreatedAccount> CreateAsync(
        ApiUser caller, Account input, CancellationToken cancellationToken = default)
    {
        var account = new CreatedAccount
        {
            Role = input.Role,
            Name = input.Name,
            Email = input.Email,
            WarehouseId = input.WarehouseId,
            App = input.App,
            EndpointAccess = input.EndpointAccess
        };

        // Accounts below the superroot are always in the caller's warehouse, so that is the
        // default. Not for a superroot, which has no warehouse: that is refused as a role
        // the caller may not give (403), not as a validation error.
        if (caller.Role != AccountRoles.Superroot &&
            FieldRules.Trim(account.Role).ToLowerInvariant() != AccountRoles.Superroot)
            account.WarehouseId ??= caller.WarehouseId;

        ValidateAccount(account);
        AccountPolicy.CheckManageAs(caller, account);

        if (account.EndpointAccess is null)
            account.EndpointAccess = AccountPolicy.DefaultAccess(caller, account.Role);
        else
            AccountPolicy.CheckGrant(caller, account.EndpointAccess);

        var now = Now();
        account.CreatedAt = now;
        account.UpdatedAt = now;
        account.ApiKey = ApiUsersService.GenerateKey();

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await CheckWarehouseExistsAsync(connection, transaction, account.WarehouseId, cancellationToken);

        try
        {
            await accounts.InsertAsync(account, connection, transaction, cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new ArgumentException("An account with this Email already exists.", e);
        }

        await SetKeyHashAsync(connection, transaction, account.Id, account.ApiKey, cancellationToken);
        await ReplaceAccessAsync(connection, transaction, account.Id, account.EndpointAccess, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return account;
    }

    // Update an account the caller manages
    public async Task<bool> UpdateAsync(
        ApiUser caller, int id, Account account, CancellationToken cancellationToken = default)
    {
        ValidateAccount(account);

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var existing = await LockAsync(connection, transaction, id, cancellationToken);

        if (existing is null || !AccountPolicy.CanView(caller, existing))
        {
            return false;
        }

        AccountPolicy.CheckManage(caller, existing);
        AccountPolicy.CheckManageAs(caller, account);

        if (account.EndpointAccess is not null)
            AccountPolicy.CheckGrant(caller, account.EndpointAccess);

        await CheckWarehouseExistsAsync(connection, transaction, account.WarehouseId, cancellationToken);

        account.Id = id;
        account.CreatedAt = existing.CreatedAt;
        account.UpdatedAt = Now();

        try
        {
            await accounts.UpdateAsync(account, connection, transaction, cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new ArgumentException("An account with this Email already exists.", e);
        }

        if (account.EndpointAccess is not null)
            await ReplaceAccessAsync(connection, transaction, id, account.EndpointAccess, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    // Delete an account the caller manages
    public async Task<bool> DeleteAsync(
        ApiUser caller, int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var existing = await LockAsync(connection, transaction, id, cancellationToken);

        if (existing is null || !AccountPolicy.CanView(caller, existing))
        {
            return false;
        }

        AccountPolicy.CheckManage(caller, existing);

        // Its endpoint access is deleted with it (ON DELETE CASCADE).
        await accounts.DeleteAsync(existing, connection, transaction, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    // Give an account a new API key; its previous key stops working.
    // Null when the account does not exist or the caller may not see it.
    public async Task<IssuedApiKey?> IssueApiKeyAsync(
        ApiUser caller, int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var account = await LockAsync(connection, transaction, id, cancellationToken);

        if (account is null || !AccountPolicy.CanView(caller, account))
        {
            return null;
        }

        AccountPolicy.CheckIssueKey(caller, account);

        var key = ApiUsersService.GenerateKey();
        await SetKeyHashAsync(connection, transaction, id, key, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new IssuedApiKey { AccountId = id, ApiKey = key };
    }

    private static async Task SetKeyHashAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        int id,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE `account` SET `api_key_hash` = @hash, `updated_at` = @now WHERE `id` = @id";
        command.Parameters.AddWithValue("@hash", ApiUsersService.Hash(key));
        command.Parameters.AddWithValue("@now", Now());
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Stores every resource with all four methods, allowed or not, like the seed does.
    private static async Task ReplaceAccessAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        int id,
        Dictionary<string, EndpointPermissions> access,
        CancellationToken cancellationToken)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM `account_endpoint` WHERE `account_id` = @id";
            delete.Parameters.AddWithValue("@id", id);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        var resources = access.Keys.ToArray();
        if (resources.Length == 0)
            return;

        await using (var endpoints = connection.CreateCommand())
        {
            endpoints.Transaction = transaction;
            endpoints.CommandText = "INSERT INTO `account_endpoint` (`account_id`, `resource`) VALUES " +
                string.Join(", ", resources.Select((_, i) => $"(@id, @r{i})"));
            endpoints.Parameters.AddWithValue("@id", id);
            for (var i = 0; i < resources.Length; i++)
                endpoints.Parameters.AddWithValue($"@r{i}", resources[i]);
            await endpoints.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var permissions = connection.CreateCommand())
        {
            permissions.Transaction = transaction;
            var rows = new List<string>();
            permissions.Parameters.AddWithValue("@id", id);
            for (var i = 0; i < resources.Length; i++)
            {
                var allowed = access[resources[i]];
                permissions.Parameters.AddWithValue($"@r{i}", resources[i]);
                foreach (var (method, value) in new[]
                         {
                             ("get", allowed.Get), ("post", allowed.Post), ("put", allowed.Put),
                             ("delete", allowed.Delete)
                         })
                {
                    rows.Add($"(@id, @r{i}, '{method}', {(value ? 1 : 0)})");
                }
            }
            permissions.CommandText =
                "INSERT INTO `account_permission` (`account_id`, `resource`, `method`, `allowed`) VALUES " +
                string.Join(", ", rows);
            await permissions.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    // Fills in EndpointAccess for the given accounts, in one query.
    private static async Task LoadAccessAsync(
        MySqlConnection connection,
        MySqlTransaction? transaction,
        IReadOnlyList<Account> list,
        CancellationToken cancellationToken)
    {
        foreach (var account in list)
            account.EndpointAccess = [];

        if (list.Count == 0)
            return;

        var byId = list.ToDictionary(a => a.Id);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT e.`account_id`, e.`resource`, p.`method`, p.`allowed` FROM `account_endpoint` e " +
            "LEFT JOIN `account_permission` p ON p.`account_id` = e.`account_id` AND p.`resource` = e.`resource` " +
            "WHERE e.`account_id` IN (" + string.Join(", ", list.Select((_, i) => $"@a{i}")) + ") " +
            "ORDER BY e.`account_id`, e.`resource`";
        for (var i = 0; i < list.Count; i++)
            command.Parameters.AddWithValue($"@a{i}", list[i].Id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var access = byId[reader.GetInt32(0)].EndpointAccess!;
            var resource = reader.GetString(1);
            if (!access.TryGetValue(resource, out var permissions))
                access[resource] = permissions = new EndpointPermissions();

            if (reader.IsDBNull(2) || !reader.GetBoolean(3))
                continue;

            switch (reader.GetString(2).ToLowerInvariant())
            {
                case "get": permissions.Get = true; break;
                case "post": permissions.Post = true; break;
                case "put": permissions.Put = true; break;
                case "delete": permissions.Delete = true; break;
            }
        }
    }

    // Lock an account record for update to prevent concurrent modifications
    private async Task<Account?> LockAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        int id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT `id` FROM `account` WHERE `id` = @id FOR UPDATE";
        command.Parameters.AddWithValue("@id", id);

        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            return null;
        }

        var result = await accounts.GetAsync(
            connection,
            transaction,
            new Dictionary<string, object?> { ["id"] = id },
            cancellationToken);

        return result.SingleOrDefault();
    }

    private static async Task CheckWarehouseExistsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        if (warehouseId is null)
            return;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM `warehouse` WHERE `id` = @id FOR SHARE";
        command.Parameters.AddWithValue("@id", warehouseId);

        if (await command.ExecuteScalarAsync(cancellationToken) is null)
            throw new ArgumentException($"WarehouseId {warehouseId} does not exist.");
    }

    // DATETIME(6) stores microseconds, so return the value exactly as stored.
    private static DateTime Now()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);
    }

    // Trims the text fields, then checks that they are filled in, fit in the database and have a valid format.
    private static void ValidateAccount(Account account)
    {
        account.Name = FieldRules.Trim(account.Name);
        account.Email = FieldRules.Trim(account.Email);
        account.Role = FieldRules.Trim(account.Role).ToLowerInvariant();
        account.App = FieldRules.Trim(account.App);

        var fields = new (string Name, string Value)[]
        {
            (nameof(Account.Role), account.Role),
            (nameof(Account.Name), account.Name),
            (nameof(Account.Email), account.Email),
            (nameof(Account.App), account.App)
        };

        FieldRules.CheckRequired(fields);
        FieldRules.CheckMaxLength(fields);
        FieldRules.CheckNoControlCharacters(fields);

        var errors = new List<string>();

        if (!account.Name.Any(char.IsLetterOrDigit))
            errors.Add("Name must contain at least one letter or digit.");

        if (!FieldRules.IsValidEmail(account.Email))
            errors.Add($"Email {FieldRules.EmailError}");

        if (!AccountRoles.Hierarchy.Contains(account.Role))
            errors.Add($"Role must be one of: {string.Join(", ", AccountRoles.Hierarchy)}.");
        else if (account.Role == AccountRoles.Superroot && account.WarehouseId is not null)
            errors.Add("WarehouseId must be empty for the superroot, which is not bound to a warehouse.");
        else if (account.Role != AccountRoles.Superroot && account.WarehouseId is null)
            errors.Add("WarehouseId is required for every account except the superroot.");

        foreach (var (resource, permissions) in account.EndpointAccess ?? [])
        {
            if (!ApiResources.All.Contains(resource))
                errors.Add($"EndpointAccess contains unknown resource '{resource}'. " +
                           $"Use: {string.Join(", ", ApiResources.All)}.");
            else if (permissions is null)
                errors.Add($"EndpointAccess.{resource} must be an object with get, post, put and delete.");
        }

        FieldRules.ThrowIfAny(errors);
    }
}
