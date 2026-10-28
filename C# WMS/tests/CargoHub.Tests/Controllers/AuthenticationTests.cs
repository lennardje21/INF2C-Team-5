using System.Net;
using CargoHub.Api.Authentication;
using CargoHub.Tests.Fixtures;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class AuthenticationTests(ApiFixture fixture)
{
    [Fact]
    public async Task MissingApiKey_ReturnsUnauthorized()
    {
        using var client = fixture.CreateUnauthenticatedClient();
        using var response = await client.GetAsync("/api/v1/warehouses");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidApiKey_ReturnsUnauthorized()
    {
        using var client = fixture.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Add(ApiKeyMiddleware.HeaderName, "invalid-test-key");
        using var response = await client.GetAsync("/api/v1/warehouses");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyApiKey_CanReadButCannotCreate()
    {
        using var client = fixture.CreateUnauthenticatedClient();
        var key = await fixture.QueryStringAsync(
            "SELECT `api_key` FROM `user` WHERE `app` = @app", ("@app", "analytics_dashboard"));
        client.DefaultRequestHeaders.Add(ApiKeyMiddleware.HeaderName, key);

        using var read = await client.GetAsync("/api/v1/warehouses");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var create = await client.PostAsync("/api/v1/warehouses", null);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }
}
