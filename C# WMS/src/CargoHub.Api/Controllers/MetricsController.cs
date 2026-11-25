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
}
