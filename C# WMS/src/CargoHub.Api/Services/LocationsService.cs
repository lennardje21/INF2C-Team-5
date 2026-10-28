using CargoHub.Api.Data;
using CargoHub.Api.Models;
using MySqlConnector;

namespace CargoHub.Api.Services;

// Logic for locations.
public sealed class LocationsService(
    MySqlConnectionFactory factory,
    MySqlRepository<Location> locations)
{
    // Returns all locations.
    public Task<IReadOnlyList<Location>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        locations.GetAsync(cancellationToken: cancellationToken);

    // Returns one location, or null if it does not exist.
    public async Task<Location?> GetByIdAsync(
        int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        return await FindAsync(connection, id, cancellationToken);
    }

    // Creates a location; the id and timestamps are set here.
    public async Task<Location> CreateAsync(
        Location location, CancellationToken cancellationToken = default)
    {
        Validate(location);

        var now = DateTime.UtcNow;
        location.Id = 0;
        location.CreatedAt = now;
        location.UpdatedAt = now;

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await locations.InsertAsync(location, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return location;
    }

    // Updates a location; its id and creation time stay the same.
    // Returns false if the location does not exist.
    public async Task<bool> UpdateAsync(
        int id, Location location, CancellationToken cancellationToken = default)
    {
        Validate(location);

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LockAsync(connection, transaction, id, cancellationToken);

        if (existing is null)
        {
            return false;
        }

        location.Id = id;
        location.CreatedAt = existing.CreatedAt;
        location.UpdatedAt = DateTime.UtcNow;

        await locations.UpdateAsync(location, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }
    
    // Deletes a location. Returns false if it does not exist and throws
    // <see cref="ArgumentException"/> when other records reference it.
    public async Task<bool> DeleteAsync(
        int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var location = await LockAsync(connection, transaction, id, cancellationToken);

        if (location is null)
        {
            return false;
        }

        try
        {
            await locations.DeleteAsync(location, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.RowIsReferenced2)
        {
            throw new ArgumentException(
                "The location cannot be deleted because other records reference it.", e);
        }

        return true;
    }

    private async Task<Location?> LockAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        int id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT `id` FROM `location` WHERE `id` = @id FOR UPDATE";
        command.Parameters.AddWithValue("@id", id);

        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            return null;
        }

        return await FindAsync(connection, id, cancellationToken, transaction);
    }

    private async Task<Location?> FindAsync(
        MySqlConnection connection,
        int id,
        CancellationToken cancellationToken,
        MySqlTransaction? transaction = null) =>
        (await locations.GetAsync(
            connection,
            transaction,
            new Dictionary<string, object?> { ["id"] = id },
            cancellationToken)).SingleOrDefault();

    private static void Validate(Location location)
    {
        if (location.WarehouseId <= 0)
        {
            throw new ArgumentException("WarehouseId must be greater than zero.");
        }

        var missing = new (string Name, string Value)[]
        {
            (nameof(Location.Code), location.Code),
            (nameof(Location.Name), location.Name)
        }
        .Where(f => string.IsNullOrWhiteSpace(f.Value))
        .Select(f => f.Name)
        .ToList();

        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"Missing required fields: {string.Join(", ", missing)}.");
        }

        var tooLong = new (string Name, string Value)[]
        {
            (nameof(Location.Code), location.Code),
            (nameof(Location.Name), location.Name)
        }
        .Where(f => f.Value.Length > 255)
        .Select(f => f.Name)
        .ToList();

        if (tooLong.Count > 0)
        {
            throw new ArgumentException(
                $"Fields must not exceed 255 characters: {string.Join(", ", tooLong)}.");
        }
    }
}