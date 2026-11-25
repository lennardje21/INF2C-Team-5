using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace CargoHub.Api.Monitoring;

// One measured request, as one JSON line in perf-dd-mm-yyyy.log.
public sealed record PerformanceEntry(
    [property: JsonPropertyName("endpoint")] string Endpoint,
    [property: JsonPropertyName("duration_ms")] long DurationMs,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("status_code")] int StatusCode);

// Writes and reads the performance log.
//
// Requests only put their measurement in a queue; this background service writes the queue
// to disk, so a request never waits for the disk and lines never interleave within one
// container. Each container writes to its own subfolder (named after its host name), so
// api-a and api-b never write to the same file. Reading combines all subfolders.
// Files are never deleted: there is no fixed retention period.
public sealed class PerformanceLog(
    IOptions<PerformanceOptions> options,
    IHostEnvironment environment,
    ILogger<PerformanceLog> logger) : BackgroundService
{
    public const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss";

    private readonly Channel<PerformanceEntry> queue = Channel.CreateUnbounded<PerformanceEntry>(
        new UnboundedChannelOptions { SingleReader = true });

    // Absolute folder that holds one subfolder per container.
    public string Directory { get; } = Path.GetFullPath(
        options.Value.LogDirectory, environment.ContentRootPath);

    private string OwnDirectory => Path.Combine(Directory, Environment.MachineName);

    public static string FileName(DateOnly day) =>
        $"perf-{day.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}.log";

    public void Add(PerformanceEntry entry) => queue.Writer.TryWrite(entry);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Keep writing what is still queued when the app stops.
        await foreach (var entry in queue.Reader.ReadAllAsync(CancellationToken.None))
        {
            try
            {
                System.IO.Directory.CreateDirectory(OwnDirectory);
                var day = DateOnly.FromDateTime(
                    DateTime.ParseExact(entry.Timestamp, TimestampFormat, CultureInfo.InvariantCulture));
                var line = JsonSerializer.Serialize(entry) + "\n";

                await using var file = new FileStream(Path.Combine(OwnDirectory, FileName(day)),
                    FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                await file.WriteAsync(Encoding.UTF8.GetBytes(line), CancellationToken.None);
            }
            catch (Exception exception)
            {
                // A full or read-only disk must not stop the API.
                logger.LogError(exception, "Could not write the performance log.");
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Writer.TryComplete();
        return base.StopAsync(cancellationToken);
    }

    // All entries from `from` up to and including `to`, from every container, oldest first.
    public async Task<List<PerformanceEntry>> ReadAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var entries = new List<PerformanceEntry>();
        if (!System.IO.Directory.Exists(Directory))
            return entries;

        var folders = System.IO.Directory.GetDirectories(Directory);
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            foreach (var folder in folders)
            {
                var path = Path.Combine(folder, FileName(day));
                if (!File.Exists(path))
                    continue;

                await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(file);
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    var entry = Parse(line);
                    if (entry is not null)
                        entries.Add(entry);
                }
            }
        }

        // The fixed timestamp format sorts correctly as text.
        entries.Sort((a, b) => string.CompareOrdinal(a.Timestamp, b.Timestamp));
        return entries;
    }

    // Skips empty, half-written or otherwise broken lines instead of failing the report.
    private static PerformanceEntry? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        try
        {
            return JsonSerializer.Deserialize<PerformanceEntry>(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
