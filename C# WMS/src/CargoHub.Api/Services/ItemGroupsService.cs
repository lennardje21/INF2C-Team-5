using System.Text;
using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Validation;
using MySqlConnector;

namespace CargoHub.Api.Services;

/// Business logic for item groups.
public sealed class ItemGroupsService(
    MySqlConnectionFactory factory,
    MySqlRepository<ItemGroup> itemGroups,
    MySqlRepository<Item> items)
{
    private const int MaxDescriptionBytes = 65_535;

    /// Returns one page of item groups.
    public Task<PagedResult<ItemGroup>> GetPageAsync(
        PageRequest page, CancellationToken cancellationToken = default) =>
        itemGroups.GetAsync(page, cancellationToken: cancellationToken);

    /// Returns one item group, or null if it does not exist.
    public async Task<ItemGroup?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var result = await itemGroups.GetAsync(
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken);
        return result.SingleOrDefault();
    }

    /// Returns the IDs of items in an item group.
    public async Task<IReadOnlyList<int>> GetItemsAsync(
        int id, CancellationToken cancellationToken = default)
    {
        var result = await items.GetAsync(
            new Dictionary<string, object?> { ["item_group_id"] = id }, cancellationToken);
        return result.Select(item => item.Id).ToArray();
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
        itemGroup.Name = FieldRules.Trim(itemGroup.Name);
        itemGroup.Description = FieldRules.Trim(itemGroup.Description);

        FieldRules.CheckRequired(
            (nameof(ItemGroup.Name), itemGroup.Name),
            (nameof(ItemGroup.Description), itemGroup.Description));

        if (itemGroup.Name.EnumerateRunes().Count() > FieldRules.MaxTextLength)
            throw new ArgumentException("Name must not exceed 255 characters.");

        FieldRules.CheckNoControlCharacters((nameof(ItemGroup.Name), itemGroup.Name));

        if (itemGroup.Description.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new ArgumentException(
                "Description must not contain control characters other than tabs or line breaks.");
        if (Encoding.UTF8.GetByteCount(itemGroup.Description) > MaxDescriptionBytes)
            throw new ArgumentException($"Description must not exceed {MaxDescriptionBytes} UTF-8 bytes.");
    }
}