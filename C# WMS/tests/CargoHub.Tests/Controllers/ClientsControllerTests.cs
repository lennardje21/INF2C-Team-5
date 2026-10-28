using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Models;
using CargoHub.Tests.Fixtures;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class ClientsControllerTests(ApiFixture fixture)
{
    private const string Url = "/api/v1/clients";
    private readonly HttpClient _client = fixture.Client;

    // A dictionary, so a test can change or add single fields.
    private static Dictionary<string, object> NewClient(string name = "Jumbo Test") => new()
    {
        ["name"] = name,
        ["address"] = "Teststraat 1",
        ["city"] = "Rotterdam",
        ["zipCode"] = "3011AA",
        ["province"] = "Zuid-Holland",
        ["country"] = "Netherlands",
        ["contactName"] = "Test Persoon",
        ["contactPhone"] = "010-1234567",
        ["contactEmail"] = "test@example.com"
    };

    private async Task<Client> CreateAsync(object client)
    {
        var response = await _client.PostAsJsonAsync(Url, client);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Client>())!;
    }

    [Fact]
    public async Task GetClients_ReturnsSeededClients()
    {
        var clients = await _client.GetFromJsonAsync<List<Client>>(Url);

        Assert.True(clients!.Count >= 300);
        Assert.Equal("Jumbo Amersfoort Leusderweg", clients.Single(c => c.Id == 1).Name);
    }

    [Fact]
    public async Task GetClient_ReturnsTheClient()
    {
        var client = await _client.GetFromJsonAsync<Client>($"{Url}/1");

        Assert.Equal("Amersfoort", client!.City);
        Assert.Equal("9704WL", client.ZipCode);
        Assert.Equal(new DateTime(2024, 5, 2, 19, 57, 33, DateTimeKind.Utc), client.CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/orders")]
    public async Task GetRoutes_UnknownClient_ReturnNotFound(string route)
    {
        var response = await _client.GetAsync($"{Url}/999999{route}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetClientOrders_ReturnsOrdersForAllThreeClientFieldsWithItems()
    {
        var expected = await fixture.QueryIntAsync(
            "SELECT COUNT(*) FROM `order` WHERE `client_id` = 1 OR `ship_to_client_id` = 1 OR `bill_to_client_id` = 1");
        // Counted from the database, since other tests in the collection create orders without items.
        var expectedItems = await fixture.QueryIntAsync(
            "SELECT COUNT(*) FROM `order_item` oi JOIN `order` o ON o.`id` = oi.`order_id` " +
            "WHERE o.`client_id` = 1 OR o.`ship_to_client_id` = 1 OR o.`bill_to_client_id` = 1");

        var orders = (await _client.GetFromJsonAsync<List<Order>>($"{Url}/1/orders"))!;

        Assert.Equal(expected, orders.Count);
        Assert.Equal(orders.Count, orders.Select(o => o.Id).Distinct().Count());
        Assert.All(orders, o => Assert.Contains(1, new[] { o.ClientId, o.ShipToClientId, o.BillToClientId }));
        Assert.Equal(expectedItems, orders.Sum(o => o.Items.Count));
    }

    [Fact]
    public async Task GetClientOrders_ClientWithoutOrders_ReturnsEmptyList()
    {
        var created = await CreateAsync(NewClient());

        var orders = await _client.GetFromJsonAsync<List<Order>>($"{Url}/{created.Id}/orders");

        Assert.Empty(orders!);
    }

    [Fact]
    public async Task CreateClient_StoresTheClient()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        var response = await _client.PostAsJsonAsync(Url, NewClient());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<Client>())!;
        Assert.Equal($"{Url}/{created.Id}", response.Headers.Location!.AbsolutePath);
        Assert.True(created.Id > 300);
        Assert.True(created.CreatedAt >= before);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);

        var stored = await _client.GetFromJsonAsync<Client>($"{Url}/{created.Id}");
        Assert.Equal("Jumbo Test", stored!.Name);
        Assert.Equal("test@example.com", stored.ContactEmail);
    }

    [Fact]
    public async Task CreateClient_IgnoresIdAndTimestampsFromTheRequest()
    {
        var client = NewClient();
        client["id"] = 1;
        client["createdAt"] = "2000-01-01T00:00:00Z";

        var created = await CreateAsync(client);

        Assert.NotEqual(1, created.Id);
        Assert.True(created.CreatedAt.Year >= 2026);
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("city", "  ")]
    [InlineData("contactEmail", "")]
    public async Task CreateClient_MissingField_ReturnsBadRequest(string field, string value)
    {
        var client = NewClient();
        client[field] = value;

        var response = await _client.PostAsJsonAsync(Url, client);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateClient_ChangesFieldsButKeepsCreatedAt()
    {
        var created = await CreateAsync(NewClient());
        await Task.Delay(10);

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", NewClient(name: "Jumbo Changed"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await _client.GetFromJsonAsync<Client>($"{Url}/{created.Id}");
        Assert.Equal("Jumbo Changed", updated!.Name);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt > created.UpdatedAt);
    }

    [Fact]
    public async Task UpdateClient_UnknownClient_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"{Url}/999999", NewClient());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateClient_MissingField_ReturnsBadRequest()
    {
        var created = await CreateAsync(NewClient());

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", NewClient(name: ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteClient_RemovesTheClient()
    {
        var created = await CreateAsync(NewClient());

        var response = await _client.DeleteAsync($"{Url}/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{Url}/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteClient_WithOrders_ReturnsConflictAndKeepsTheClient()
    {
        var response = await _client.DeleteAsync($"{Url}/1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"{Url}/1")).StatusCode);
    }

    [Fact]
    public async Task DeleteClient_UnknownClient_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"{Url}/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
