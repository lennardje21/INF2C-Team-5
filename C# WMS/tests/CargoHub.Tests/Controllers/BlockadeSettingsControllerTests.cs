using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Authentication;
using CargoHub.Api.Models;
using CargoHub.Api.Settings;
using CargoHub.Tests.Fixtures;

namespace CargoHub.Tests.Controllers;

// The blockade settings are open to an admin or higher; a floormanager and a user get a 403.
// The accounts are created through the API below the fixture's superroot.
[Collection("Api")]
public sealed class BlockadeSettingsControllerTests(ApiFixture fixture) : IAsyncLifetime, IDisposable
{
    private const string Url = "/api/v2/blockade-settings";
    private const int Warehouse = 1;

    private readonly List<HttpClient> _clients = [];

    // Every test starts and ends without stored settings, so the tests do not depend on each other.
    public Task InitializeAsync() => fixture.ExecuteAsync("DELETE FROM `blockade_setting`");

    // The accounts of these tests are removed again, so other tests do not find them.
    public async Task DisposeAsync()
    {
        await fixture.ExecuteAsync("DELETE FROM `blockade_setting`");
        await fixture.ExecuteAsync("DELETE FROM `account` WHERE `email` LIKE 'blockade-%@cargohub.nl'");
    }

    public void Dispose()
    {
        foreach (var client in _clients)
            client.Dispose();
    }

    private HttpClient ClientWithKey(string apiKey)
    {
        var client = fixture.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Add(ApiKeyMiddleware.HeaderName, apiKey);
        _clients.Add(client);
        return client;
    }

    // Creates an account through the API as the given creator and returns it with a client
    // that sends its API key. Only the first account in a chain gets a warehouse: the others
    // belong to the warehouse of the account that creates them.
    private async Task<HttpClient> CreateAsync(HttpClient creator, string role, int? warehouse = null)
    {
        var response = await creator.PostAsJsonAsync("/api/v2/accounts", new Dictionary<string, object?>
        {
            ["role"] = role,
            ["name"] = $"Test {role}",
            ["email"] = $"blockade-{Guid.NewGuid():N}@cargohub.nl",
            ["warehouseId"] = warehouse,
            ["app"] = "integration-tests"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreatedAccount>())!;
        return ClientWithKey(created.ApiKey);
    }

    // A client for a new account with this role. Every role only creates the roles below it:
    // superroot creates root, root creates admin, admin creates floormanager and user. So the
    // account is made at the end of that chain.
    private async Task<HttpClient> ClientForRoleAsync(string role)
    {
        var root = await CreateAsync(fixture.Client, "root", Warehouse);
        if (role == "root")
            return root;

        var admin = await CreateAsync(root, "admin");
        if (role == "admin")
            return admin;

        return await CreateAsync(admin, role);
    }

    private static BlockadeSettings Settings(int order, int shipment, int transfer, int interval) => new()
    {
        OrderMaxMinutes = order,
        ShipmentMaxMinutes = shipment,
        TransferMaxMinutes = transfer,
        CheckIntervalMinutes = interval
    };

    [Fact]
    public async Task Get_WithoutStoredSettings_ReturnsTheDefaults()
    {
        var settings = await fixture.Client.GetFromJsonAsync<BlockadeSettings>(Url);

        Assert.True(settings!.OrderMaxMinutes > 0);
        Assert.True(settings.ShipmentMaxMinutes > 0);
        Assert.True(settings.TransferMaxMinutes > 0);
        Assert.True(settings.CheckIntervalMinutes > 0);
    }

    [Fact]
    public async Task Put_ByAdmin_ChangesTheSettingsAndKeepsThem()
    {
        var admin = await ClientForRoleAsync("admin");

        var response = await admin.PutAsJsonAsync(Url, Settings(30, 60, 90, 5));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await admin.GetFromJsonAsync<BlockadeSettings>(Url);
        Assert.Equal(30, stored!.OrderMaxMinutes);
        Assert.Equal(60, stored.ShipmentMaxMinutes);
        Assert.Equal(90, stored.TransferMaxMinutes);
        Assert.Equal(5, stored.CheckIntervalMinutes);
        Assert.Equal(1, await fixture.QueryIntAsync("SELECT COUNT(*) FROM `blockade_setting`"));
    }

    [Fact]
    public async Task Put_ByRoot_Works_AndAChangeReplacesTheEarlierOne()
    {
        var root = await ClientForRoleAsync("root");

        Assert.Equal(HttpStatusCode.OK, (await root.PutAsJsonAsync(Url, Settings(30, 60, 90, 5))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await root.PutAsJsonAsync(Url, Settings(45, 75, 105, 10))).StatusCode);

        var stored = await root.GetFromJsonAsync<BlockadeSettings>(Url);
        Assert.Equal(45, stored!.OrderMaxMinutes);
        Assert.Equal(10, stored.CheckIntervalMinutes);
        Assert.Equal(1, await fixture.QueryIntAsync("SELECT COUNT(*) FROM `blockade_setting`"));
    }

    [Theory]
    [InlineData(0, 60, 90, 5, "OrderMaxMinutes")]
    [InlineData(30, -1, 90, 5, "ShipmentMaxMinutes")]
    [InlineData(30, 60, 525601, 5, "TransferMaxMinutes")]
    [InlineData(30, 60, 90, 0, "CheckIntervalMinutes")]
    [InlineData(30, 60, 90, 1441, "CheckIntervalMinutes")]
    public async Task Put_WithAnInvalidValue_Returns400AndNamesTheValue(
        int order, int shipment, int transfer, int interval, string expectedName)
    {
        var response = await fixture.Client.PutAsJsonAsync(Url, Settings(order, shipment, transfer, interval));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedName, await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await fixture.QueryIntAsync("SELECT COUNT(*) FROM `blockade_setting`"));
    }

    [Theory]
    [InlineData("floormanager")]
    [InlineData("user")]
    public async Task GetAndPut_ByALowerRole_Return403(string role)
    {
        var client = await ClientForRoleAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(Url, Settings(30, 60, 90, 5))).StatusCode);
        Assert.Equal(0, await fixture.QueryIntAsync("SELECT COUNT(*) FROM `blockade_setting`"));
    }

    [Fact]
    public async Task Get_WithoutAnApiKey_Returns401()
    {
        using var client = fixture.CreateUnauthenticatedClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
    }
}
