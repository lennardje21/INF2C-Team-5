using System.ComponentModel.DataAnnotations;

namespace CargoHub.Api.Settings;

// Settings for blockade detection (FR-7). The values come from the "BlockadeDetection"
// section in appsettings.json and can be overridden with environment variables such as
// BlockadeDetection__OrderMaxMinutes.
public class BlockadeDetectionOptions
{
    public const string SectionName = "BlockadeDetection";

    // How long an order may stay in a not-finished status before it counts as blocked.
    [Range(1, int.MaxValue)]
    public int OrderMaxMinutes { get; set; } = 120;

    // The same for a shipment.
    [Range(1, int.MaxValue)]
    public int ShipmentMaxMinutes { get; set; } = 240;

    // The same for a transfer.
    [Range(1, int.MaxValue)]
    public int TransferMaxMinutes { get; set; } = 240;

    // How often the background job looks for blocked items.
    [Range(1, int.MaxValue)]
    public int CheckIntervalMinutes { get; set; } = 15;
}