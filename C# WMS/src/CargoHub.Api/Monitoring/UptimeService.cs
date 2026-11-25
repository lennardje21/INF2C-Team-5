namespace CargoHub.Api.Monitoring;

public sealed record UptimeReport(
    int MonitorId, DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd,
    decimal UptimePercentage, decimal CoveragePercentage, decimal Sla,
    bool? SlaMet, decimal UpSeconds, decimal DownSeconds, decimal UnknownSeconds,
    decimal AllowedDowntimeSeconds);

public sealed class UptimeService(IUptimeKumaClient client)
{
    public async Task<UptimeReport> GetAsync(decimal sla, CancellationToken cancellationToken = default)
    {
        if (sla is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(sla), "SLA must be between 0 and 100 percent.");
        return Calculate(await client.GetHistoryAsync(cancellationToken), sla);
    }

    public static UptimeReport Calculate(UptimeHistory history, decimal sla)
    {
        if (sla is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(sla));
        if (history.IntervalSeconds is < 1 or > 86400)
            throw new UptimeUnavailableException("The monitoring interval is invalid.");

        const decimal weekSeconds = 7 * 24 * 60 * 60;
        var end = history.RetrievedAt;
        var start = end.AddDays(-7);
        var beats = history.Heartbeats.Where(b => b.Time <= end).OrderBy(b => b.Time).ToArray();
        if (beats.Length == 0 || beats[^1].Time < start)
            throw new UptimeUnavailableException("No uptime measurements are available for this monitor.");

        decimal up = 0, down = 0;
        var cursor = start;
        DateTimeOffset? previous = null;
        var maximumGap = TimeSpan.FromSeconds(history.IntervalSeconds * 2);
        foreach (var beat in beats)
        {
            if (beat.Status is < 0 or > 3 || beat.Duration < 0 || previous == beat.Time)
                throw new UptimeUnavailableException("The monitoring history contains invalid measurements.");

            // Kuma attributes the elapsed interval preceding a beat to that beat's status.
            // Use timestamps between adjacent checks to preserve sub-second precision.
            var duration = TimeSpan.FromSeconds((double)Math.Min(beat.Duration, weekSeconds));
            var intervalStart = previous ?? beat.Time.Subtract(duration);
            if (intervalStart < beat.Time - maximumGap)
                intervalStart = beat.Time - maximumGap;
            if (intervalStart < cursor)
                intervalStart = cursor;

            if (beat.Time > intervalStart)
            {
                var seconds = (decimal)(beat.Time - intervalStart).Ticks / TimeSpan.TicksPerSecond;
                if (beat.Status == 1) up += seconds;
                else down += seconds; // Pending and maintenance do not confirm availability.
                cursor = beat.Time;
            }
            previous = beat.Time;
        }

        // A recent measurement remains valid until the next expected check. A stale
        // monitor never makes a long unobserved period appear continuously online.
        var last = beats[^1];
        if (last.Time >= start && end - last.Time <= maximumGap)
        {
            var seconds = (decimal)(end - last.Time).Ticks / TimeSpan.TicksPerSecond;
            if (last.Status == 1) up += seconds;
            else down += seconds;
        }

        var unknown = weekSeconds - up - down;
        var percentage = up * 100 / weekSeconds;
        return new UptimeReport(history.MonitorId, start, end,
            Math.Round(percentage, 6), Math.Round((up + down) * 100 / weekSeconds, 6), sla,
            unknown == 0 ? percentage >= sla : null, up, down, unknown,
            weekSeconds * (100 - sla) / 100);
    }
}
