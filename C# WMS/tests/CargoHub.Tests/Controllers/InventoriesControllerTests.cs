using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Models;
using CargoHub.Tests.Fixtures;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class InventoriesControllerTests(ApiFixture fixture)
{
    private const string Url = "/api/v2/inventories";
    private readonly HttpClient _client = fixture.Client;

    // A dictionary, so a test can change or add single fields.
    private static Dictionary<string, object> NewInventory(int itemId, int locationId, int onHand = 10) => new()
    {
        ["itemId"] = itemId,
        ["locationId"] = locationId,
        ["quantityOnHand"] = onHand,
        ["quantityExpected"] = 5,
        ["quantityOrdered"] = 3,
        ["quantityAllocated"] = 2
    };

    // An existing item and a location where that item has no inventory yet.
    private async Task<(int ItemId, int LocationId)> FreeKeyAsync()
    {
        var itemId = await fixture.QueryIntAsync("SELECT MIN(`id`) FROM `item`");
        var locationId = await fixture.QueryIntAsync("""
            SELECT MIN(l.`id`) FROM `location` l
            WHERE NOT EXISTS (
                SELECT 1 FROM `inventory` i WHERE i.`item_id` = @item AND i.`location_id` = l.`id`)
            """, ("@item", itemId));
        return (itemId, locationId);
    }

    // An inventory that is already in the seed data.
    private async Task<(int ItemId, int LocationId)> SeededKeyAsync()
    {
        var itemId = await fixture.QueryIntAsync("SELECT `item_id` FROM `inventory` LIMIT 1");
        var locationId = await fixture.QueryIntAsync(
            "SELECT `location_id` FROM `inventory` WHERE `item_id` = @item LIMIT 1", ("@item", itemId));
        return (itemId, locationId);
    }

    private async Task<Inventory> CreateAsync(object inventory)
    {
        var response = await _client.PostAsJsonAsync(Url, inventory);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Inventory>())!;
    }

    [Fact]
    public async Task GetInventories_ReturnsSeededInventories()
    {
        var inventories = await _client.GetFromJsonAsync<List<Inventory>>(Url);
        var expected = await fixture.QueryIntAsync("SELECT COUNT(*) FROM `inventory`");

        Assert.NotNull(inventories);
        Assert.Equal(expected, inventories.Count);
        Assert.NotEmpty(inventories);
    }

    [Fact]
    public async Task GetInventory_ReturnsTheInventory()
    {
        var (itemId, locationId) = await SeededKeyAsync();
        var expectedOnHand = await fixture.QueryIntAsync(
            "SELECT `quantity_on_hand` FROM `inventory` WHERE `item_id` = @item AND `location_id` = @location",
            ("@item", itemId), ("@location", locationId));

        var inventory = await _client.GetFromJsonAsync<Inventory>($"{Url}/{itemId}/{locationId}");

        Assert.NotNull(inventory);
        Assert.Equal(itemId, inventory.ItemId);
        Assert.Equal(locationId, inventory.LocationId);
        Assert.Equal(expectedOnHand, inventory.QuantityOnHand);
    }

    [Fact]
    public async Task GetInventory_UnknownInventory_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"{Url}/999999/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateInventory_StoresTheInventory()
    {
        var (itemId, locationId) = await FreeKeyAsync();
        var before = DateTime.UtcNow.AddSeconds(-1);

        var response = await _client.PostAsJsonAsync(Url, NewInventory(itemId, locationId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<Inventory>())!;
        Assert.Equal($"{Url}/{itemId}/{locationId}", response.Headers.Location!.AbsolutePath);
        Assert.True(created.CreatedAt >= before);
        Assert.InRange(
            created.UpdatedAt,
            created.CreatedAt.AddTicks(-10),
            created.CreatedAt.AddTicks(10));

        var stored = await _client.GetFromJsonAsync<Inventory>($"{Url}/{itemId}/{locationId}");
        Assert.Equal(10, stored!.QuantityOnHand);
        Assert.Equal(5, stored.QuantityExpected);
        Assert.Equal(3, stored.QuantityOrdered);
        Assert.Equal(2, stored.QuantityAllocated);
    }

    [Fact]
    public async Task CreateInventory_IgnoresTimestampsFromTheRequest()
    {
        var (itemId, locationId) = await FreeKeyAsync();
        var inventory = NewInventory(itemId, locationId);
        inventory["createdAt"] = "2000-01-01T00:00:00Z";

        var created = await CreateAsync(inventory);

        Assert.NotEqual(2000, created.CreatedAt.Year);
    }

    [Fact]
    public async Task CreateInventory_AlreadyExists_ReturnsConflict()
    {
        var (itemId, locationId) = await SeededKeyAsync();

        var response = await _client.PostAsJsonAsync(Url, NewInventory(itemId, locationId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateInventory_UnknownItem_ReturnsBadRequest()
    {
        var (_, locationId) = await FreeKeyAsync();

        var response = await _client.PostAsJsonAsync(Url, NewInventory(999999, locationId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateInventory_UnknownLocation_ReturnsBadRequest()
    {
        var (itemId, _) = await FreeKeyAsync();

        var response = await _client.PostAsJsonAsync(Url, NewInventory(itemId, 999999));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("quantityOnHand")]
    [InlineData("quantityExpected")]
    [InlineData("quantityOrdered")]
    [InlineData("quantityAllocated")]
    public async Task CreateInventory_NegativeQuantity_ReturnsBadRequest(string field)
    {
        var (itemId, locationId) = await FreeKeyAsync();
        var inventory = NewInventory(itemId, locationId);
        inventory[field] = -1;

        var response = await _client.PostAsJsonAsync(Url, inventory);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateInventory_ChangesQuantitiesButKeepsCreatedAt()
    {
        var (itemId, locationId) = await FreeKeyAsync();
        var created = await CreateAsync(NewInventory(itemId, locationId));
        await Task.Delay(10);

        var response = await _client.PutAsJsonAsync(
            $"{Url}/{itemId}/{locationId}", NewInventory(itemId, locationId, onHand: 99));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await _client.GetFromJsonAsync<Inventory>($"{Url}/{itemId}/{locationId}");
        Assert.Equal(99, updated!.QuantityOnHand);
        Assert.InRange(
            updated.CreatedAt,
            created.CreatedAt.AddTicks(-10),
            created.CreatedAt.AddTicks(10));
        Assert.True(updated.UpdatedAt > created.UpdatedAt);
    }

    [Fact]
    public async Task UpdateInventory_UsesTheKeysFromTheUrlNotTheBody()
    {
        var (itemId, locationId) = await FreeKeyAsync();
        await CreateAsync(NewInventory(itemId, locationId));

        var response = await _client.PutAsJsonAsync(
            $"{Url}/{itemId}/{locationId}", NewInventory(999999, 999999, onHand: 42));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await _client.GetFromJsonAsync<Inventory>($"{Url}/{itemId}/{locationId}");
        Assert.Equal(itemId, updated!.ItemId);
        Assert.Equal(locationId, updated.LocationId);
        Assert.Equal(42, updated.QuantityOnHand);
    }

    [Fact]
    public async Task UpdateInventory_UnknownInventory_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"{Url}/999999/999999", NewInventory(999999, 999999));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateInventory_NegativeQuantity_ReturnsBadRequest()
    {
        var (itemId, locationId) = await FreeKeyAsync();
        await CreateAsync(NewInventory(itemId, locationId));

        var response = await _client.PutAsJsonAsync(
            $"{Url}/{itemId}/{locationId}", NewInventory(itemId, locationId, onHand: -1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteInventory_RemovesTheInventory()
    {
        var (itemId, locationId) = await FreeKeyAsync();
        await CreateAsync(NewInventory(itemId, locationId));

        var response = await _client.DeleteAsync($"{Url}/{itemId}/{locationId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync($"{Url}/{itemId}/{locationId}")).StatusCode);
    }

    [Fact]
    public async Task DeleteInventory_UnknownInventory_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"{Url}/999999/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}