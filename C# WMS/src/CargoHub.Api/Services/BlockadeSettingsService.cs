using CargoHub.Api.Data;
using CargoHub.Api.Settings;
using Microsoft.Extensions.Options;

namespace CargoHub.Api.Services;

// Reads and changes the blockade detection times. The times an admin or root has set are
// stored in the database, so both API containers use them; until then the defaults from
// appsettings.json (or the environment) apply.
public sealed class BlockadeSettingsService(
    MySqlConnectionFactory factory,
    IOptions<BlockadeDetectionOptions> options) : IBlockadeSettingsService
{
    public async Task<BlockadeSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT `order_max_minutes`, `shipment_max_minutes`, `transfer_max_minutes`, `check_interval_minutes`
            FROM `blockade_setting` WHERE `id` = 1
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new BlockadeSettings
            {
                OrderMaxMinutes = reader.GetInt32(0),
                ShipmentMaxMinutes = reader.GetInt32(1),
                TransferMaxMinutes = reader.GetInt32(2),
                CheckIntervalMinutes = reader.GetInt32(3)
            };
        }

        var defaults = options.Value;
        return new BlockadeSettings
        {
            OrderMaxMinutes = defaults.OrderMaxMinutes,
            ShipmentMaxMinutes = defaults.ShipmentMaxMinutes,
            TransferMaxMinutes = defaults.TransferMaxMinutes,
            CheckIntervalMinutes = defaults.CheckIntervalMinutes
        };
    }

    public async Task<BlockadeSettings> UpdateAsync(
        BlockadeSettings settings, CancellationToken cancellationToken = default)
    {
        Validate(settings);

        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `blockade_setting`
                (`id`, `order_max_minutes`, `shipment_max_minutes`, `transfer_max_minutes`,
                 `check_interval_minutes`, `updated_at`)
            VALUES (1, @order, @shipment, @transfer, @interval, @now) AS new_row
            ON DUPLICATE KEY UPDATE
                `order_max_minutes` = new_row.`order_max_minutes`,
                `shipment_max_minutes` = new_row.`shipment_max_minutes`,
                `transfer_max_minutes` = new_row.`transfer_max_minutes`,
                `check_interval_minutes` = new_row.`check_interval_minutes`,
                `updated_at` = new_row.`updated_at`
            """;
        command.Parameters.AddWithValue("@order", settings.OrderMaxMinutes);
        command.Parameters.AddWithValue("@shipment", settings.ShipmentMaxMinutes);
        command.Parameters.AddWithValue("@transfer", settings.TransferMaxMinutes);
        command.Parameters.AddWithValue("@interval", settings.CheckIntervalMinutes);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return settings;
    }

    private static void Validate(BlockadeSettings settings)
    {
        var errors = new List<string>();
        foreach (var (name, value, max) in new[]
        {
            (nameof(BlockadeSettings.OrderMaxMinutes), settings.OrderMaxMinutes, BlockadeDetectionOptions.MaxMinutes),
            (nameof(BlockadeSettings.ShipmentMaxMinutes), settings.ShipmentMaxMinutes, BlockadeDetectionOptions.MaxMinutes),
            (nameof(BlockadeSettings.TransferMaxMinutes), settings.TransferMaxMinutes, BlockadeDetectionOptions.MaxMinutes),
            (nameof(BlockadeSettings.CheckIntervalMinutes), settings.CheckIntervalMinutes, BlockadeDetectionOptions.MaxCheckIntervalMinutes)
        })
        {
            if (value < 1 || value > max)
                errors.Add($"{name} must be a whole number of minutes from 1 to {max}.");
        }

        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors));
    }
}
