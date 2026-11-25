using CargoHub.Api.Services;
using Microsoft.Extensions.Options;

namespace CargoHub.Api.Monitoring;

// Mails the team when a request is slower than the threshold, at most once per endpoint
// per cooldown period (default 15 minutes) so a slow endpoint does not flood the inbox.
// The cooldown is kept per container: with api-a and api-b the team can get at most one
// mail per container per period.
public sealed class SlowRequestNotifier(
    IEmailService email,
    IOptions<PerformanceOptions> options,
    TimeProvider time,
    ILogger<SlowRequestNotifier> logger)
{
    private readonly PerformanceOptions settings = options.Value;
    private readonly Dictionary<string, DateTimeOffset> lastSent = [];
    private readonly Lock gate = new();

    // Returns the send task when a mail goes out, or null when it was skipped.
    // The caller does not need to await it; IEmailService never throws.
    public Task<bool>? Notify(string endpoint, string path, long durationMs)
    {
        if (durationMs <= settings.SlowRequestThresholdMs)
            return null;

        logger.LogWarning("Slow request: {Endpoint} ({Path}) took {Duration} ms",
            endpoint, path, durationMs);

        if (string.IsNullOrWhiteSpace(settings.AlertEmail))
            return null;

        var now = time.GetUtcNow();
        lock (gate)
        {
            if (lastSent.TryGetValue(endpoint, out var sent) &&
                now - sent < TimeSpan.FromMinutes(settings.AlertCooldownMinutes))
                return null;
            lastSent[endpoint] = now;
        }

        var subject = $"[CargoHub] Slow request: {endpoint} took {durationMs} ms";
        var body =
            $"""
            A request took longer than {settings.SlowRequestThresholdMs} ms.

            Endpoint:  {endpoint}
            Path:      {path}
            Duration:  {durationMs} ms
            Time:      {now.UtcDateTime.ToString(PerformanceLog.TimestampFormat)} (UTC)
            Container: {Environment.MachineName}

            No further mail is sent for this endpoint for the next {settings.AlertCooldownMinutes} minutes.
            """;
        return email.SendAsync(settings.AlertEmail, subject, body, CancellationToken.None);
    }
}
