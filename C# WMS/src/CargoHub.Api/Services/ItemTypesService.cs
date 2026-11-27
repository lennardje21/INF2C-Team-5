using System.Text;
using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Validation;
using MySqlConnector;

namespace CargoHub.Api.Services;

/// Business logic for item types.
public sealed class ItemTypesService(
    MySqlConnectionFactory factory,
    MySqlRepository<ItemType> itemTypes,
    MySqlRepository<Item> items)
{
    private const int MaxDescriptionBytes = 65_535;

    /// Returns one page of item types.
    public Task<PagedResult<ItemType>> GetPageAsync(
        PageRequest page, CancellationToken cancellationToken = default) =>
        itemTypes.GetAsync(page, cancellationToken: cancellationToken);

    /// Returns one item type, or null if it does not exist.
    public async Task<ItemType?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var result = await itemTypes.GetAsync(
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken);
        return result.SingleOrDefault();
    }

    /// Returns the IDs of items of an item type.
    public async Task<IReadOnlyList<int>> GetItemsAsync(
        int id, CancellationToken cancellationToken = default)
    {
        var result = await items.GetAsync(
            new Dictionary<string, object?> { ["item_type_id"] = id }, cancellationToken);
        return result.Select(item => item.Id).ToArray();
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
        itemType.Name = FieldRules.Trim(itemType.Name);
        itemType.Description = FieldRules.Trim(itemType.Description);

        FieldRules.CheckRequired(
            (nameof(ItemType.Name), itemType.Name),
            (nameof(ItemType.Description), itemType.Description));

        if (itemType.Name.EnumerateRunes().Count() > FieldRules.MaxTextLength)
            throw new ArgumentException("Name must not exceed 255 characters.");

        FieldRules.CheckNoControlCharacters((nameof(ItemType.Name), itemType.Name));

        if (itemType.Description.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new ArgumentException(
                "Description must not contain control characters other than tabs or line breaks.");
        if (Encoding.UTF8.GetByteCount(itemType.Description) > MaxDescriptionBytes)
            throw new ArgumentException($"Description must not exceed {MaxDescriptionBytes} UTF-8 bytes.");
    }
}
