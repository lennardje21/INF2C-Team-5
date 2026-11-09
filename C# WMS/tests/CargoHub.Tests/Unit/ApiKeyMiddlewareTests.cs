using System.Text.Json;
using CargoHub.Api.Authentication;
using CargoHub.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CargoHub.Tests.Unit;

public sealed class ApiKeyMiddlewareTests
{
    [Theory]
    [InlineData("/api/v2/unknown", 404)]
    [InlineData("/api/v2/suppliers", 405)]
    public async Task RoutingError_ReachesRoutingInsteadOfReturningUnauthorized(string path, int status)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (status == 405)
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
                EndpointMetadataCollection.Empty, "Method not allowed"));
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(httpContext =>
        {
            nextCalled = true;
            httpContext.Response.StatusCode = status;
            return Task.CompletedTask;
        }, NullLogger<ApiKeyMiddleware>.Instance);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var users = CreateUsersService(cache);

        await middleware.InvokeAsync(context, users);

        Assert.True(nextCalled);
        Assert.Equal(status, context.Response.StatusCode);
    }

    [Fact]
    public async Task MatchedApiRoute_WithoutKey_ReturnsDescriptiveUnauthorizedAndStopsPipeline()
    {
        using var services = new ServiceCollection().AddOptions().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/api/v2/suppliers";
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new ControllerActionDescriptor()), "Suppliers"));
        await using var body = new MemoryStream();
        context.Response.Body = body;
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, NullLogger<ApiKeyMiddleware>.Instance);
        using var cache = new MemoryCache(new MemoryCacheOptions());

        await middleware.InvokeAsync(context, CreateUsersService(cache));

        Assert.False(nextCalled);
        Assert.Equal(401, context.Response.StatusCode);
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        Assert.Contains("API_KEY", json.RootElement.GetProperty("detail").GetString());
        Assert.Equal("/api/v2/suppliers", json.RootElement.GetProperty("instance").GetString());
    }

    // An empty configuration makes any accidental database access fail.
    private static ApiUsersService CreateUsersService(IMemoryCache cache) =>
        new(new MySqlConnectionFactory(new ConfigurationBuilder().Build()), cache);
}
