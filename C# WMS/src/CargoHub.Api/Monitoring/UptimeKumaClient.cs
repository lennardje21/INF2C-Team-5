using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SocketIOClient;
using SocketIOClient.Common;

namespace CargoHub.Api.Monitoring;

/// <summary>Reads authenticated monitor data through Kuma's Socket.IO acknowledgements.</summary>
public sealed class UptimeKumaClient(
    IOptions<UptimeKumaOptions> options, TimeProvider clock) : IUptimeKumaClient, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UptimeHistory? _cached;

    public async Task<UptimeHistory> GetHistoryAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null && clock.GetUtcNow() - _cached.RetrievedAt < TimeSpan.FromSeconds(30))
                return _cached;

            var config = options.Value;
            if (!Uri.TryCreate(config.Url, UriKind.Absolute, out var url) ||
                url.Scheme is not ("http" or "https") || config.MonitorId <= 0 ||
                config.TimeoutSeconds is < 1 or > 60 ||
                (string.IsNullOrWhiteSpace(config.Token) &&
                 (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))))
                throw new UptimeUnavailableException("Uptime Kuma is not configured. Contact the administrator.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));
            using var socket = new SocketIO(url, new SocketIOOptions
            {
                Reconnection = false,
                ConnectionTimeout = TimeSpan.FromSeconds(config.TimeoutSeconds),
                Transport = TransportProtocol.WebSocket
            });
            try
            {
                await socket.ConnectAsync(timeout.Token);
                if (!string.IsNullOrWhiteSpace(config.Token))
                    await CallAsync(socket, "loginByToken", [config.Token], timeout.Token);
                else
                    await CallAsync(socket, "login", [new { username = config.Username, password = config.Password }], timeout.Token);

                // getMonitor checks monitor ownership and supplies the heartbeat interval.
                var monitor = await CallAsync(socket, "getMonitor", [config.MonitorId], timeout.Token);
                var interval = monitor.GetProperty("monitor").GetProperty("interval").GetInt32();
                if (interval is < 1 or > 86400)
                    throw new JsonException("Invalid monitor interval.");

                var response = await CallAsync(socket, "getMonitorBeats", [config.MonitorId, 168], timeout.Token);
                var beats = response.GetProperty("data").EnumerateArray().Select(ParseHeartbeat).ToArray();
                _cached = new UptimeHistory(config.MonitorId, interval, clock.GetUtcNow(), beats);
                return _cached;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                // Neither upstream error messages nor credentials belong in a public response.
                throw new UptimeUnavailableException("Uptime measurements are temporarily unavailable. Try again later.", error);
            }
            finally
            {
                if (socket.Connected)
                {
                    using var disconnectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try { await socket.DisconnectAsync(disconnectTimeout.Token); }
                    catch (Exception) { /* Disposing the client also releases the transport. */ }
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<JsonElement> CallAsync(
        SocketIO socket, string eventName, object[] arguments, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await socket.EmitAsync(eventName, arguments, acknowledgement =>
        {
            try { completion.TrySetResult(acknowledgement.GetValue<JsonElement>(0).Clone()); }
            catch (Exception error) { completion.TrySetException(error); }
            return Task.CompletedTask;
        }, cancellationToken);
        var response = await completion.Task.WaitAsync(cancellationToken);
        if (!response.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            throw new UptimeUnavailableException("Uptime Kuma rejected the monitoring request.");
        return response;
    }

    private static UptimeHeartbeat ParseHeartbeat(JsonElement value)
    {
        // Kuma serialises UTC timestamps with a space, without a timezone suffix.
        if (!DateTimeOffset.TryParse(value.GetProperty("time").GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
            throw new JsonException("Invalid heartbeat timestamp.");
        var status = value.GetProperty("status").GetInt32();
        var duration = value.GetProperty("duration").GetDecimal();
        if (status is < 0 or > 3 || duration < 0)
            throw new JsonException("Invalid heartbeat status or duration.");
        return new UptimeHeartbeat(time, status, duration);
    }

    public void Dispose() => _gate.Dispose();
}
