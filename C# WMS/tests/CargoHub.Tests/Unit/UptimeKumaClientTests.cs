using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CargoHub.Api.Monitoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CargoHub.Tests.Unit;

public sealed class UptimeKumaClientTests
{
    // A small Socket.IO peer exercises the real .NET client over a loopback
    // WebSocket, including authentication, arguments and acknowledgements.
    private sealed class KumaPeer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public ConcurrentQueue<JsonElement> Events { get; } = new();
        public bool RejectBeats { get; set; }
        public bool IgnoreBeats { get; set; }
        public string Url => _app.Urls.Single();

        public KumaPeer()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            _app = builder.Build();
            _app.UseWebSockets();
            _app.Map("/socket.io", HandleAsync);
        }

        public Task StartAsync() => _app.StartAsync();

        private async Task HandleAsync(HttpContext context)
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await SendAsync(socket, "0{\"sid\":\"test\",\"upgrades\":[],\"pingInterval\":25000,\"pingTimeout\":20000,\"maxPayload\":1000000}");
            var buffer = new byte[8192];
            while (socket.State == WebSocketState.Open)
            {
                var received = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (received.MessageType == WebSocketMessageType.Close)
                    break;
                var message = Encoding.UTF8.GetString(buffer, 0, received.Count);
                if (message.StartsWith("40"))
                {
                    await SendAsync(socket, "40{\"sid\":\"authenticated-test\"}");
                    continue;
                }
                if (message == "41") break;
                if (message == "2")
                {
                    await SendAsync(socket, "3");
                    continue;
                }
                if (!message.StartsWith("42")) continue;

                var start = message.IndexOf('[');
                var acknowledgement = message[2..start];
                using var json = JsonDocument.Parse(message[start..]);
                var data = json.RootElement.Clone();
                Events.Enqueue(data);
                var name = data[0].GetString();
                if (name == "getMonitorBeats" && IgnoreBeats) continue;
                object response = name switch
                {
                    "getMonitor" => new { ok = true, monitor = new { interval = 60 } },
                    "getMonitorBeats" when RejectBeats => new { ok = false, msg = "secret upstream error" },
                    "getMonitorBeats" => new
                    {
                        ok = true,
                        data = new[] { new { time = "2026-11-20 12:00:00.000", status = 1, duration = 60 } }
                    },
                    _ => new { ok = true }
                };
                await SendAsync(socket, "43" + acknowledgement + "[" + JsonSerializer.Serialize(response) + "]");
            }
        }

        private static Task SendAsync(WebSocket socket, string message) =>
            socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, CancellationToken.None);

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }

    [Theory]
    [InlineData(false, "login")]
    [InlineData(true, "loginByToken")]
    public async Task Reads168HoursThroughSocketIoAndCachesConcurrentRequests(bool tokenLogin, string loginEvent)
    {
        await using var peer = new KumaPeer();
        await peer.StartAsync();
        using var client = new UptimeKumaClient(Options.Create(new UptimeKumaOptions
        {
            Url = peer.Url, MonitorId = 4, Username = "test-user", Password = "test-password",
            Token = tokenLogin ? "test-token" : ""
        }), TimeProvider.System);

        var histories = await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => client.GetHistoryAsync(CancellationToken.None)));

        Assert.Equal(3, peer.Events.Count);
        var events = peer.Events.ToArray();
        Assert.Equal(loginEvent, events[0][0].GetString());
        Assert.Equal("getMonitor", events[1][0].GetString());
        Assert.Equal("getMonitorBeats", events[2][0].GetString());
        Assert.Equal(4, events[2][1].GetInt32());
        Assert.Equal(168, events[2][2].GetInt32());
        var beat = Assert.Single(histories[0].Heartbeats);
        Assert.Equal(UptimeServiceTests.Now, beat.Time);
        Assert.Equal(60, beat.Duration);
        Assert.Same(histories[0], histories[1]);
    }

    [Fact]
    public async Task MissingConfigurationIsUnavailable()
    {
        using var client = new UptimeKumaClient(Options.Create(new UptimeKumaOptions()), TimeProvider.System);
        await Assert.ThrowsAsync<UptimeUnavailableException>(() => client.GetHistoryAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpstreamRejectionOrMissingAcknowledgementReturnsSafeFailure(bool timeout)
    {
        await using var peer = new KumaPeer { RejectBeats = !timeout, IgnoreBeats = timeout };
        await peer.StartAsync();
        using var client = new UptimeKumaClient(Options.Create(new UptimeKumaOptions
        {
            Url = peer.Url, MonitorId = 4, Token = "test-token", TimeoutSeconds = 1
        }), TimeProvider.System);

        var error = await Assert.ThrowsAsync<UptimeUnavailableException>(() => client.GetHistoryAsync(CancellationToken.None));
        Assert.Contains("temporarily unavailable", error.Message);
        Assert.DoesNotContain("secret", error.Message);
    }
}
