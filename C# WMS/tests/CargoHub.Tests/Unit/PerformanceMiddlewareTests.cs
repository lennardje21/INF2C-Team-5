using CargoHub.Api.Monitoring;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static CargoHub.Tests.Unit.SlowRequestNotifierTests;

namespace CargoHub.Tests.Unit;

public sealed class PerformanceMiddlewareTests : IDisposable
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private readonly string directory = PerformanceLogTests.NewTempDirectory();
    private readonly RecordingEmailService email = new();

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    // Runs one request through the middleware and returns what ended up in the log.
    private async Task<List<PerformanceEntry>> RunAsync(string path, RequestDelegate next)
    {
        var log = PerformanceLogTests.CreateLog(directory);
        var time = new ManualTime(Now);
        var notifier = new SlowRequestNotifier(email,
            Options.Create(new PerformanceOptions { AlertEmail = "team@example.com" }),
            time, NullLogger<SlowRequestNotifier>.Instance);
        var middleware = new PerformanceMiddleware(next, log, notifier, time);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;

        await log.StartAsync(CancellationToken.None);
        try
        {
            await middleware.InvokeAsync(context);
        }
        finally
        {
            await log.StopAsync(CancellationToken.None);
        }
        return await log.ReadAsync(Today, Today, CancellationToken.None);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task LogsEveryRequestWithItsStatusCode(int status)
    {
        var entries = await RunAsync("/api/v2/items/5", context =>
        {
            context.Response.StatusCode = status;
            return Task.CompletedTask;
        });

        var entry = Assert.Single(entries);
        Assert.Equal("/api/v2/items/5", entry.Endpoint);
        Assert.Equal(status, entry.StatusCode);
        Assert.Equal("2026-11-10T14:30:00", entry.Timestamp);
        Assert.InRange(entry.DurationMs, 0, 300);
    }

    [Fact]
    public async Task UnhandledException_IsLoggedAs500AndRethrown()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunAsync("/api/v2/items", _ => throw new InvalidOperationException()));

        var entry = Assert.Single(await PerformanceLogTests.CreateLog(directory)
            .ReadAsync(Today, Today, CancellationToken.None));
        Assert.Equal(500, entry.StatusCode);
    }

    [Fact]
    public async Task MeasuresHowLongTheRequestTook_AndMailsWhenItIsSlow()
    {
        var entries = await RunAsync("/api/v2/items", async _ => await Task.Delay(350));

        Assert.True(Assert.Single(entries).DurationMs >= 340);
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task FastRequest_SendsNoMail()
    {
        await RunAsync("/api/v2/items", _ => Task.CompletedTask);

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task HealthChecks_AreNotLogged()
    {
        var nextCalled = false;
        var entries = await RunAsync("/health", _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        Assert.True(nextCalled);
        Assert.Empty(entries);
    }
}
