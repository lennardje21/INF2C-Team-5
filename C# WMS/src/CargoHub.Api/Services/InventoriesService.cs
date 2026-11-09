using CargoHub.Api.Data;
using CargoHub.Api.Models;
using MySqlConnector;

namespace CargoHub.Api.Services;


// Logic for inventories.

public sealed class InventoriesService(
    MySqlConnectionFactory factory,
    MySqlRepository<Inventory> inventories)
{
    // Returns all inventories.
    public Task<IReadOnlyList<Inventory>> GetAllAsync(CancellationToken cancellationToken = default) =>
        inventories.GetAsync(cancellationToken: cancellationToken);

    // Returns one inventory, or null if not found.
    public async Task<Inventory?> GetByKeyAsync(int itemId, int locationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        return await FindAsync(connection, itemId, locationId, cancellationToken);
    }

    
    // Creates an inventory;  Throws ArgumentException if the item or location does not exist, and InvalidOperationException if it already exists.
    
    public async Task<Inventory> CreateAsync(Inventory inventory, CancellationToken cancellationToken = default)
    {
        Validate(inventory);
        var now = DateTime.UtcNow;
        inventory.CreatedAt = now;
        inventory.UpdatedAt = now;

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await inventories.InsertAsync(inventory, connection, transaction, cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            throw new ArgumentException(
                $"Item {inventory.ItemId} or location {inventory.LocationId} does not exist.", e);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new InvalidOperationException(
                $"Inventory for item {inventory.ItemId} at location {inventory.LocationId} already exists.", e);
        }
        await transaction.CommitAsync(cancellationToken);
        return inventory;
    }

    
    // Updates the quantities of an inventory
   
    
    public async Task<bool> UpdateAsync(int itemId, int locationId, Inventory inventory,
        CancellationToken cancellationToken = default)
    {
        Validate(inventory);

        await using var connection = await factory.OpenAsync(cancellationToken);
        var existing = await FindAsync(connection, itemId, locationId, cancellationToken);
        if (existing is null)
            return false;

        // The keys come from the URL
        inventory.ItemId = itemId;
        inventory.LocationId = locationId;
        inventory.CreatedAt = existing.CreatedAt;
        inventory.UpdatedAt = DateTime.UtcNow;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await inventories.UpdateAsync(inventory, connection, transaction, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            // Deleted by another request in the meantime.
            return false;
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // Deletes an inventory. Returns false if not found.
    public async Task<bool> DeleteAsync(int itemId, int locationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await inventories.DeleteAsync(new Inventory { ItemId = itemId, LocationId = locationId },
                connection, transaction, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // Loads one inventory on the given connection.
    private async Task<Inventory?> FindAsync(MySqlConnection connection, int itemId, int locationId,
        CancellationToken cancellationToken) =>
        (await inventories.GetAsync(connection, null,
            new Dictionary<string, object?> { ["item_id"] = itemId, ["location_id"] = locationId },
            cancellationToken)).SingleOrDefault();

    // Checks that the quantities are not negative.
    private static void Validate(Inventory inventory)
    {
        var negative = new (string Name, int Value)[]
        {
            (nameof(Inventory.QuantityOnHand), inventory.QuantityOnHand),
            (nameof(Inventory.QuantityExpected), inventory.QuantityExpected),
            (nameof(Inventory.QuantityOrdered), inventory.QuantityOrdered),
            (nameof(Inventory.QuantityAllocated), inventory.QuantityAllocated)
        }.Where(f => f.Value < 0).Select(f => f.Name).ToList();
        if (negative.Count > 0)
            throw new ArgumentException($"Quantities cannot be negative: {string.Join(", ", negative)}.");
    }
}