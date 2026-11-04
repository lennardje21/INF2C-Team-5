using CargoHub.Api.Data;
using CargoHub.Api.Models;
using MySqlConnector;

namespace CargoHub.Api.Services;

/// Business logic for item groups.
public sealed class ItemGroupsService(
    MySqlConnectionFactory factory,
    MySqlRepository<ItemGroup> itemGroups)
{
    /// Returns all item groups.
    public Task<IReadOnlyList<ItemGroup>> GetAllAsync(CancellationToken cancellationToken = default) =>
        itemGroups.GetAsync(cancellationToken: cancellationToken);

    /// Returns one item group, or null if it does not exist.
    public async Task<ItemGroup?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var result = await itemGroups.GetAsync(
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken);
        return result.SingleOrDefault();
    }

    /// Creates an item group; the id and timestamps are set here.
    public async Task<ItemGroup> CreateAsync(
        ItemGroup itemGroup, CancellationToken cancellationToken = default)
    {
        Validate(itemGroup);
        var now = DateTime.UtcNow;
        itemGroup.Id = 0;
        itemGroup.CreatedAt = now;
        itemGroup.UpdatedAt = now;

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await itemGroups.InsertAsync(itemGroup, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return itemGroup;
    }

    /// Updates an item group; returns false if it does not exist.
    public async Task<bool> UpdateAsync(
        int id, ItemGroup itemGroup, CancellationToken cancellationToken = default)
    {
        Validate(itemGroup);

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LockAsync(connection, transaction, id, cancellationToken);
        if (existing is null)
            return false;

        itemGroup.Id = id;
        itemGroup.CreatedAt = existing.CreatedAt;
        itemGroup.UpdatedAt = DateTime.UtcNow;
        await itemGroups.UpdateAsync(itemGroup, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// Deletes an item group; returns false if missing, or throws if items reference it.
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var itemGroup = await LockAsync(connection, transaction, id, cancellationToken);
        if (itemGroup is null)
            return false;

        try
        {
            await itemGroups.DeleteAsync(itemGroup, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.RowIsReferenced2)
        {
            throw new ArgumentException(
                "The item group cannot be deleted because items reference it.", e);
        }

        return true;
    }

    private async Task<ItemGroup?> LockAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        int id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT `id` FROM `item_group` WHERE `id` = @id FOR UPDATE";
        command.Parameters.AddWithValue("@id", id);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
            return null;

        var result = await itemGroups.GetAsync(connection, transaction,
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken);
        return result.SingleOrDefault();
    }

    private static void Validate(ItemGroup itemGroup)
    {
        if (string.IsNullOrWhiteSpace(itemGroup.Name))
            throw new ArgumentException("Missing required field: Name.");
        if (itemGroup.Name.Length > 255)
            throw new ArgumentException("Name must not exceed 255 characters.");
        if (string.IsNullOrWhiteSpace(itemGroup.Description))
            throw new ArgumentException("Missing required field: Description.");
    }
}