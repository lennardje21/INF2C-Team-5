using System.Text.RegularExpressions;
using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Validation;
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

    // Trims the text fields, then checks that they are filled in, fit in the database and have a valid format.
    private static void ValidateWarehouse(Warehouse warehouse)
    {
        warehouse.Code = FieldRules.Trim(warehouse.Code);
        warehouse.Name = FieldRules.Trim(warehouse.Name);
        warehouse.Address = FieldRules.Trim(warehouse.Address);
        warehouse.City = FieldRules.Trim(warehouse.City);
        warehouse.ZipCode = FieldRules.Trim(warehouse.ZipCode);
        warehouse.Province = FieldRules.Trim(warehouse.Province);
        warehouse.Country = FieldRules.Trim(warehouse.Country);
        warehouse.ContactName = FieldRules.Trim(warehouse.ContactName);
        warehouse.ContactPhone = FieldRules.Trim(warehouse.ContactPhone);
        warehouse.ContactEmail = FieldRules.Trim(warehouse.ContactEmail);

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

        FieldRules.CheckRequired(fields);
        FieldRules.CheckMaxLength(fields);
        FieldRules.CheckNoControlCharacters(fields);

        var errors = new List<string>();

        if (!WarehouseCode.IsMatch(warehouse.Code))
            errors.Add("Code may only contain letters and digits, optionally separated by single hyphens, such as 'VGH-AMB'.");

        if (!warehouse.Name.Any(char.IsLetterOrDigit))
            errors.Add("Name must contain at least one letter or digit.");

        if (!warehouse.Address.Any(char.IsLetter))
            errors.Add("Address must contain a street name.");

        foreach (var (name, value) in new[]
                 {
                     (nameof(Warehouse.City), warehouse.City),
                     (nameof(Warehouse.Province), warehouse.Province),
                     (nameof(Warehouse.Country), warehouse.Country),
                     (nameof(Warehouse.ContactName), warehouse.ContactName)
                 })
        {
            if (!FieldRules.IsPlaceOrPersonName(value))
                errors.Add($"{name} {FieldRules.NameError}");
        }

        if (!FieldRules.IsValidZipCode(warehouse.ZipCode, warehouse.Country))
            errors.Add($"ZipCode {FieldRules.ZipCodeErrorFor(warehouse.Country)}");

        if (!FieldRules.IsValidPhone(warehouse.ContactPhone))
            errors.Add($"ContactPhone {FieldRules.PhoneError}");

        if (!FieldRules.IsValidEmail(warehouse.ContactEmail))
            errors.Add($"ContactEmail {FieldRules.EmailError}");

        FieldRules.ThrowIfAny(errors);
    }

    // Letters and digits, optionally in groups separated by single hyphens, e.g. "VGH-AMB" or "WH-001".
    private static readonly Regex WarehouseCode = new(@"^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$", RegexOptions.Compiled);
}