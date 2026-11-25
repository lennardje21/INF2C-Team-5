using System.ComponentModel.DataAnnotations;

namespace CargoHub.Api.Monitoring;

// Settings for performance monitoring (NFR-2), from the "Performance" section in
// appsettings.json. Override them with environment variables such as
// Performance__SlowRequestThresholdMs.
public sealed class PerformanceOptions
{
    public const string SectionName = "Performance";

    // Folder for the perf-dd-mm-yyyy.log files. Relative paths start at the app folder
    // (/app in the container, where a volume is mounted on /app/logs).
    [Required]
    public string LogDirectory { get; set; } = "logs";

    // A request that takes longer than this is slow: it triggers a notification and does
    // not count as "under the threshold" in GET /api/v2/metrics/performance.
    [Range(1, int.MaxValue)]
    public int SlowRequestThresholdMs { get; set; } = 300;

    // At most one notification per endpoint within this many minutes.
    [Range(1, int.MaxValue)]
    public int AlertCooldownMinutes { get; set; } = 15;

    // Who gets the slow-request mail. Empty turns the notification off.
    public string AlertEmail { get; set; } = "";

    // Maps GET /dev/slow?ms=500, a deliberately slow endpoint to try out the notification.
    // Never turn this on in production.
    public bool TestEndpointEnabled { get; set; } = false;
}
