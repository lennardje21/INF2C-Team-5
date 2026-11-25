using System.ComponentModel.DataAnnotations;
using CargoHub.Api.Monitoring;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Api.Controllers;

[ApiController]
[Route("metrics")]
public sealed class MetricsController(UptimeService uptime, ILogger<MetricsController> logger) : ControllerBase
{
    /// <summary>Returns the last seven days of measured availability and SLA compliance.</summary>
    [HttpGet("uptime")]
    [ProducesResponseType<UptimeReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<UptimeReport>> GetUptime(
        [FromQuery(Name = "SLA"), Range(typeof(decimal), "0", "100")] decimal sla = 99.99m,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            return Ok(await uptime.GetAsync(sla, cancellationToken));
        }
        catch (UptimeUnavailableException error)
        {
            logger.LogWarning("Uptime metrics unavailable: {Reason}", error.Message);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, detail: error.Message);
        }
    }

    /// <summary>
    /// Returns every measured request from `from` up to and including `to` (dates as
    /// yyyy-MM-dd, UTC), and the percentage that stayed within the threshold (default 300 ms).
    /// </summary>
    [HttpGet("/api/v2/metrics/performance")]
    [ProducesResponseType<PerformanceReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PerformanceReport>> GetPerformance(
        [FromServices] PerformanceLog performanceLog,
        [FromServices] Microsoft.Extensions.Options.IOptions<PerformanceOptions> performanceOptions,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (!TryParseDay(from, out var fromDay) || !TryParseDay(to, out var toDay))
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                detail: "Give 'from' and 'to' as dates in the format yyyy-MM-dd.");
        if (fromDay > toDay)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                detail: "'from' must be on or before 'to'.");

        var threshold = performanceOptions.Value.SlowRequestThresholdMs;
        var requests = await performanceLog.ReadAsync(fromDay, toDay, cancellationToken);
        var fast = requests.Count(r => r.DurationMs <= threshold);
        double? percentage = requests.Count == 0 ? null : Math.Round(100.0 * fast / requests.Count, 2);

        return Ok(new PerformanceReport(fromDay, toDay, threshold, requests.Count, percentage, requests));
    }

    private static bool TryParseDay(string? value, out DateOnly day) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out day);
}

public sealed record PerformanceReport(
    DateOnly From,
    DateOnly To,
    int ThresholdMs,
    int TotalRequests,
    // Null when there are no requests in the range.
    double? PercentageUnderThreshold,
    IReadOnlyList<PerformanceEntry> Requests);
