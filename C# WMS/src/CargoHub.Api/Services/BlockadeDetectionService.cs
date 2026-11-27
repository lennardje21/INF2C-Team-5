using System.Globalization;
using CargoHub.Api.Data;
using CargoHub.Api.Settings;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace CargoHub.Api.Services;

// Finds blocked orders, shipments and transfers and mails the floormanagers of their
// warehouse. A blockade is an item that has stayed too long in a status that is not
// finished: an order in Pending, a shipment or transfer in Scheduled.
public sealed class BlockadeDetectionService(
    MySqlConnectionFactory factory,
    IBlockadeSettingsService settingsService,
    IEmailService emailService,
    IOptions<BlockadeDetectionOptions> options,
    ILogger<BlockadeDetectionService> logger) : IBlockadeDetectionService
{
    // A named MySQL lock, so only one API container runs a check at a time.
    private const string LockName = "cargohub_blockade_detection";

    // The query returns id, warehouse id, status and updated_at. Items that already got
    // their mail for this updated_at are left out, so a blockade is mailed only once. When
    // an item moves on and gets stuck again, updated_at is different and it is a new blockade.
    private sealed record Source(string Type, string Status, string Select, string From, string Id, string UpdatedAt);

    private static readonly Source[] Sources =
    [
        new("order", "Pending", "o.`id`, o.`warehouse_id`, o.`order_status`, o.`updated_at`",
            "`order` o", "o.`id`", "o.`updated_at`"),
        new("shipment", "Scheduled", "s.`id`, o.`warehouse_id`, s.`shipment_status`, s.`updated_at`",
            "`shipment` s JOIN `order` o ON o.`id` = s.`order_id`", "s.`id`", "s.`updated_at`"),
        new("transfer", "Scheduled", "t.`id`, l.`warehouse_id`, t.`transfer_status`, t.`updated_at`",
            "`transfer` t JOIN `location` l ON l.`id` = t.`from_location_id`", "t.`id`", "t.`updated_at`")
    ];

    private sealed record Blockade(
        string Type, int Id, int WarehouseId, string Status, DateTime UpdatedAt, int MaxMinutes);

    // Runs one check and returns how many blockades it handled. Does nothing (returns 0)
    // when another container is already running a check. A mail problem never throws:
    // IEmailService returns false, and the blockade is tried again at the next check.
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        if (!await TryLockAsync(connection, cancellationToken))
        {
            logger.LogInformation("Blockade check skipped: another container is already running it.");
            return 0;
        }

        try
        {
            var settings = await settingsService.GetAsync(cancellationToken);
            var limits = new Dictionary<string, int>
            {
                ["order"] = settings.OrderMaxMinutes,
                ["shipment"] = settings.ShipmentMaxMinutes,
                ["transfer"] = settings.TransferMaxMinutes
            };

            var now = DateTime.UtcNow;
            var blockades = new List<Blockade>();
            foreach (var source in Sources)
            {
                var limit = limits[source.Type];
                blockades.AddRange(await FindAsync(connection, source, limit, now.AddMinutes(-limit), cancellationToken));
            }

            var handled = 0;
            var mailed = 0;
            foreach (var warehouse in blockades.GroupBy(b => b.WarehouseId))
            {
                var recipients = await GetRecipientsAsync(connection, warehouse.Key, cancellationToken);
                if (recipients.Count == 0)
                {
                    // Nobody to mail. The blockades are still recorded, so this is logged once
                    // and a floormanager added later is not flooded with old blockades.
                    foreach (var blockade in warehouse)
                    {
                        if (await ClaimAsync(connection, blockade, cancellationToken))
                            handled++;
                    }
                    logger.LogWarning(
                        "Warehouse {WarehouseId} has {Count} blocked item(s) but no floormanager to notify.",
                        warehouse.Key, warehouse.Count());
                    continue;
                }

                foreach (var blockade in warehouse)
                {
                    if (mailed >= options.Value.MaxMailsPerCheck)
                        break;
                    if (!await ClaimAsync(connection, blockade, cancellationToken))
                        continue;

                    var sent = 0;
                    foreach (var recipient in recipients)
                    {
                        if (await emailService.SendAsync(
                            recipient, SubjectFor(blockade), BodyFor(blockade), cancellationToken))
                            sent++;
                    }

                    if (sent == 0)
                    {
                        // Nothing could be sent: forget the blockade so the next check tries again.
                        // A failed try does not count as a mail, so it does not use up the limit.
                        // The rest of this warehouse waits for the next check, because the mail
                        // service or these recipients most likely have a problem, and trying
                        // every blockade again would only make the check slow.
                        await UnclaimAsync(connection, blockade, cancellationToken);
                        logger.LogWarning(
                            "Could not mail the blockade of {Type} {Id}; will retry at the next check.",
                            blockade.Type, blockade.Id);
                        break;
                    }

                    handled++;
                    mailed++;
                }
            }

            logger.LogInformation("Blockade check ran: {Count} blockade(s) handled.", handled);
            return handled;
        }
        finally
        {
            await ReleaseLockAsync(connection);
        }
    }

    private static async Task<List<Blockade>> FindAsync(
        MySqlConnection connection, Source source, int maxMinutes, DateTime cutoff,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {source.Select}
            FROM {source.From}
            WHERE {StatusColumn(source)} = @status
              AND {source.UpdatedAt} < @cutoff
              AND NOT EXISTS (
                  SELECT 1 FROM `blockade_notification` n
                  WHERE n.`item_type` = @type AND n.`item_id` = {source.Id}
                    AND n.`item_updated_at` = {source.UpdatedAt})
            ORDER BY {source.UpdatedAt}, {source.Id}
            """;
        command.Parameters.AddWithValue("@status", source.Status);
        command.Parameters.AddWithValue("@cutoff", cutoff);
        command.Parameters.AddWithValue("@type", source.Type);

        var result = new List<Blockade>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new Blockade(
                source.Type, reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2),
                DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc), maxMinutes));
        }
        return result;
    }

    // The status column of each source.
    private static string StatusColumn(Source source) => source.Type switch
    {
        "order" => "o.`order_status`",
        "shipment" => "s.`shipment_status`",
        _ => "t.`transfer_status`"
    };

    private static async Task<List<string>> GetRecipientsAsync(
        MySqlConnection connection, int warehouseId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `email` FROM `account`
            WHERE `role` = 'floormanager' AND `warehouse_id` = @warehouse
              AND `deleted_at` IS NULL AND `email` IS NOT NULL
            ORDER BY `id`
            """;
        command.Parameters.AddWithValue("@warehouse", warehouseId);

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(reader.GetString(0));
        return result;
    }

    // Records that this blockade is being handled. False when it was already recorded.
    private static async Task<bool> ClaimAsync(
        MySqlConnection connection, Blockade blockade, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT IGNORE INTO `blockade_notification` (`item_type`, `item_id`, `item_updated_at`, `notified_at`)
            VALUES (@type, @id, @updated, @now)
            """;
        command.Parameters.AddWithValue("@type", blockade.Type);
        command.Parameters.AddWithValue("@id", blockade.Id);
        command.Parameters.AddWithValue("@updated", blockade.UpdatedAt);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task UnclaimAsync(
        MySqlConnection connection, Blockade blockade, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM `blockade_notification`
            WHERE `item_type` = @type AND `item_id` = @id AND `item_updated_at` = @updated
            """;
        command.Parameters.AddWithValue("@type", blockade.Type);
        command.Parameters.AddWithValue("@id", blockade.Id);
        command.Parameters.AddWithValue("@updated", blockade.UpdatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string SubjectFor(Blockade blockade) =>
        $"Blocked {blockade.Type} {blockade.Id} in warehouse {blockade.WarehouseId}";

    private static string BodyFor(Blockade blockade) => string.Join(Environment.NewLine,
        $"A {blockade.Type} has been blocked for too long.",
        "",
        $"Type: {blockade.Type}",
        $"Id: {blockade.Id}",
        $"Status: {blockade.Status}",
        $"Updated at: {blockade.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)}",
        $"Warehouse: {blockade.WarehouseId}",
        $"Allowed time in this status: {blockade.MaxMinutes} minutes");

    // GET_LOCK with timeout 0 returns at once: 1 when we got the lock, 0 when someone else has it.
    // The lock belongs to this connection, which stays open until the check is finished.
    private static async Task<bool> TryLockAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GET_LOCK(@name, 0)";
        command.Parameters.AddWithValue("@name", LockName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long acquired && acquired == 1;
    }

    // The connection goes back to the pool and is reused, so the lock is released by hand.
    // A failure here is only logged, so it can never hide the result of the check.
    private async Task ReleaseLockAsync(MySqlConnection connection)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT RELEASE_LOCK(@name)";
            command.Parameters.AddWithValue("@name", LockName);
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not release the blockade detection lock.");
        }
    }
}
