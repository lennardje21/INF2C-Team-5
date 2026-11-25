using CargoHub.Api.Monitoring;

namespace CargoHub.UptimeVerification;

public sealed record TransitionScenario(
    string Name, UptimeHistory History, decimal ExpectedUpSeconds,
    decimal ExpectedDownSeconds, bool? ExpectedSlaMet, bool ExpectKumaAgreement);

/// <summary>Deterministic heartbeat fixtures, including full seven-day histories.</summary>
public static class TransitionScenarios
{
    // The interval aligns with Kuma's 168 hourly buckets, so bucket boundaries
    // cannot introduce an extra hour into the comparison.
    public static readonly DateTimeOffset End = new(2026, 11, 25, 11, 59, 59, TimeSpan.Zero);

    public static IEnumerable<TransitionScenario> All()
    {
        yield return FullWeek("equal_interval_60s_down", 60, 1);
        yield return FullWeek("equal_interval_120s_down", 60, 2);
        yield return FullWeek("short_retry_3x20s_down", 20, 3);
        yield return new TransitionScenario("incomplete_history", new UptimeHistory(1, 60, End,
        [
            new(End.AddSeconds(-180), 1, 0),
            new(End.AddSeconds(-120), 0, 60),
            new(End.AddSeconds(-60), 0, 60),
            new(End, 1, 60)
        ]), 60, 120, null, false);
    }

    private static TransitionScenario FullWeek(string name, int retrySeconds, int failures)
    {
        var start = End.AddDays(-7);
        const int outageMinute = 5000;
        var beats = new List<UptimeHeartbeat>();
        for (var minute = 1; minute <= 10080; minute++)
        {
            if (retrySeconds == 20 && minute == outageMinute)
            {
                var before = start.AddMinutes(minute - 1);
                for (var retry = 1; retry <= failures; retry++)
                    beats.Add(new UptimeHeartbeat(before.AddSeconds(retry * retrySeconds), 0, retrySeconds));
            }
            else
            {
                var down = retrySeconds == 60 && minute >= outageMinute && minute < outageMinute + failures;
                beats.Add(new UptimeHeartbeat(start.AddMinutes(minute), down ? 0 : 1, 60));
            }
        }
        var downSeconds = retrySeconds * failures;
        return new TransitionScenario(name, new UptimeHistory(1, 60, End, beats),
            604800 - downSeconds, downSeconds, downSeconds <= 60.48m, retrySeconds == 60);
    }
}
