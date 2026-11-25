using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Authentication;
using CargoHub.Api.Controllers;
using CargoHub.Api.Monitoring;
using CargoHub.Api.Services;
using CargoHub.Tests.Fixtures;
using CargoHub.Tests.Unit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class PerformanceMetricsControllerTests(ApiFixture fixture) : IDisposable
{
    private const string Endpoint = "/api/v2/metrics/performance";

    private readonly string directory = PerformanceLogTests.NewTempDirectory();
    private readonly SlowRequestNotifierTests.RecordingEmailService email = new();

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    // The API against the test database, with its own log folder, the slow test endpoint
    // turned on and mail going to `email` instead of an SMTP server.
    private WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:CargoHub"] = fixture.ConnectionString,
                    ["Performance:LogDirectory"] = directory,
                    ["Performance:AlertEmail"] = "team@example.com",
                    ["Performance:TestEndpointEnabled"] = "true",
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(email);
            });
        });

    private static HttpClient AuthorizedClient(WebApplicationFactory<Program> factory, string apiKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyMiddleware.HeaderName, apiKey);
        return client;
    }

    // Known log lines from two containers, in 2020 so the requests of the test itself
    // (logged today) do not end up in the range.
    private void WriteKnownLogLines()
    {
        void Write(string container, string fileName, params string[] lines)
        {
            Directory.CreateDirectory(Path.Combine(directory, container));
            File.WriteAllLines(Path.Combine(directory, container, fileName), lines);
        }

        Write("api-a", "perf-01-01-2020.log",
            """{"endpoint":"/api/v2/items/1","duration_ms":100,"timestamp":"2020-01-01T10:00:00","status_code":200}""",
            """{"endpoint":"/api/v2/items/2","duration_ms":300,"timestamp":"2020-01-01T11:00:00","status_code":200}""",
            """{"endpoint":"/api/v2/items/3","duration_ms":301,"timestamp":"2020-01-01T12:00:00","status_code":500}""");
        Write("api-b", "perf-02-01-2020.log",
            """{"endpoint":"/api/v2/orders","duration_ms":50,"timestamp":"2020-01-02T09:00:00","status_code":404}""",
            """{"endpoint":"/api/v2/orders/1","duration_ms":1000,"timestamp":"2020-01-02T23:59:59","status_code":200}""");
        Write("api-a", "perf-03-01-2020.log",
            """{"endpoint":"/outside-the-range","duration_ms":10,"timestamp":"2020-01-03T00:00:00","status_code":200}""");
    }

    [Fact]
    public async Task SlowTestEndpoint_SendsAMail_AndASecondWithin15MinutesDoesNot()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();

        using var first = await client.GetAsync("/dev/slow?ms=400");
        await first.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var mail = Assert.Single(email.Sent);
        Assert.Equal("team@example.com", mail.To);
        Assert.Contains("/dev/slow", mail.Subject);

        using var second = await client.GetAsync("/dev/slow?ms=400");
        await second.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task EveryRequestIsLogged_IncludingFailedOnes()
    {
        await using (var factory = Factory())
        {
            using var client = factory.CreateClient();
            (await client.GetAsync("/dev/slow?ms=0")).Dispose();
            (await client.GetAsync("/api/v2/does-not-exist")).Dispose();
            (await client.GetAsync("/api/v2/warehouses")).Dispose();
            (await client.GetAsync("/metrics/uptime?SLA=200")).Dispose();
        }
        // Stopping the API has written everything that was still queued.

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var file = Assert.Single(Directory.GetFiles(directory, "perf-*.log", SearchOption.AllDirectories));
        Assert.Equal(PerformanceLog.FileName(today), Path.GetFileName(file));

        var entries = await PerformanceLogTests.CreateLog(directory).ReadAsync(today, today, CancellationToken.None);
        Assert.Equal(
            [("/dev/slow", 200), ("/api/v2/does-not-exist", 404), ("/api/v2/warehouses", 401), ("/metrics/uptime", 400)],
            entries.Select(e => (e.Endpoint, e.StatusCode)));
    }

    [Fact]
    public async Task Performance_ReturnsThePercentageUnder300MsAndTheRequestsInTheRange()
    {
        WriteKnownLogLines();
        await using var factory = Factory();
        using var client = AuthorizedClient(factory, fixture.ReadOnlyApiKey);

        using var response = await client.GetAsync(Endpoint + "?from=2020-01-01&to=2020-01-02");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var report = await response.Content.ReadFromJsonAsync<PerformanceReport>();
        Assert.NotNull(report);
        Assert.Equal(new DateOnly(2020, 1, 1), report.From);
        Assert.Equal(new DateOnly(2020, 1, 2), report.To);
        Assert.Equal(300, report.ThresholdMs);
        Assert.Equal(5, report.TotalRequests);
        // 100, 300 and 50 ms are within the threshold; 301 and 1000 ms are not.
        Assert.Equal(60, report.PercentageUnderThreshold);
        Assert.Equal(
            ["/api/v2/items/1", "/api/v2/items/2", "/api/v2/items/3", "/api/v2/orders", "/api/v2/orders/1"],
            report.Requests.Select(r => r.Endpoint));
        Assert.Equal(404, report.Requests[3].StatusCode);
    }

    [Fact]
    public async Task Performance_IncludesTheWholeDay_WhenFromAndToAreTheSame()
    {
        WriteKnownLogLines();
        await using var factory = Factory();
        using var client = AuthorizedClient(factory, fixture.ReadOnlyApiKey);

        var report = await client.GetFromJsonAsync<PerformanceReport>(Endpoint + "?from=2020-01-02&to=2020-01-02");

        Assert.NotNull(report);
        Assert.Equal(2, report.TotalRequests);
        Assert.Equal(50, report.PercentageUnderThreshold);
    }

    [Fact]
    public async Task Performance_ReturnsNoPercentage_WhenThereAreNoRequests()
    {
        await using var factory = Factory();
        using var client = AuthorizedClient(factory, fixture.ReadOnlyApiKey);

        var report = await client.GetFromJsonAsync<PerformanceReport>(Endpoint + "?from=2019-01-01&to=2019-01-31");

        Assert.NotNull(report);
        Assert.Equal(0, report.TotalRequests);
        Assert.Null(report.PercentageUnderThreshold);
        Assert.Empty(report.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?from=2020-01-01")]
    [InlineData("?to=2020-01-01")]
    [InlineData("?from=2020-13-01&to=2020-12-31")]
    [InlineData("?from=01-01-2020&to=02-01-2020")]
    [InlineData("?from=abc&to=2020-01-01")]
    [InlineData("?from=2020-01-01T10:00:00&to=2020-01-02")]
    [InlineData("?from=2020-01-02&to=2020-01-01")]
    public async Task Performance_InvalidOrReversedDatesReturn400(string query)
    {
        await using var factory = Factory();
        using var client = AuthorizedClient(factory, fixture.ReadOnlyApiKey);

        using var response = await client.GetAsync(Endpoint + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Performance_WithoutApiKeyReturns401()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Endpoint + "?from=2020-01-01&to=2020-01-02");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
