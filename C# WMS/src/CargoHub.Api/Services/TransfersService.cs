using CargoHub.Api.Data;
using CargoHub.Api.Models;
using MySqlConnector;

namespace CargoHub.Api.Services;

// Logic for stock transfers
public sealed class TransfersService(
    MySqlConnectionFactory factory,
    MySqlRepository<Transfer> transfers,
    MySqlRepository<TransferItem> transferItems,
    MySqlRepository<Inventory> inventories)
{
    public async Task<IReadOnlyList<Transfer>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await transfers.GetAsync(cancellationToken: cancellationToken);
        var itemsByTransfer = (await transferItems.GetAsync(cancellationToken: cancellationToken))
            .ToLookup(item => item.TransferId);

        foreach (var transfer in result)
            transfer.Items = itemsByTransfer[transfer.Id].ToList();

        return result;
    }

    public async Task<Transfer?> GetByIdAsync(
        int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        return await FindAsync(connection, null, id, cancellationToken);
    }

    public async Task<IReadOnlyList<TransferItem>?> GetItemsAsync(
        int id, CancellationToken cancellationToken = default)
    {
        var transfer = await GetByIdAsync(id, cancellationToken);
        return transfer?.Items;
    }

    public async Task<Transfer> CreateAsync(
        Transfer transfer, CancellationToken cancellationToken = default)
    {
        ValidateTransfer(transfer);
        ValidateItems(transfer.Items);

        var now = DateTime.UtcNow;
        transfer.Id = 0;
        transfer.Reference = string.Empty;
        transfer.TransferStatus = "Scheduled";
        transfer.CreatedAt = now;
        transfer.UpdatedAt = now;

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await transfers.InsertAsync(transfer, connection, transaction, cancellationToken);
            transfer.Reference = $"TRF-{transfer.Id:D6}";
            await transfers.UpdateAsync(transfer, connection, transaction, cancellationToken);
            await InsertItemsAsync(transfer.Id, transfer.Items, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            throw new ArgumentException("The source location or destination location or one of the items does not exist.", e);
        }

        return transfer;
    }

    public async Task<bool> UpdateAsync(
        int id, Transfer transfer, CancellationToken cancellationToken = default)
    {
        ValidateTransfer(transfer);
        ValidateItems(transfer.Items);

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LockAsync(connection, transaction, id, cancellationToken);
        if (existing is null)
            return false;

        transfer.Id = id;
        transfer.Reference = existing.Reference;
        transfer.CreatedAt = existing.CreatedAt;
        transfer.UpdatedAt = DateTime.UtcNow;

        try
        {
            await transfers.UpdateAsync(transfer, connection, transaction, cancellationToken);
            foreach (var item in existing.Items)
                await transferItems.DeleteAsync(item, connection, transaction, cancellationToken);
            await InsertItemsAsync(id, transfer.Items, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            throw new ArgumentException("The source location or destination location or one of the items does not exist.", e);
        }

        return true;
    }

    public async Task<bool> CommitAsync(
        int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var transfer = await LockAsync(connection, transaction, id, cancellationToken);
        if (transfer is null)
            return false;
        if (transfer.TransferStatus == "Processed")
            throw new ArgumentException("The transfer has already been processed.");

        var now = DateTime.UtcNow;
        try
        {
            foreach (var item in transfer.Items)
            {
                var source = await GetInventoryAsync(
                    connection, transaction, item.ItemId, transfer.FromLocationId, cancellationToken);
                if (source is not null)
                {
                    source.QuantityOnHand -= item.Amount;
                    source.UpdatedAt = now;
                    await inventories.UpdateAsync(source, connection, transaction, cancellationToken);
                }

                var destination = await GetInventoryAsync(
                    connection, transaction, item.ItemId, transfer.ToLocationId, cancellationToken);
                if (destination is null)
                {
                    destination = new Inventory
                    {
                        ItemId = item.ItemId,
                        LocationId = transfer.ToLocationId,
                        QuantityOnHand = item.Amount,
                        QuantityExpected = 0,
                        QuantityOrdered = 0,
                        QuantityAllocated = 0,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    await inventories.InsertAsync(destination, connection, transaction, cancellationToken);
                }
                else
                {
                    destination.QuantityOnHand += item.Amount;
                    destination.UpdatedAt = now;
                    await inventories.UpdateAsync(destination, connection, transaction, cancellationToken);
                }
            }

            transfer.TransferStatus = "Processed";
            transfer.UpdatedAt = now;
            await transfers.UpdateAsync(transfer, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.NoReferencedRow2)
        {
            throw new ArgumentException("The transfer references a missing inventory record.", e);
        }

        return true;
    }

    public async Task<bool> DeleteAsync(
        int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var transfer = await LockAsync(connection, transaction, id, cancellationToken);
        if (transfer is null)
            return false;

        foreach (var item in transfer.Items)
            await transferItems.DeleteAsync(item, connection, transaction, cancellationToken);
        await transfers.DeleteAsync(transfer, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<Transfer?> FindAsync(
        MySqlConnection connection, MySqlTransaction? transaction, int id,
        CancellationToken cancellationToken)
    {
        var transfer = (await transfers.GetAsync(connection, transaction,
            new Dictionary<string, object?> { ["id"] = id }, cancellationToken)).SingleOrDefault();
        if (transfer is not null)
        {
            transfer.Items = (await transferItems.GetAsync(connection, transaction,
                new Dictionary<string, object?> { ["transfer_id"] = id }, cancellationToken)).ToList();
        }
        return transfer;
    }

    private async Task<Transfer?> LockAsync(
        MySqlConnection connection, MySqlTransaction transaction, int id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT `id` FROM `transfer` WHERE `id` = @id FOR UPDATE";
        command.Parameters.AddWithValue("@id", id);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
            return null;
        return await FindAsync(connection, transaction, id, cancellationToken);
    }

    private async Task InsertItemsAsync(
        int transferId, IEnumerable<TransferItem> items, MySqlConnection connection,
        MySqlTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            item.Id = 0;
            item.TransferId = transferId;
            await transferItems.InsertAsync(item, connection, transaction, cancellationToken);
        }
    }

    private async Task<Inventory?> GetInventoryAsync(
        MySqlConnection connection, MySqlTransaction transaction, int itemId, int locationId,
        CancellationToken cancellationToken)
    {
        return (await inventories.GetAsync(connection, transaction,
            new Dictionary<string, object?> { ["item_id"] = itemId, ["location_id"] = locationId },
            cancellationToken)).SingleOrDefault();
    }

    private static void ValidateTransfer(Transfer transfer)
    {
        var missing = new List<string>();
        if (transfer.FromLocationId <= 0)
            missing.Add(nameof(Transfer.FromLocationId));
        if (transfer.ToLocationId <= 0)
            missing.Add(nameof(Transfer.ToLocationId));
        if (transfer.FromLocationId == transfer.ToLocationId)
            throw new ArgumentException("FromLocationId and ToLocationId must be different.");
        if (missing.Count > 0)
            throw new ArgumentException($"Missing required fields: {string.Join(", ", missing)}.");
    }

    private static void ValidateItems(IReadOnlyCollection<TransferItem> items)
    {
        if (items.Any(item => item.Amount <= 0))
            throw new ArgumentException("Every item needs an amount greater than 0.");
        if (items.GroupBy(item => item.ItemId).Any(group => group.Count() > 1))
            throw new ArgumentException("An item can only appear once in a transfer.");
    }
}