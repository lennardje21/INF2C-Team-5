using CargoHub.Api.Data;
using CargoHub.Api.Models;
using MySqlConnector;

namespace CargoHub.Api.Services;

/// <summary>Business logic for suppliers and the items that belong to them.</summary>
public sealed class SuppliersService(
    MySqlConnectionFactory factory,
    MySqlRepository<Supplier> suppliers)
{
    /// <summary>Returns all suppliers.</summary>
    public Task<IReadOnlyList<Supplier>> GetAllAsync(CancellationToken cancellationToken = default) =>
        suppliers.GetAsync(cancellationToken: cancellationToken);

    /// <summary>Returns one supplier, or null if not found.</summary>
    public async Task<Supplier?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        return await FindAsync(connection, id, cancellationToken);
    }

    /// <summary>Creates a supplier; the id and timestamps are set here.</summary>
    public async Task<Supplier> CreateAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        Validate(supplier);
        var now = DateTime.UtcNow;
        supplier.Id = 0;
        supplier.CreatedAt = now;
        supplier.UpdatedAt = now;

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await suppliers.InsertAsync(supplier, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return supplier;
    }

    /// <summary>Updates a supplier; the id and creation time stay the same. Returns false if not found.</summary>
    public async Task<bool> UpdateAsync(int id, Supplier supplier, CancellationToken cancellationToken = default)
    {
        Validate(supplier);

        await using var connection = await factory.OpenAsync(cancellationToken);
        var existing = await FindAsync(connection, id, cancellationToken);
        if (existing is null)
            return false;

        supplier.Id = id;
        supplier.CreatedAt = existing.CreatedAt;
        supplier.UpdatedAt = DateTime.UtcNow;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await suppliers.UpdateAsync(supplier, connection, transaction, cancellationToken);
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
    /// Deletes a supplier. Returns false if not found, and throws InvalidOperationException
    /// if items still refer to the supplier.
    /// </summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await suppliers.DeleteAsync(new Supplier { Id = id }, connection, transaction, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.RowIsReferenced2)
        {
            throw new InvalidOperationException($"Supplier {id} still has items and cannot be deleted.", e);
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>Loads one supplier on the given connection.</summary>
    private async Task<Supplier?> FindAsync(MySqlConnection connection, int id, CancellationToken cancellationToken) =>
        (await suppliers.GetAsync(connection, null,
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken)).SingleOrDefault();

    /// <summary>Checks that the required fields are filled in.</summary>
    private static void Validate(Supplier supplier)
    {
        var missing = new (string Name, string Value)[]
        {
            (nameof(Supplier.Name), supplier.Name),
            (nameof(Supplier.Address), supplier.Address),
            (nameof(Supplier.City), supplier.City),
            (nameof(Supplier.ZipCode), supplier.ZipCode),
            (nameof(Supplier.Province), supplier.Province),
            (nameof(Supplier.Country), supplier.Country),
            (nameof(Supplier.ContactName), supplier.ContactName),
            (nameof(Supplier.PhoneNumber), supplier.PhoneNumber),
            (nameof(Supplier.Code), supplier.Code),
            (nameof(Supplier.Reference), supplier.Reference)
        }.Where(f => string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Name).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"Missing required fields: {string.Join(", ", missing)}.");
    }
}
