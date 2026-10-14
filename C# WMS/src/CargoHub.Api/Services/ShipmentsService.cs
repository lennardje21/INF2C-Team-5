using CargoHub.Api.Data;
using CargoHub.Api.Models;
using MySqlConnector;

namespace CargoHub.Api.Services;

public sealed class ShipmentsService(
    MySqlConnectionFactory factory,
    MySqlRepository<Shipment> shipments,
    MySqlRepository<ShipmentItem> shipmentItems)
{
    public async Task<IReadOnlyList<Shipment>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = await shipments.GetAsync(cancellationToken: cancellationToken);
        var itemsByShipment = (await shipmentItems.GetAsync(cancellationToken: cancellationToken))
            .ToLookup(i => i.ShipmentId);
        foreach (var shipment in result)
            shipment.Items = itemsByShipment[shipment.Id].ToList();
        return result;
    }

    public async Task<Shipment?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        return await FindAsync(connection, null, id, cancellationToken);
    }

    public async Task<IReadOnlyList<ShipmentItem>?> GetItemsAsync(int id, CancellationToken cancellationToken = default)
    {
        var shipment = await GetByIdAsync(id, cancellationToken);
        return shipment?.Items;
    }

    public async Task<Shipment> CreateAsync(Shipment shipment, CancellationToken cancellationToken = default)
    {
        ValidateShipment(shipment);
        ValidateItems(shipment.Items);
        var now = DateTime.UtcNow;
        shipment.Id = 0;
        shipment.ShipmentDate = AsUtc(shipment.ShipmentDate);
        shipment.CreatedAt = now;
        shipment.UpdatedAt = now;

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            // The reference is built from the id, which MySQL only assigns on insert.
            shipment.Reference = string.Empty;
            await shipments.InsertAsync(shipment, connection, transaction, cancellationToken);
            shipment.Reference = ReferenceFor(shipment.Id);
            await shipments.UpdateAsync(shipment, connection, transaction, cancellationToken);
            await InsertItemsAsync(shipment.Id, shipment.Items, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            throw new ArgumentException("The order or one of the items does not exist.", e);
        }
        return shipment;
    }

    // Updates the shipment's own fields; items change through UpdateItemsAsync.
    public async Task<bool> UpdateAsync(int id, Shipment shipment, CancellationToken cancellationToken = default)
    {
        ValidateShipment(shipment);

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LockAsync(connection, transaction, id, cancellationToken);
        if (existing is null)
            return false;

        shipment.Id = id;
        shipment.Reference = existing.Reference;
        shipment.ShipmentDate = AsUtc(shipment.ShipmentDate);
        shipment.CreatedAt = existing.CreatedAt;
        shipment.UpdatedAt = DateTime.UtcNow;
        try
        {
            await shipments.UpdateAsync(shipment, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            throw new ArgumentException($"Order {shipment.OrderId} does not exist.", e);
        }
        return true;
    }

    // A shipment belongs to a single order; the list form is kept from the Python API.
    public async Task<bool> UpdateOrdersAsync(int id, IReadOnlyList<int> orderIds,
        CancellationToken cancellationToken = default)
    {
        if (orderIds.Count != 1)
            throw new ArgumentException("A shipment must belong to exactly one order.");

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var shipment = await LockAsync(connection, transaction, id, cancellationToken);
        if (shipment is null)
            return false;

        shipment.OrderId = orderIds[0];
        shipment.UpdatedAt = DateTime.UtcNow;
        try
        {
            await shipments.UpdateAsync(shipment, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            throw new ArgumentException($"Order {orderIds[0]} does not exist.", e);
        }
        return true;
    }

    // Replaces the items and moves the difference in amount onto quantity_ordered,
    // on the inventory row with the most stock for each item (as in the Python API).
    public async Task<bool> UpdateItemsAsync(int id, IReadOnlyList<ShipmentItem> items,
        CancellationToken cancellationToken = default)
    {
        ValidateItems(items);

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var shipment = await LockAsync(connection, transaction, id, cancellationToken);
        if (shipment is null)
            return false;

        var oldAmounts = shipment.Items.ToDictionary(i => i.ItemId, i => i.Amount);
        var newAmounts = items.ToDictionary(i => i.ItemId, i => i.Amount);
        var now = DateTime.UtcNow;
        try
        {
            foreach (var itemId in oldAmounts.Keys.Union(newAmounts.Keys))
            {
                var delta = newAmounts.GetValueOrDefault(itemId) - oldAmounts.GetValueOrDefault(itemId);
                if (delta != 0)
                    await AddToQuantityOrderedAsync(itemId, delta, now, connection, transaction, cancellationToken);
            }

            foreach (var old in shipment.Items)
                await shipmentItems.DeleteAsync(old, connection, transaction, cancellationToken);
            await InsertItemsAsync(id, items, connection, transaction, cancellationToken);

            shipment.UpdatedAt = now;
            await shipments.UpdateAsync(shipment, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            throw new ArgumentException("One of the items does not exist.", e);
        }
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var shipment = await LockAsync(connection, transaction, id, cancellationToken);
        if (shipment is null)
            return false;

        foreach (var item in shipment.Items)
            await shipmentItems.DeleteAsync(item, connection, transaction, cancellationToken);
        await shipments.DeleteAsync(shipment, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<Shipment?> FindAsync(MySqlConnection connection, MySqlTransaction? transaction,
        int id, CancellationToken cancellationToken)
    {
        var shipment = (await shipments.GetAsync(connection, transaction,
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken)).SingleOrDefault();
        if (shipment is not null)
            shipment.Items = (await shipmentItems.GetAsync(connection, transaction,
                new Dictionary<string, object?> { ["shipment_id"] = id }, cancellationToken)).ToList();
        return shipment;
    }

    // Locks the shipment row so concurrent changes to the same shipment run one after another.
    private async Task<Shipment?> LockAsync(MySqlConnection connection, MySqlTransaction transaction,
        int id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT `id` FROM `shipment` WHERE `id` = @id FOR UPDATE";
        command.Parameters.AddWithValue("@id", id);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
            return null;
        return await FindAsync(connection, transaction, id, cancellationToken);
    }

    private async Task InsertItemsAsync(int shipmentId, IEnumerable<ShipmentItem> items,
        MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            item.Id = 0;
            item.ShipmentId = shipmentId;
            await shipmentItems.InsertAsync(item, connection, transaction, cancellationToken);
        }
    }

    // A single UPDATE, so concurrent requests cannot overwrite each other's change.
    private static async Task AddToQuantityOrderedAsync(int itemId, int delta, DateTime now,
        MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE `inventory` SET `quantity_ordered` = GREATEST(0, `quantity_ordered` + @delta), " +
            "`updated_at` = @now WHERE `item_id` = @itemId " +
            "ORDER BY `quantity_on_hand` DESC, `location_id` LIMIT 1";
        command.Parameters.AddWithValue("@delta", delta);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@itemId", itemId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Every shipment reference is "SHP-" followed by its id, padded to 6 digits.
    private static string ReferenceFor(int id) => $"SHP-{id:D6}";

    // Missing text fields arrive as "" because the model defaults to string.Empty.
    private static void ValidateShipment(Shipment shipment)
    {
        var missing = new (string Name, string Value)[]
        {
            (nameof(Shipment.ShipmentType), shipment.ShipmentType),
            (nameof(Shipment.ShipmentStatus), shipment.ShipmentStatus),
            (nameof(Shipment.CarrierName), shipment.CarrierName),
            (nameof(Shipment.ShippingMethod), shipment.ShippingMethod),
            (nameof(Shipment.PaymentType), shipment.PaymentType)
        }.Where(f => string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Name).ToList();
        if (shipment.ShipmentDate == default)
            missing.Add(nameof(Shipment.ShipmentDate));
        if (missing.Count > 0)
            throw new ArgumentException($"Missing required fields: {string.Join(", ", missing)}.");
    }

    private static void ValidateItems(IReadOnlyCollection<ShipmentItem> items)
    {
        if (items.Any(i => i.Amount <= 0))
            throw new ArgumentException("Every item needs an amount greater than 0.");
        if (items.GroupBy(i => i.ItemId).Any(g => g.Count() > 1))
            throw new ArgumentException("An item can only appear once in a shipment.");
    }

    // Dates sent without a time zone are treated as UTC, like the stored data.
    private static DateTime AsUtc(DateTime date) => date.Kind switch
    {
        DateTimeKind.Unspecified => DateTime.SpecifyKind(date, DateTimeKind.Utc),
        DateTimeKind.Local => date.ToUniversalTime(),
        _ => date
    };
}
