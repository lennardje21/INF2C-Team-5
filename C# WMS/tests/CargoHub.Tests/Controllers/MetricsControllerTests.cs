using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Monitoring;
using CargoHub.Tests.Unit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CargoHub.Tests.Controllers;

public sealed class MetricsControllerTests
{
    private sealed class FakeKuma(bool fail = false) : IUptimeKumaClient
    {
        public int Calls { get; private set; }
        public Task<UptimeHistory> GetHistoryAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (fail) throw new UptimeUnavailableException("Uptime measurements are temporarily unavailable.");
            return Task.FromResult(UptimeServiceTests.FullWeek(60));
        }
    }

    private static WebApplicationFactory<Program> Factory(IUptimeKumaClient kuma) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUptimeKumaClient>();
                services.AddSingleton(kuma);
            });
        });

    [Theory]
    [InlineData("/metrics/uptime", true, "99.99")]
    [InlineData("/metrics/uptime?SLA=100", false, "100")]
    [InlineData("/metrics/uptime?SLA=0", true, "0")]
    public async Task PublicEndpointReportsTheWeekAndRequestedSla(string path, bool met, string sla)
    {
        await using var factory = Factory(new FakeKuma());
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<UptimeReport>();
        Assert.NotNull(report);
        Assert.Equal(met, report.SlaMet);
        Assert.Equal(decimal.Parse(sla, System.Globalization.CultureInfo.InvariantCulture), report.Sla);
        Assert.Equal(TimeSpan.FromDays(7), report.PeriodEnd - report.PeriodStart);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("100.01")]
    [InlineData("abc")]
    [InlineData("NaN")]
    public async Task InvalidSlaReturns400WithoutCallingKuma(string sla)
    {
        var kuma = new FakeKuma();
        await using var factory = Factory(kuma);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/metrics/uptime?SLA=" + sla);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, kuma.Calls);
    }

    [Fact]
    public async Task UnavailableKumaReturns503WithProblemDetails()
    {
        await using var factory = Factory(new FakeKuma(fail: true));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/metrics/uptime?SLA=99.99");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("temporarily unavailable", problem.Detail);
        Assert.True(problem.Extensions.ContainsKey("traceId"));
    }
}
