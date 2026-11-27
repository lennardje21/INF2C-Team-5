namespace CargoHub.Api.Settings;

// The times for blockade detection, in minutes. They apply to the whole system. This class
// lives in Settings and not in Models, because it is not a table row of its own: the
// database row is blockade_setting.
public class BlockadeSettings
{
    // How long an order may stay in Pending before it counts as blocked.
    public int OrderMaxMinutes { get; set; }

    // How long a shipment may stay in Scheduled before it counts as blocked.
    public int ShipmentMaxMinutes { get; set; }

    // How long a transfer may stay in Scheduled before it counts as blocked.
    public int TransferMaxMinutes { get; set; }

    // How often the system looks for blocked items.
    public int CheckIntervalMinutes { get; set; }
}
