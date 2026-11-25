using System.ComponentModel.DataAnnotations;

namespace CargoHub.Api.Settings;

// Defaults for blockade detection (FR-7). They come from the "BlockadeDetection" section in
// appsettings.json and can be overridden with environment variables such as
// BlockadeDetection__OrderMaxMinutes. An admin or root can change the four times through
// /api/v2/blockade-settings; those values are stored in the database and win over these.
public class BlockadeDetectionOptions
{
    public const string SectionName = "BlockadeDetection";

    // One year; keeps the times sensible.
    public const int MaxMinutes = 525_600;

    // One day; the job waits this long at most between two checks.
    public const int MaxCheckIntervalMinutes = 1_440;

    // Turns the background job off, for example in tests.
    public bool Enabled { get; set; } = true;

    // How long an order may stay in Pending before it counts as blocked.
    [Range(1, MaxMinutes)]
    public int OrderMaxMinutes { get; set; } = 120;

    // The same for a shipment in Scheduled.
    [Range(1, MaxMinutes)]
    public int ShipmentMaxMinutes { get; set; } = 240;

    // The same for a transfer in Scheduled.
    [Range(1, MaxMinutes)]
    public int TransferMaxMinutes { get; set; } = 240;

    // How often the background job looks for blocked items.
    [Range(1, MaxCheckIntervalMinutes)]
    public int CheckIntervalMinutes { get; set; } = 15;

    // At most this many blocked items are mailed per check, so a large backlog cannot use up
    // the daily mail limit of Resend at once. The rest follows in the next checks.
    [Range(1, 1000)]
    public int MaxMailsPerCheck { get; set; } = 25;
}
