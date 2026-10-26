using CargoHub.Api.Data;
using CargoHub.Api.Models;
using MySqlConnector;

namespace CargoHub.Api.Services;

/// <summary>Business logic for orders; each change runs in one transaction.</summary>
public sealed class OrdersService(
    MySqlConnectionFactory factory,
    MySqlRepository<Order> orders,
    MySqlRepository<OrderItem> orderItems)
{
    /// <summary>Returns all orders with their items.</summary>
    public async Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = await orders.GetAsync(cancellationToken: cancellationToken);
        var itemsByOrder = (await orderItems.GetAsync(cancellationToken: cancellationToken))
            .ToLookup(i => i.OrderId);
        foreach (var order in result)
            order.Items = WithRoundedPrices(itemsByOrder[order.Id]);
        return result;
    }

    /// <summary>Returns one order with its items, or null if not found.</summary>
    public async Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        return await FindAsync(connection, null, id, cancellationToken);
    }

    /// <summary>Returns the items of an order, or null if not found.</summary>
    public async Task<IReadOnlyList<OrderItem>?> GetItemsAsync(int id, CancellationToken cancellationToken = default)
    {
        var order = await GetByIdAsync(id, cancellationToken);
        return order?.Items;
    }

    /// <summary>
    /// Creates an order with its items; id, reference and timestamps are set here.
    /// Does not change the inventory, same as the Python API.
    /// </summary>
    public async Task<Order> CreateAsync(Order order, CancellationToken cancellationToken = default)
    {
        ValidateOrder(order);
        ValidateItems(order.Items);
        var now = DateTime.UtcNow;
        order.Id = 0;
        order.OrderDate = AsUtc(order.OrderDate);
        order.RequestDate = AsUtc(order.RequestDate);
        order.CreatedAt = now;
        order.UpdatedAt = now;

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            // The reference uses the id, which MySQL only assigns on insert.
            order.Reference = string.Empty;
            await orders.InsertAsync(order, connection, transaction, cancellationToken);
            order.Reference = ReferenceFor(order.Id);
            await orders.UpdateAsync(order, connection, transaction, cancellationToken);
            await InsertItemsAsync(order.Id, order.Items, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            throw new ArgumentException("The client, warehouse or one of the items does not exist.", e);
        }
        return order;
    }

    /// <summary>Updates the order's fields, not its items. Returns false if not found.</summary>
    public async Task<bool> UpdateAsync(int id, Order order, CancellationToken cancellationToken = default)
    {
        ValidateOrder(order);

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LockAsync(connection, transaction, id, cancellationToken);
        if (existing is null)
            return false;

        order.Id = id;
        order.Reference = existing.Reference;
        order.OrderDate = AsUtc(order.OrderDate);
        order.RequestDate = AsUtc(order.RequestDate);
        order.CreatedAt = existing.CreatedAt;
        order.UpdatedAt = DateTime.UtcNow;
        try
        {
            await orders.UpdateAsync(order, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            throw new ArgumentException("The client or warehouse does not exist.", e);
        }
        return true;
    }

    /// <summary>Replaces the items and updates quantity_allocated in the inventory. Returns false if not found.</summary>
    public async Task<bool> UpdateItemsAsync(int id, IReadOnlyList<OrderItem> items,
        CancellationToken cancellationToken = default)
    {
        ValidateItems(items);

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var order = await LockAsync(connection, transaction, id, cancellationToken);
        if (order is null)
            return false;

        var oldAmounts = order.Items.ToDictionary(i => i.ItemId, i => i.Amount);
        var newAmounts = items.ToDictionary(i => i.ItemId, i => i.Amount);
        var now = DateTime.UtcNow;
        try
        {
            foreach (var itemId in oldAmounts.Keys.Union(newAmounts.Keys))
            {
                var delta = newAmounts.GetValueOrDefault(itemId) - oldAmounts.GetValueOrDefault(itemId);
                if (delta != 0)
                    await AddToQuantityAllocatedAsync(itemId, delta, now, connection, transaction, cancellationToken);
            }

            foreach (var old in order.Items)
                await orderItems.DeleteAsync(old, connection, transaction, cancellationToken);
            await InsertItemsAsync(id, items, connection, transaction, cancellationToken);

            order.UpdatedAt = now;
            await orders.UpdateAsync(order, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            throw new ArgumentException("One of the items does not exist.", e);
        }
        return true;
    }

    /// <summary>
    /// Deletes the order and its items. Returns false if not found, and throws
    /// InvalidOperationException if shipments still refer to the order.
    /// Does not change the inventory, same as the Python API.
    /// </summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var order = await LockAsync(connection, transaction, id, cancellationToken);
        if (order is null)
            return false;

        try
        {
            // Items first: the database refuses to delete an order that still has items.
            foreach (var item in order.Items)
                await orderItems.DeleteAsync(item, connection, transaction, cancellationToken);
            await orders.DeleteAsync(order, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.RowIsReferenced2)
        {
            throw new InvalidOperationException($"Order {id} still has shipments and cannot be deleted.", e);
        }
        return true;
    }

    /// <summary>Loads one order with its items.</summary>
    private async Task<Order?> FindAsync(MySqlConnection connection, MySqlTransaction? transaction,
        int id, CancellationToken cancellationToken)
    {
        var order = (await orders.GetAsync(connection, transaction,
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken)).SingleOrDefault();
        if (order is not null)
            order.Items = WithRoundedPrices(await orderItems.GetAsync(connection, transaction,
                new Dictionary<string, object?> { ["order_id"] = id }, cancellationToken));
        return order;
    }

    /// <summary>Locks the order until the transaction ends, so changes to it run one at a time.</summary>
    private async Task<Order?> LockAsync(MySqlConnection connection, MySqlTransaction transaction,
        int id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT `id` FROM `order` WHERE `id` = @id FOR UPDATE";
        command.Parameters.AddWithValue("@id", id);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
            return null;
        return await FindAsync(connection, transaction, id, cancellationToken);
    }

    /// <summary>Inserts the items for an order.</summary>
    private async Task InsertItemsAsync(int orderId, IEnumerable<OrderItem> items,
        MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            item.Id = 0;
            item.OrderId = orderId;
            item.UnitPrice = RoundPrice(item.UnitPrice);
            await orderItems.InsertAsync(item, connection, transaction, cancellationToken);
        }
    }

    /// <summary>
    /// Adds the change in amount to quantity_allocated on the inventory row with the most stock.
    /// One UPDATE statement, so simultaneous requests cannot overwrite each other's change.
    /// </summary>
    private static async Task AddToQuantityAllocatedAsync(int itemId, int delta, DateTime now,
        MySqlConnection connection, MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE `inventory` SET `quantity_allocated` = GREATEST(0, `quantity_allocated` + @delta), " +
            "`updated_at` = @now WHERE `item_id` = @itemId " +
            "ORDER BY `quantity_on_hand` DESC, `location_id` LIMIT 1";
        command.Parameters.AddWithValue("@delta", delta);
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@itemId", itemId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Rounds a price to exactly 2 decimals, e.g. 21.3400 to 21.34 and 9.9 to 9.90.
    /// The database column stores 4 decimals, so prices are rounded when read and saved.
    /// </summary>
    private static decimal RoundPrice(decimal price) =>
        Math.Round(price, 2, MidpointRounding.AwayFromZero) + 0.00m;

    /// <summary>Rounds the prices of the given items and returns them as a list.</summary>
    private static List<OrderItem> WithRoundedPrices(IEnumerable<OrderItem> items)
    {
        var list = items.ToList();
        foreach (var item in list)
            item.UnitPrice = RoundPrice(item.UnitPrice);
        return list;
    }

    /// <summary>Builds the reference, e.g. ORD-004855.</summary>
    private static string ReferenceFor(int id) => $"ORD-{id:D6}";

    /// <summary>Checks that the required fields are filled in.</summary>
    private static void ValidateOrder(Order order)
    {
        var missing = new (string Name, string Value)[]
        {
            (nameof(Order.CustomerPoNumber), order.CustomerPoNumber),
            (nameof(Order.OrderStatus), order.OrderStatus)
        }.Where(f => string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Name).ToList();
        if (order.OrderDate == default)
            missing.Add(nameof(Order.OrderDate));
        if (order.RequestDate == default)
            missing.Add(nameof(Order.RequestDate));
        if (missing.Count > 0)
            throw new ArgumentException($"Missing required fields: {string.Join(", ", missing)}.");
    }

    /// <summary>Checks that amounts are positive, prices are not negative and no item appears twice.</summary>
    private static void ValidateItems(IReadOnlyCollection<OrderItem> items)
    {
        if (items.Any(i => i.Amount <= 0))
            throw new ArgumentException("Every item needs an amount greater than 0.");
        if (items.Any(i => i.UnitPrice < 0))
            throw new ArgumentException("Unit prices cannot be negative.");
        if (items.GroupBy(i => i.ItemId).Any(g => g.Count() > 1))
            throw new ArgumentException("An item can only appear once in an order.");
    }

    /// <summary>Treats dates without a time zone as UTC.</summary>
    private static DateTime AsUtc(DateTime date) => date.Kind switch
    {
        DateTimeKind.Unspecified => DateTime.SpecifyKind(date, DateTimeKind.Utc),
        DateTimeKind.Local => date.ToUniversalTime(),
        _ => date
    };
}
