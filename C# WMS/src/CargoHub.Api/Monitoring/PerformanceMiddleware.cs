using System.Diagnostics;

namespace CargoHub.Api.Monitoring;

// Measures how long every request takes, including failed ones (4xx/5xx), writes it to the
// performance log and notifies the team when it was slow. Registered first in Program.cs
// so it also covers the error handling.
public sealed class PerformanceMiddleware(
    RequestDelegate next,
    PerformanceLog log,
    SlowRequestNotifier notifier,
    TimeProvider time)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // The Docker healthcheck calls /health every 5 seconds per container; measuring
        // those would bury the real requests.
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        var failed = false;
        try
        {
            await next(context);
        }
        catch
        {
            // Normally the exception handler already turned this into a 500 response.
            failed = true;
            throw;
        }
        finally
        {
            var durationMs = (long)Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            var path = context.Request.Path.Value ?? "/";

            log.Add(new PerformanceEntry(path, durationMs,
                time.GetUtcNow().UtcDateTime.ToString(PerformanceLog.TimestampFormat),
                failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode));

            // Group notifications by route (GET api/v2/items/{id}), not by path, so
            // /items/1 and /items/2 share one cooldown.
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            // Not awaited: the mail is sent in the background, the response does not wait.
            _ = notifier.Notify($"{context.Request.Method} {route ?? path}", path, durationMs);
        }
    }
}
