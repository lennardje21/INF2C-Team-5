using CargoHub.Api.Data;
using CargoHub.Api.Models;
using MySqlConnector;

namespace CargoHub.Api.Services;

/// <summary>Business logic for clients and the orders that belong to them.</summary>
public sealed class ClientsService(
    MySqlConnectionFactory factory,
    MySqlRepository<Client> clients,
    MySqlRepository<Order> orders,
    MySqlRepository<OrderItem> orderItems)
{
    /// <summary>Returns all clients.</summary>
    public Task<IReadOnlyList<Client>> GetAllAsync(CancellationToken cancellationToken = default) =>
        clients.GetAsync(cancellationToken: cancellationToken);

    /// <summary>Returns one client, or null if not found.</summary>
    public async Task<Client?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        return await FindAsync(connection, id, cancellationToken);
    }

    /// <summary>
    /// Returns the orders where the client is the client, ship-to or bill-to client,
    /// each with its items. Returns null if the client does not exist.
    /// </summary>
    public async Task<IReadOnlyList<Order>?> GetOrdersAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        if (await FindAsync(connection, id, cancellationToken) is null)
            return null;

        var result = new Dictionary<int, Order>();
        foreach (var column in new[] { "client_id", "ship_to_client_id", "bill_to_client_id" })
        {
            var matches = await orders.GetAsync(connection, null,
                new Dictionary<string, object?> { [column] = id }, cancellationToken);
            foreach (var order in matches)
                result.TryAdd(order.Id, order);
        }

        foreach (var order in result.Values)
            order.Items = (await orderItems.GetAsync(connection, null,
                new Dictionary<string, object?> { ["order_id"] = order.Id }, cancellationToken)).ToList();
        return result.Values.OrderBy(o => o.Id).ToList();
    }

    /// <summary>Creates a client; the id and timestamps are set here.</summary>
    public async Task<Client> CreateAsync(Client client, CancellationToken cancellationToken = default)
    {
        Validate(client);
        var now = DateTime.UtcNow;
        client.Id = 0;
        client.CreatedAt = now;
        client.UpdatedAt = now;

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await clients.InsertAsync(client, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return client;
    }

    /// <summary>Updates a client; the id and creation time stay the same. Returns false if not found.</summary>
    public async Task<bool> UpdateAsync(int id, Client client, CancellationToken cancellationToken = default)
    {
        Validate(client);

        await using var connection = await factory.OpenAsync(cancellationToken);
        var existing = await FindAsync(connection, id, cancellationToken);
        if (existing is null)
            return false;

        client.Id = id;
        client.CreatedAt = existing.CreatedAt;
        client.UpdatedAt = DateTime.UtcNow;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await clients.UpdateAsync(client, connection, transaction, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            // Deleted by another request in the meantime.
            return false;
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Deletes a client. Returns false if not found, and throws InvalidOperationException
    /// if orders still refer to the client.
    /// </summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await clients.DeleteAsync(new Client { Id = id }, connection, transaction, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.RowIsReferenced2)
        {
            throw new InvalidOperationException($"Client {id} still has orders and cannot be deleted.", e);
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>Loads one client on the given connection.</summary>
    private async Task<Client?> FindAsync(MySqlConnection connection, int id, CancellationToken cancellationToken) =>
        (await clients.GetAsync(connection, null,
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken)).SingleOrDefault();

    /// <summary>Checks that the required fields are filled in.</summary>
    private static void Validate(Client client)
    {
        var missing = new (string Name, string Value)[]
        {
            (nameof(Client.Name), client.Name),
            (nameof(Client.Address), client.Address),
            (nameof(Client.City), client.City),
            (nameof(Client.ZipCode), client.ZipCode),
            (nameof(Client.Province), client.Province),
            (nameof(Client.Country), client.Country),
            (nameof(Client.ContactName), client.ContactName),
            (nameof(Client.ContactPhone), client.ContactPhone),
            (nameof(Client.ContactEmail), client.ContactEmail)
        }.Where(f => string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Name).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"Missing required fields: {string.Join(", ", missing)}.");
    }
}
