
using System.ComponentModel.DataAnnotations;
using CargoHub.Api.Data;
using CargoHub.Api.Models;
using MySqlConnector;

namespace CargoHub.Api.Services;

public sealed class WarehousesService(
    MySqlConnectionFactory factory,
    MySqlRepository<Warehouse> warehouses,
    MySqlRepository<Location> locations)
{
	// Get all warehouses
    public Task<IReadOnlyList<Warehouse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return warehouses.GetAsync(cancellationToken: cancellationToken);
    }

	// Get warehouse by id
    public async Task<Warehouse?> GetByIdAsync(
        int id, CancellationToken cancellationToken = default)
    {
        var result = await warehouses.GetAsync(
            new Dictionary<string, object?> { ["id"] = id },
            cancellationToken);

        return result.SingleOrDefault();
    }

	// Get locations of a warehouse by warehouse id
    public async Task<IReadOnlyList<Location>?> GetLocationsAsync(
        int id, CancellationToken cancellationToken = default)
    {
        var warehouse = await GetByIdAsync(id, cancellationToken);

        if (warehouse is null)
        {
            return null;
        }

        return await locations.GetAsync(
            new Dictionary<string, object?> { ["warehouse_id"] = id },
            cancellationToken);
    }

	// Create a new warehouse
    public async Task<Warehouse> CreateAsync(
        Warehouse warehouse, CancellationToken cancellationToken = default)
    {
        ValidateWarehouse(warehouse);

        var now = DateTime.UtcNow;
        warehouse.Id = 0;
        warehouse.CreatedAt = now;
        warehouse.UpdatedAt = now;

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await warehouses.InsertAsync(
            warehouse, connection, transaction, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return warehouse;
    }

	// Update an existing warehouse
    public async Task<bool> UpdateAsync(
        int id, Warehouse warehouse, CancellationToken cancellationToken = default)
    {
        ValidateWarehouse(warehouse);

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var existing = await LockAsync(connection, transaction, id, cancellationToken);

        if (existing is null)
        {
            return false;
        }

        warehouse.Id = id;
        warehouse.CreatedAt = existing.CreatedAt;
        warehouse.UpdatedAt = DateTime.UtcNow;

        await warehouses.UpdateAsync(
            warehouse, connection, transaction, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

	// Delete a warehouse by id
    public async Task<bool> DeleteAsync(
        int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var warehouse = await LockAsync(connection, transaction, id, cancellationToken);

        if (warehouse is null)
        {
            return false;
        }

        try
        {
            await warehouses.DeleteAsync(
                warehouse, connection, transaction, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.RowIsReferenced2)
        {
            throw new ArgumentException(
                "The warehouse cannot be deleted because other records reference it.", e);
        }

        return true;
    }

	// Lock a warehouse record for update to prevent concurrent modifications
    private async Task<Warehouse?> LockAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        int id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT `id` FROM `warehouse` WHERE `id` = @id FOR UPDATE";
        command.Parameters.AddWithValue("@id", id);

        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            return null;
        }

        var result = await warehouses.GetAsync(
            connection,
            transaction,
            new Dictionary<string, object?> { ["id"] = id },
            cancellationToken);

        return result.SingleOrDefault();
    }

	// Validate the warehouse object to ensure all required fields are present and valid
    private static void ValidateWarehouse(Warehouse warehouse)
    {
        var missing = new (string Name, string Value)[]
        {
            (nameof(Warehouse.Code), warehouse.Code),
            (nameof(Warehouse.Name), warehouse.Name),
            (nameof(Warehouse.Address), warehouse.Address),
            (nameof(Warehouse.City), warehouse.City),
            (nameof(Warehouse.ZipCode), warehouse.ZipCode),
            (nameof(Warehouse.Province), warehouse.Province),
            (nameof(Warehouse.Country), warehouse.Country),
            (nameof(Warehouse.ContactName), warehouse.ContactName),
            (nameof(Warehouse.ContactPhone), warehouse.ContactPhone),
            (nameof(Warehouse.ContactEmail), warehouse.ContactEmail)
        }
        .Where(f => string.IsNullOrWhiteSpace(f.Value))
        .Select(f => f.Name)
        .ToList();

        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"Missing required fields: {string.Join(", ", missing)}.");
        }

        var fields = new (string Name, string Value)[]
        {
            (nameof(Warehouse.Code), warehouse.Code),
            (nameof(Warehouse.Name), warehouse.Name),
            (nameof(Warehouse.Address), warehouse.Address),
            (nameof(Warehouse.City), warehouse.City),
            (nameof(Warehouse.ZipCode), warehouse.ZipCode),
            (nameof(Warehouse.Province), warehouse.Province),
            (nameof(Warehouse.Country), warehouse.Country),
            (nameof(Warehouse.ContactName), warehouse.ContactName),
            (nameof(Warehouse.ContactPhone), warehouse.ContactPhone),
            (nameof(Warehouse.ContactEmail), warehouse.ContactEmail)
        };

        var tooLong = fields
            .Where(f => f.Value.Length > 255)
            .Select(f => f.Name)
            .ToList();

        if (tooLong.Count > 0)
        {
            throw new ArgumentException(
                $"Fields must not exceed 255 characters: {string.Join(", ", tooLong)}.");
        }

        if (!new EmailAddressAttribute().IsValid(warehouse.ContactEmail))
        {
            throw new ArgumentException("ContactEmail must be a valid email address.");
        }
    }
}
