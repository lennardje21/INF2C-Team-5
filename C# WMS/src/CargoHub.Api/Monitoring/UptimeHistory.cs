namespace CargoHub.Api.Monitoring;

public sealed record UptimeHeartbeat(DateTimeOffset Time, int Status, decimal Duration);

public sealed record UptimeHistory(
    int MonitorId, int IntervalSeconds, DateTimeOffset RetrievedAt,
    IReadOnlyList<UptimeHeartbeat> Heartbeats);

public interface IUptimeKumaClient
{
    Task<UptimeHistory> GetHistoryAsync(CancellationToken cancellationToken);
}

public sealed class UptimeUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
