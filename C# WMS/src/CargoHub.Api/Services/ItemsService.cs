using CargoHub.Api.Data;
using CargoHub.Api.Models;
using MySqlConnector;

namespace CargoHub.Api.Services;

// Business logic for items and their inventory summaries.
public sealed class ItemsService(
    MySqlConnectionFactory factory,
    MySqlRepository<Item> items,
    MySqlRepository<Inventory> inventories)
{
    // Returns all items.
    public Task<IReadOnlyList<Item>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return items.GetAsync(cancellationToken: cancellationToken);
    }

    // Returns one item, or null if it does not exist.
    public async Task<Item?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var result = await items.GetAsync(
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken);
        return result.SingleOrDefault();
    }

    // Returns an item's inventory by location, or null if the item does not exist.
    public async Task<IReadOnlyList<Inventory>?> GetInventoryAsync(
        int id, CancellationToken cancellationToken = default)
    {
        if (await GetByIdAsync(id, cancellationToken) is null)
            return null;

        return await inventories.GetAsync(
            new Dictionary<string, object?> { ["item_id"] = id }, cancellationToken);
    }

    // Returns the summed inventory quantities, or null if the item does not exist.
    public async Task<ItemInventoryTotals?> GetInventoryTotalsAsync(
        int id, CancellationToken cancellationToken = default)
    {
        var itemInventory = await GetInventoryAsync(id, cancellationToken);
        if (itemInventory is null)
            return null;

        return new ItemInventoryTotals(
            itemInventory.Sum(inventory => (long)inventory.QuantityExpected),
            itemInventory.Sum(inventory => (long)inventory.QuantityOrdered),
            itemInventory.Sum(inventory => (long)inventory.QuantityAllocated),
            itemInventory.Sum(inventory => (long)inventory.QuantityOnHand - inventory.QuantityAllocated));
    }

    // Creates an item and sets its id and timestamps.
    public async Task<Item> CreateAsync(Item item, CancellationToken cancellationToken = default)
    {
        ValidateItem(item);
        var now = DateTime.UtcNow;
        item.Id = 0;
        item.CreatedAt = now;
        item.UpdatedAt = now;

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await items.InsertAsync(item, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            throw new ArgumentException("The item line, group, type, or supplier does not exist.", e);
        }

        return item;
    }

    // Updates an item. Returns false if the item does not exist.
    public async Task<bool> UpdateAsync(
        int id, Item item, CancellationToken cancellationToken = default)
    {
        ValidateItem(item);

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LockAsync(connection, transaction, id, cancellationToken);
        if (existing is null)
            return false;

        item.Id = id;
        item.CreatedAt = existing.CreatedAt;
        item.UpdatedAt = DateTime.UtcNow;
        try
        {
            await items.UpdateAsync(item, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            throw new ArgumentException("The item line, group, type, or supplier does not exist.", e);
        }

        return true;
    }

    // Deletes an item. Returns false if it does not exist.
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var item = await LockAsync(connection, transaction, id, cancellationToken);
        if (item is null)
            return false;

        try
        {
            await items.DeleteAsync(item, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.RowIsReferenced2)
        {
            throw new ArgumentException(
                "The item cannot be deleted because other records reference it.", e);
        }

        return true;
    }

    private async Task<Item?> LockAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        int id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT `id` FROM `item` WHERE `id` = @id FOR UPDATE";
        command.Parameters.AddWithValue("@id", id);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
            return null;

        var result = await items.GetAsync(connection, transaction,
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken);
        return result.SingleOrDefault();
    }

    private static void ValidateItem(Item item)
    {
        var missing = new (string Name, string Value)[]
        {
            (nameof(Item.Code), item.Code),
            (nameof(Item.Description), item.Description),
            (nameof(Item.Barcode), item.Barcode),
            (nameof(Item.ModelNumber), item.ModelNumber),
            (nameof(Item.PackagingType), item.PackagingType),
            (nameof(Item.SupplierSku), item.SupplierSku)
        }
        .Where(field => string.IsNullOrWhiteSpace(field.Value))
        .Select(field => field.Name)
        .ToArray();

        if (missing.Length > 0)
            throw new ArgumentException($"Missing required fields: {string.Join(", ", missing)}.");

        var tooLong = new (string Name, string Value)[]
        {
            (nameof(Item.Code), item.Code),
            (nameof(Item.Barcode), item.Barcode),
            (nameof(Item.ModelNumber), item.ModelNumber),
            (nameof(Item.PackagingType), item.PackagingType),
            (nameof(Item.SupplierSku), item.SupplierSku)
        }
        .Where(field => field.Value.Length > 255)
        .Select(field => field.Name)
        .ToArray();
        if (tooLong.Length > 0)
            throw new ArgumentException($"Fields must not exceed 255 characters: {string.Join(", ", tooLong)}.");

        if (item.UnitWeight < 0 || item.CommodityCode < 0 ||
            item.MinPurchaseQuantity < 0 || item.CaseSize < 0 || item.OrderMultiple < 0)
        {
            throw new ArgumentException("Weight and quantity fields cannot be negative.");
        }
    }
}

/// Summed inventory quantities for an item across all locations.</summary>
public sealed record ItemInventoryTotals(
    long TotalExpected,
    long TotalOrdered,
    long TotalAllocated,
    long TotalAvailable);
