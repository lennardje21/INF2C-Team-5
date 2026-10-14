using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CargoHub.Api.Models;
using CargoHub.Tests.Fixtures;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class ShipmentsControllerTests(ApiFixture fixture)
{
    private const string Url = "/api/v1/shipments";
    private readonly HttpClient _client = fixture.Client;

    // Tests that change data use their own shipments and items, so they do not
    // depend on each other. Creating a shipment does not change the inventory.
    private static object NewShipment(int orderId = 3, string? reference = "SHP-TEST",
        string shipmentDate = "2026-10-12T10:00:00Z", params object[] items) => new
    {
        reference,
        orderId,
        shipmentDate,
        shipmentType = "Outgoing",
        shipmentStatus = "Scheduled",
        carrierName = "DHL Parcel",
        shippingMethod = "Express",
        paymentType = "Manual",
        items
    };

    private static object Item(int itemId, int amount) => new { itemId, amount };

    private async Task<Shipment> CreateAsync(object shipment)
    {
        var response = await _client.PostAsJsonAsync(Url, shipment);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Shipment>())!;
    }

    private Task<int> QuantityOrderedAsync(int itemId) => fixture.QueryIntAsync(
        "SELECT `quantity_ordered` FROM `inventory` WHERE `item_id` = @id " +
        "ORDER BY `quantity_on_hand` DESC, `location_id` LIMIT 1", ("@id", itemId));

    [Fact]
    public async Task GetShipments_ReturnsSeededShipmentsWithItems()
    {
        var shipments = await _client.GetFromJsonAsync<List<Shipment>>(Url);

        Assert.True(shipments!.Count >= 6132);
        var first = shipments.Single(s => s.Id == 1);
        Assert.Equal("SHP-000001", first.Reference);
        Assert.Equal(82, Assert.Single(first.Items).ItemId);
    }

    [Fact]
    public async Task GetShipment_ReturnsShipmentWithItems()
    {
        var shipment = await _client.GetFromJsonAsync<Shipment>($"{Url}/1");

        Assert.Equal(1, shipment!.OrderId);
        Assert.Equal(new DateTime(2025, 3, 5, 18, 51, 53, DateTimeKind.Utc), shipment.ShipmentDate);
        var item = Assert.Single(shipment.Items);
        Assert.Equal((82, 6), (item.ItemId, item.Amount));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/orders")]
    [InlineData("/items")]
    public async Task GetRoutes_UnknownShipment_ReturnNotFound(string route)
    {
        var response = await _client.GetAsync($"{Url}/999999{route}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetShipmentOrders_ReturnsTheOrderId()
    {
        Assert.Equal(new[] { 1 }, await _client.GetFromJsonAsync<int[]>($"{Url}/1/orders"));
    }

    [Fact]
    public async Task GetShipmentItems_ReturnsTheItems()
    {
        var items = await _client.GetFromJsonAsync<List<ShipmentItem>>($"{Url}/2/items");

        Assert.Equal(await fixture.QueryIntAsync(
            "SELECT COUNT(*) FROM `shipment_item` WHERE `shipment_id` = 2"), items!.Count);
        Assert.True(items.Count > 1);
        Assert.All(items, i => Assert.Equal(2, i.ShipmentId));
    }

    [Fact]
    public async Task CreateShipment_StoresShipmentAndItems()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        var response = await _client.PostAsJsonAsync(Url, NewShipment(items: Item(10, 2)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<Shipment>())!;
        Assert.Equal($"{Url}/{created.Id}", response.Headers.Location!.AbsolutePath);
        Assert.True(created.Id > 6132);
        Assert.True(created.CreatedAt >= before);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);

        var stored = await _client.GetFromJsonAsync<Shipment>($"{Url}/{created.Id}");
        Assert.Equal("SHP-TEST", stored!.Reference);
        var item = Assert.Single(stored.Items);
        Assert.Equal((10, 2), (item.ItemId, item.Amount));
    }

    [Fact]
    public async Task CreateShipment_IgnoresIdAndTimestampsFromTheRequest()
    {
        var created = await CreateAsync(new
        {
            id = 1,
            createdAt = "2000-01-01T00:00:00Z",
            reference = "SHP-TEST",
            orderId = 3,
            shipmentDate = "2026-10-12T10:00:00Z",
            shipmentType = "Outgoing",
            shipmentStatus = "Scheduled",
            carrierName = "DHL Parcel",
            shippingMethod = "Express",
            paymentType = "Manual"
        });

        Assert.NotEqual(1, created.Id);
        Assert.True(created.CreatedAt.Year >= 2026);
    }

    [Fact]
    public async Task CreateShipment_DateWithoutTimeZone_IsStoredAsUtc()
    {
        var created = await CreateAsync(NewShipment(shipmentDate: "2026-10-12T10:00:00"));

        var json = await _client.GetStringAsync($"{Url}/{created.Id}");
        Assert.Equal("2026-10-12T10:00:00Z",
            JsonDocument.Parse(json).RootElement.GetProperty("shipmentDate").GetString());
    }

    public static TheoryData<object> InvalidShipments => new()
    {
        NewShipment(orderId: 999999),
        NewShipment(reference: null),
        NewShipment(reference: "  "),
        NewShipment(items: Item(999999, 1)),
        NewShipment(items: Item(10, 0)),
        NewShipment(items: [Item(10, 1), Item(10, 2)])
    };

    [Theory]
    [MemberData(nameof(InvalidShipments))]
    public async Task CreateShipment_InvalidInput_ReturnsBadRequest(object shipment)
    {
        var count = await fixture.QueryIntAsync("SELECT COUNT(*) FROM `shipment`");

        var response = await _client.PostAsJsonAsync(Url, shipment);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(count, await fixture.QueryIntAsync("SELECT COUNT(*) FROM `shipment`"));
    }

    [Fact]
    public async Task UpdateShipment_ChangesFieldsButKeepsCreatedAtAndItems()
    {
        var created = await CreateAsync(NewShipment(items: Item(11, 4)));
        await Task.Delay(10);

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", NewShipment(reference: "SHP-CHANGED"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await _client.GetFromJsonAsync<Shipment>($"{Url}/{created.Id}");
        Assert.Equal("SHP-CHANGED", updated!.Reference);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt > created.UpdatedAt);
        Assert.Equal(11, Assert.Single(updated.Items).ItemId);
    }

    [Fact]
    public async Task UpdateShipment_UnknownShipment_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"{Url}/999999", NewShipment());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateShipment_UnknownOrder_ReturnsBadRequest()
    {
        var created = await CreateAsync(NewShipment());

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", NewShipment(orderId: 999999));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateShipmentOrders_ChangesTheOrder()
    {
        var created = await CreateAsync(NewShipment(orderId: 3));

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}/orders", new[] { 4 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new[] { 4 }, await _client.GetFromJsonAsync<int[]>($"{Url}/{created.Id}/orders"));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 4, 5 })]
    [InlineData(new[] { 999999 })]
    public async Task UpdateShipmentOrders_InvalidOrders_ReturnsBadRequest(int[] orderIds)
    {
        var created = await CreateAsync(NewShipment());

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}/orders", orderIds);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateShipmentOrders_UnknownShipment_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"{Url}/999999/orders", new[] { 4 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateShipmentItems_ReplacesItemsAndUpdatesInventory()
    {
        var created = await CreateAsync(NewShipment(items: [Item(20, 6), Item(21, 3)]));
        var (q20, q21, q22) = (await QuantityOrderedAsync(20), await QuantityOrderedAsync(21), await QuantityOrderedAsync(22));

        // 20 goes up by 4, 21 is removed and 22 is added.
        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}/items", new[] { Item(20, 10), Item(22, 5) });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var items = (await _client.GetFromJsonAsync<List<ShipmentItem>>($"{Url}/{created.Id}/items"))!;
        Assert.Equal([(20, 10), (22, 5)], items.Select(i => (i.ItemId, i.Amount)).OrderBy(i => i.ItemId));
        Assert.Equal(q20 + 4, await QuantityOrderedAsync(20));
        Assert.Equal(Math.Max(0, q21 - 3), await QuantityOrderedAsync(21));
        Assert.Equal(q22 + 5, await QuantityOrderedAsync(22));
    }

    [Fact]
    public async Task UpdateShipmentItems_InvalidItems_ChangeNothing()
    {
        var created = await CreateAsync(NewShipment(items: Item(23, 2)));
        var q23 = await QuantityOrderedAsync(23);

        // The first item is valid, so this also checks that the change is rolled back.
        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}/items", new[] { Item(23, 8), Item(999999, 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var item = Assert.Single((await _client.GetFromJsonAsync<List<ShipmentItem>>($"{Url}/{created.Id}/items"))!);
        Assert.Equal((23, 2), (item.ItemId, item.Amount));
        Assert.Equal(q23, await QuantityOrderedAsync(23));
    }

    [Fact]
    public async Task UpdateShipmentItems_UnknownShipment_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"{Url}/999999/items", new[] { Item(20, 1) });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateShipmentItems_ConcurrentRequests_KeepInventoryConsistent()
    {
        // Starting at 1 means no request can push quantity_ordered below 0.
        var created = await CreateAsync(NewShipment(items: Item(30, 1)));
        var before = await QuantityOrderedAsync(30);

        var random = new Random(42);
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ =>
            _client.PutAsJsonAsync($"{Url}/{created.Id}/items", new[] { Item(30, random.Next(1, 41)) })));

        var item = Assert.Single((await _client.GetFromJsonAsync<List<ShipmentItem>>($"{Url}/{created.Id}/items"))!);
        Assert.Equal(before + item.Amount - 1, await QuantityOrderedAsync(30));
    }

    [Fact]
    public async Task DeleteShipment_RemovesShipmentAndItems()
    {
        var created = await CreateAsync(NewShipment(items: Item(12, 1)));

        var response = await _client.DeleteAsync($"{Url}/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{Url}/{created.Id}")).StatusCode);
        Assert.Equal(0, await fixture.QueryIntAsync(
            "SELECT COUNT(*) FROM `shipment_item` WHERE `shipment_id` = @id", ("@id", created.Id)));
    }

    [Fact]
    public async Task DeleteShipment_UnknownShipment_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"{Url}/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
