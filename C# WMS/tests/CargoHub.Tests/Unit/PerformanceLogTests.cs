using CargoHub.Api.Monitoring;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CargoHub.Tests.Unit;

public sealed class PerformanceLogTests : IDisposable
{
    private readonly string directory = NewTempDirectory();

    internal static string NewTempDirectory() =>
        Path.Combine(Path.GetTempPath(), "cargohub-perf-" + Guid.NewGuid().ToString("N"));

    internal static PerformanceLog CreateLog(string directory) =>
        new(Options.Create(new PerformanceOptions { LogDirectory = directory }),
            new HostingEnvironment { ContentRootPath = directory },
            NullLogger<PerformanceLog>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private void WriteLines(string container, string fileName, params string[] lines)
    {
        Directory.CreateDirectory(Path.Combine(directory, container));
        File.WriteAllLines(Path.Combine(directory, container, fileName), lines);
    }

    [Theory]
    [InlineData(2026, 11, 10, "perf-10-11-2026.log")]
    [InlineData(2026, 1, 5, "perf-05-01-2026.log")]
    public void FileName_IsPerfDayMonthYear(int year, int month, int day, string expected)
    {
        Assert.Equal(expected, PerformanceLog.FileName(new DateOnly(year, month, day)));
    }

    [Fact]
    public async Task Add_WritesOneJsonLinePerRequestToTheFileOfThatDay()
    {
        var log = CreateLog(directory);
        await log.StartAsync(CancellationToken.None);

        log.Add(new PerformanceEntry("/example/id", 120, "2026-11-10T14:30:00", 200));
        log.Add(new PerformanceEntry("/api/v2/items/5", 450, "2026-11-10T23:59:59", 500));
        log.Add(new PerformanceEntry("/api/v2/items/6", 10, "2026-11-11T00:00:00", 404));
        // Stopping writes everything that is still queued.
        await log.StopAsync(CancellationToken.None);

        var own = Path.Combine(directory, Environment.MachineName);
        Assert.Equal(
            [
                """{"endpoint":"/example/id","duration_ms":120,"timestamp":"2026-11-10T14:30:00","status_code":200}""",
                """{"endpoint":"/api/v2/items/5","duration_ms":450,"timestamp":"2026-11-10T23:59:59","status_code":500}""",
            ],
            await File.ReadAllLinesAsync(Path.Combine(own, "perf-10-11-2026.log")));
        Assert.Equal(
            ["""{"endpoint":"/api/v2/items/6","duration_ms":10,"timestamp":"2026-11-11T00:00:00","status_code":404}"""],
            await File.ReadAllLinesAsync(Path.Combine(own, "perf-11-11-2026.log")));
    }

    [Fact]
    public async Task Add_AppendsToAnExistingFile()
    {
        WriteLines(Environment.MachineName, "perf-10-11-2026.log",
            """{"endpoint":"/old","duration_ms":1,"timestamp":"2026-11-10T08:00:00","status_code":200}""");
        var log = CreateLog(directory);
        await log.StartAsync(CancellationToken.None);

        log.Add(new PerformanceEntry("/new", 2, "2026-11-10T09:00:00", 200));
        await log.StopAsync(CancellationToken.None);

        var entries = await log.ReadAsync(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10), CancellationToken.None);
        Assert.Equal(["/old", "/new"], entries.Select(e => e.Endpoint));
    }

    [Fact]
    public async Task ReadAsync_CombinesAllContainersWithinTheRangeOldestFirst()
    {
        WriteLines("api-a", "perf-09-11-2026.log",
            """{"endpoint":"/before","duration_ms":1,"timestamp":"2026-11-09T23:59:59","status_code":200}""");
        WriteLines("api-a", "perf-10-11-2026.log",
            """{"endpoint":"/a2","duration_ms":2,"timestamp":"2026-11-10T12:00:00","status_code":200}""");
        WriteLines("api-b", "perf-10-11-2026.log",
            """{"endpoint":"/b1","duration_ms":3,"timestamp":"2026-11-10T08:00:00","status_code":404}""");
        WriteLines("api-b", "perf-11-11-2026.log",
            """{"endpoint":"/b3","duration_ms":4,"timestamp":"2026-11-11T23:59:59","status_code":500}""");
        WriteLines("api-a", "perf-12-11-2026.log",
            """{"endpoint":"/after","duration_ms":5,"timestamp":"2026-11-12T00:00:00","status_code":200}""");

        var entries = await CreateLog(directory)
            .ReadAsync(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11), CancellationToken.None);

        Assert.Equal(["/b1", "/a2", "/b3"], entries.Select(e => e.Endpoint));
        Assert.Equal(404, entries[0].StatusCode);
    }

    [Fact]
    public async Task ReadAsync_SkipsEmptyAndBrokenLines()
    {
        WriteLines("api-a", "perf-10-11-2026.log",
            """{"endpoint":"/ok","duration_ms":1,"timestamp":"2026-11-10T08:00:00","status_code":200}""",
            "",
            "not json",
            """{"endpoint":"/half-writ""");

        var entries = await CreateLog(directory)
            .ReadAsync(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10), CancellationToken.None);

        Assert.Equal("/ok", Assert.Single(entries).Endpoint);
    }

    [Fact]
    public async Task ReadAsync_ReturnsNothing_WhenNoLogExistsYet()
    {
        var entries = await CreateLog(directory)
            .ReadAsync(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10), CancellationToken.None);

        Assert.Empty(entries);
    }
}
