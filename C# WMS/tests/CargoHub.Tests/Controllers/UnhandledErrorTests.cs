using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CargoHub.Tests.Controllers;

public sealed class UnhandledErrorTests
{
    [Theory]
    [InlineData("application/json")]
    [InlineData("text/html")]
    public async Task UnexpectedException_ReturnsSafeProblemWithTraceId(string accept)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services => services.AddControllers()
                .AddApplicationPart(typeof(ErrorProbeController).Assembly));
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd(accept);
        using var response = await client.GetAsync("/__tests/error");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(500, problem.Status);
        Assert.Equal("/__tests/error", problem.Instance);
        Assert.Contains("traceId", problem.Detail);
        Assert.True(problem.Extensions.ContainsKey("traceId"));
        Assert.DoesNotContain("sensitive", await response.Content.ReadAsStringAsync());
    }
}

// Loaded only by the factory above, never by the production application.
[ApiController]
public sealed class ErrorProbeController : ControllerBase
{
    [HttpGet("/__tests/error")]
    public IActionResult Throw() => throw new InvalidOperationException("sensitive internal information");
}
