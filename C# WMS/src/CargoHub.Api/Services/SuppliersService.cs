using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Validation;
using MySqlConnector;

namespace CargoHub.Api.Services;

/// <summary>Business logic for suppliers and the items that belong to them.</summary>
public sealed class SuppliersService(
    MySqlConnectionFactory factory,
    MySqlRepository<Supplier> suppliers,
    MySqlRepository<Item> items)
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

    /// <summary>Returns the items associated with a supplier.</summary>
    public Task<IReadOnlyList<Item>> GetItemsAsync(
        int id, CancellationToken cancellationToken = default)
    {
        return items.GetAsync(
            new Dictionary<string, object?> { ["supplier_id"] = id }, cancellationToken);
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

    // Checks every field; all format errors come back together in one message.
    private static void Validate(Supplier supplier)
    {
        supplier.Code = FieldRules.Trim(supplier.Code);
        supplier.Name = FieldRules.Trim(supplier.Name);
        supplier.Address = FieldRules.Trim(supplier.Address);
        supplier.City = FieldRules.Trim(supplier.City);
        supplier.ZipCode = FieldRules.Trim(supplier.ZipCode);
        supplier.Province = FieldRules.Trim(supplier.Province);
        supplier.Country = FieldRules.Trim(supplier.Country);
        supplier.ContactName = FieldRules.Trim(supplier.ContactName);
        supplier.PhoneNumber = FieldRules.Trim(supplier.PhoneNumber);
        supplier.Reference = FieldRules.Trim(supplier.Reference);

        var fields = new (string Name, string Value)[]
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
        };

        FieldRules.CheckRequired(fields);
        FieldRules.CheckMaxLength(fields);
        FieldRules.CheckNoControlCharacters(fields);

        var errors = new List<string>();

        // A code such as "SUP-0001": letters and digits, optionally separated by single hyphens.
        if (!supplier.Code.Split('-').All(part => part.Length > 0 && part.All(char.IsAsciiLetterOrDigit)))
            errors.Add("Code may only contain letters, digits and single hyphens, such as 'SUP-0001'.");

        if (!supplier.Name.Any(char.IsLetterOrDigit))
            errors.Add("Name must contain at least one letter or digit.");

        if (!supplier.Address.Any(char.IsLetter))
            errors.Add("Address must contain a street name.");

        foreach (var (name, value) in new[]
                 {
                     (nameof(Supplier.City), supplier.City),
                     (nameof(Supplier.Province), supplier.Province),
                     (nameof(Supplier.Country), supplier.Country),
                     (nameof(Supplier.ContactName), supplier.ContactName)
                 })
        {
            if (!FieldRules.IsPlaceOrPersonName(value))
                errors.Add($"{name} {FieldRules.NameError}");
        }

        if (!FieldRules.IsValidZipCode(supplier.ZipCode, supplier.Country))
            errors.Add($"ZipCode {FieldRules.ZipCodeErrorFor(supplier.Country)}");

        if (!FieldRules.IsValidPhone(supplier.PhoneNumber))
            errors.Add($"PhoneNumber {FieldRules.PhoneError}");

        if (!supplier.Reference.Any(char.IsLetterOrDigit))
            errors.Add("Reference must contain at least one letter or digit.");

        FieldRules.ThrowIfAny(errors);
    }
}