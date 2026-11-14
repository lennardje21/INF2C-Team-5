using System.Text;
using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Validation;
using MySqlConnector;

namespace CargoHub.Api.Services;

/// Business logic for item lines.
public sealed class ItemLinesService(
    MySqlConnectionFactory factory,
    MySqlRepository<ItemLine> itemLines)
{
    private const int MaxDescriptionBytes = 65_535;

    /// Returns all item lines.
    public Task<IReadOnlyList<ItemLine>> GetAllAsync(CancellationToken cancellationToken = default) =>
        itemLines.GetAsync(cancellationToken: cancellationToken);

    /// Returns one item line, or null if it does not exist.
    public async Task<ItemLine?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var result = await itemLines.GetAsync(
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken);
        return result.SingleOrDefault();
    }

    /// Creates an item line; the id and timestamps are set here.
    public async Task<ItemLine> CreateAsync(
        ItemLine itemLine, CancellationToken cancellationToken = default)
    {
        Validate(itemLine);
        var now = DateTime.UtcNow;
        itemLine.Id = 0;
        itemLine.CreatedAt = now;
        itemLine.UpdatedAt = now;

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await itemLines.InsertAsync(itemLine, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return itemLine;
    }

    /// Updates an item line; returns false if it does not exist.
    public async Task<bool> UpdateAsync(
        int id, ItemLine itemLine, CancellationToken cancellationToken = default)
    {
        Validate(itemLine);

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LockAsync(connection, transaction, id, cancellationToken);
        if (existing is null)
            return false;

        itemLine.Id = id;
        itemLine.CreatedAt = existing.CreatedAt;
        itemLine.UpdatedAt = DateTime.UtcNow;
        await itemLines.UpdateAsync(itemLine, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// Deletes an item line; returns false if missing, or throws if items reference it.
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var itemLine = await LockAsync(connection, transaction, id, cancellationToken);
        if (itemLine is null)
            return false;

        try
        {
            await itemLines.DeleteAsync(itemLine, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.RowIsReferenced2)
        {
            throw new ArgumentException(
                "The item line cannot be deleted because items reference it.", e);
        }

        return true;
    }

    private async Task<ItemLine?> LockAsync(
        MySqlConnection connection, MySqlTransaction transaction,
        int id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT `id` FROM `item_line` WHERE `id` = @id FOR UPDATE";
        command.Parameters.AddWithValue("@id", id);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
            return null;

        var result = await itemLines.GetAsync(connection, transaction,
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken);
        return result.SingleOrDefault();
    }

    private static void Validate(ItemLine itemLine)
    {
        itemLine.Name = FieldRules.Trim(itemLine.Name);
        itemLine.Description = FieldRules.Trim(itemLine.Description);

        FieldRules.CheckRequired(
            (nameof(ItemLine.Name), itemLine.Name),
            (nameof(ItemLine.Description), itemLine.Description));

        if (itemLine.Name.EnumerateRunes().Count() > FieldRules.MaxTextLength)
            throw new ArgumentException("Name must not exceed 255 characters.");

        FieldRules.CheckNoControlCharacters((nameof(ItemLine.Name), itemLine.Name));

        if (itemLine.Description.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new ArgumentException(
                "Description must not contain control characters other than tabs or line breaks.");
        if (Encoding.UTF8.GetByteCount(itemLine.Description) > MaxDescriptionBytes)
            throw new ArgumentException($"Description must not exceed {MaxDescriptionBytes} UTF-8 bytes.");
    }
}