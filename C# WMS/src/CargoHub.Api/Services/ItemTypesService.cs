using CargoHub.Api.Data;
using CargoHub.Api.Models;
using MySqlConnector;

namespace CargoHub.Api.Services;

/// Business logic for item types.
public sealed class ItemTypesService(
    MySqlConnectionFactory factory,
    MySqlRepository<ItemType> itemTypes)
{
    /// Returns all item types.
    public Task<IReadOnlyList<ItemType>> GetAllAsync(CancellationToken cancellationToken = default) =>
        itemTypes.GetAsync(cancellationToken: cancellationToken);

    /// Returns one item type, or null if it does not exist.
    public async Task<ItemType?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var result = await itemTypes.GetAsync(
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken);
        return result.SingleOrDefault();
    }

    /// Creates an item type; the id and timestamps are set here.
    public async Task<ItemType> CreateAsync(
        ItemType itemType, CancellationToken cancellationToken = default)
    {
        Validate(itemType);
        var now = DateTime.UtcNow;
        itemType.Id = 0;
        itemType.CreatedAt = now;
        itemType.UpdatedAt = now;

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await itemTypes.InsertAsync(itemType, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return itemType;
    }

    /// Updates an item type; returns false if it does not exist.
    public async Task<bool> UpdateAsync(
        int id, ItemType itemType, CancellationToken cancellationToken = default)
    {
        Validate(itemType);

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LockAsync(connection, transaction, id, cancellationToken);
        if (existing is null)
            return false;

        itemType.Id = id;
        itemType.CreatedAt = existing.CreatedAt;
        itemType.UpdatedAt = DateTime.UtcNow;
        await itemTypes.UpdateAsync(itemType, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// Deletes an item type; returns false if missing, or throws if items reference it.
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var itemType = await LockAsync(connection, transaction, id, cancellationToken);
        if (itemType is null)
            return false;

        try
        {
            await itemTypes.DeleteAsync(itemType, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.RowIsReferenced2)
        {
            throw new ArgumentException(
                "The item type cannot be deleted because items reference it.", e);
        }

        return true;
    }

    private async Task<ItemType?> LockAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        int id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT `id` FROM `item_type` WHERE `id` = @id FOR UPDATE";
        command.Parameters.AddWithValue("@id", id);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
            return null;

        var result = await itemTypes.GetAsync(connection, transaction,
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken);
        return result.SingleOrDefault();
    }

    private static void Validate(ItemType itemType)
    {
        if (string.IsNullOrWhiteSpace(itemType.Name))
            throw new ArgumentException("Missing required field: Name.");
        if (itemType.Name.Length > 255)
            throw new ArgumentException("Name must not exceed 255 characters.");
        if (string.IsNullOrWhiteSpace(itemType.Description))
            throw new ArgumentException("Missing required field: Description.");
    }
}
