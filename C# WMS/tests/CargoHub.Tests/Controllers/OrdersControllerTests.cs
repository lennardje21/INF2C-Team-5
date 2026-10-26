using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CargoHub.Api.Models;
using CargoHub.Tests.Fixtures;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class OrdersControllerTests(ApiFixture fixture)
{
    private const string Url = "/api/v1/orders";
    private readonly HttpClient _client = fixture.Client;

    // Tests that change data use their own orders and items, so they do not
    // depend on each other. Creating an order does not change the inventory.
    // A dictionary, so a test can change, add or remove single fields.
    private static Dictionary<string, object?> NewOrder(int clientId = 1, int warehouseId = 1,
        string orderStatus = "Pending", string requestDate = "2026-10-30T09:00:00Z",
        params object[] items) => new()
    {
        ["clientId"] = clientId,
        ["orderDate"] = "2026-10-26T09:00:00Z",
        ["requestDate"] = requestDate,
        ["customerPoNumber"] = "PO-TEST",
        ["orderStatus"] = orderStatus,
        ["shippingNotes"] = null,
        ["warehouseId"] = warehouseId,
        ["shipToClientId"] = 2,
        ["billToClientId"] = 3,
        ["items"] = items
    };

    private static object Item(int itemId, int amount, decimal unitPrice = 2.50m) => new { itemId, amount, unitPrice };

    private static string ReferenceFor(int id) => $"ORD-{id:D6}";

    private async Task<Order> CreateAsync(object order)
    {
        var response = await _client.PostAsJsonAsync(Url, order);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Order>())!;
    }

    private Task<int> QuantityAllocatedAsync(int itemId) => fixture.QueryIntAsync(
        "SELECT `quantity_allocated` FROM `inventory` WHERE `item_id` = @id " +
        "ORDER BY `quantity_on_hand` DESC, `location_id` LIMIT 1", ("@id", itemId));

    private async Task<List<string>> RawPricesAsync(string url)
    {
        var json = JsonDocument.Parse(await _client.GetStringAsync(url)).RootElement;
        return json.EnumerateArray().Select(i => i.GetProperty("unitPrice").GetRawText()).ToList();
    }

    [Fact]
    public async Task GetOrders_ReturnsSeededOrdersWithItems()
    {
        var orders = await _client.GetFromJsonAsync<List<Order>>(Url);

        Assert.True(orders!.Count >= 4854);
        var first = orders.Single(o => o.Id == 1);
        Assert.Equal("ORD-000001", first.Reference);
        Assert.NotEmpty(first.Items);
    }

    [Fact]
    public async Task GetOrder_ReturnsOrderWithItems()
    {
        var order = await _client.GetFromJsonAsync<Order>($"{Url}/1");

        Assert.Equal(123, order!.ClientId);
        Assert.Equal(2, order.WarehouseId);
        Assert.Equal((31, 87), (order.ShipToClientId, order.BillToClientId));
        Assert.Equal(new DateTime(2025, 3, 5, 4, 42, 29, DateTimeKind.Utc), order.OrderDate);
        Assert.Equal(await fixture.QueryIntAsync(
            "SELECT COUNT(*) FROM `order_item` WHERE `order_id` = 1"), order.Items.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/items")]
    public async Task GetRoutes_UnknownOrder_ReturnNotFound(string route)
    {
        var response = await _client.GetAsync($"{Url}/999999{route}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetOrderItems_ReturnsPricesWithTwoDecimals()
    {
        var prices = await RawPricesAsync($"{Url}/1/items");

        Assert.Equal("21.34", prices[0]);
        Assert.All(prices, p => Assert.Matches(@"^\d+\.\d{2}$", p));
    }

    [Fact]
    public async Task CreateOrder_StoresOrderAndItems()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        var response = await _client.PostAsJsonAsync(Url, NewOrder(items: Item(50, 2, 9.99m)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<Order>())!;
        Assert.Equal($"{Url}/{created.Id}", response.Headers.Location!.AbsolutePath);
        Assert.True(created.Id > 4854);
        Assert.Equal(ReferenceFor(created.Id), created.Reference);
        Assert.True(created.CreatedAt >= before);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);

        var stored = await _client.GetFromJsonAsync<Order>($"{Url}/{created.Id}");
        Assert.Equal(ReferenceFor(created.Id), stored!.Reference);
        Assert.Null(stored.ShippingNotes);
        var item = Assert.Single(stored.Items);
        Assert.Equal((50, 2, 9.99m), (item.ItemId, item.Amount, item.UnitPrice));
    }

    [Fact]
    public async Task CreateOrder_RoundsPricesToTwoDecimals()
    {
        var created = await CreateAsync(NewOrder(items: [Item(50, 1, 9.9m), Item(51, 1, 1.005m), Item(52, 1, 5m)]));

        var prices = await RawPricesAsync($"{Url}/{created.Id}/items");

        Assert.Equal(["9.90", "1.01", "5.00"], prices);
    }

    [Fact]
    public async Task CreateOrder_IgnoresIdReferenceAndTimestampsFromTheRequest()
    {
        var order = NewOrder();
        order["id"] = 1;
        order["reference"] = "ORD-000001";
        order["createdAt"] = "2000-01-01T00:00:00Z";

        var created = await CreateAsync(order);

        Assert.NotEqual(1, created.Id);
        Assert.Equal(ReferenceFor(created.Id), created.Reference);
        Assert.True(created.CreatedAt.Year >= 2026);
    }

    [Fact]
    public async Task CreateOrder_DateWithoutTimeZone_IsStoredAsUtc()
    {
        var created = await CreateAsync(NewOrder(requestDate: "2026-10-30T09:00:00"));

        var json = await _client.GetStringAsync($"{Url}/{created.Id}");
        Assert.Equal("2026-10-30T09:00:00Z",
            JsonDocument.Parse(json).RootElement.GetProperty("requestDate").GetString());
    }

    [Fact]
    public async Task CreateOrder_ConcurrentRequests_EachGetTheirOwnReference()
    {
        var created = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => CreateAsync(NewOrder())));

        Assert.Equal(20, created.Select(o => o.Id).Distinct().Count());
        Assert.All(created, o => Assert.Equal(ReferenceFor(o.Id), o.Reference));
    }

    public static TheoryData<Dictionary<string, object?>> InvalidOrders
    {
        get
        {
            var missingDate = NewOrder();
            missingDate.Remove("orderDate");
            var missingPo = NewOrder();
            missingPo["customerPoNumber"] = " ";
            var unknownShipTo = NewOrder();
            unknownShipTo["shipToClientId"] = 999999;
            return new()
            {
                NewOrder(clientId: 999999),
                NewOrder(warehouseId: 999999),
                unknownShipTo,
                NewOrder(orderStatus: ""),
                missingPo,
                missingDate,
                NewOrder(items: Item(999999, 1)),
                NewOrder(items: Item(50, 0)),
                NewOrder(items: Item(50, 1, -1m)),
                NewOrder(items: [Item(50, 1), Item(50, 2)])
            };
        }
    }

    [Theory]
    [MemberData(nameof(InvalidOrders))]
    public async Task CreateOrder_InvalidInput_ReturnsBadRequest(Dictionary<string, object?> order)
    {
        var count = await fixture.QueryIntAsync("SELECT COUNT(*) FROM `order`");

        var response = await _client.PostAsJsonAsync(Url, order);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(count, await fixture.QueryIntAsync("SELECT COUNT(*) FROM `order`"));
    }

    [Fact]
    public async Task UpdateOrder_ChangesFieldsButKeepsReferenceCreatedAtAndItems()
    {
        var created = await CreateAsync(NewOrder(items: Item(53, 4)));
        await Task.Delay(10);
        var changes = NewOrder(orderStatus: "Shipped");
        changes["reference"] = "ORD-CHANGED";
        changes["shippingNotes"] = "Achterom leveren";

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", changes);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await _client.GetFromJsonAsync<Order>($"{Url}/{created.Id}");
        Assert.Equal("Shipped", updated!.OrderStatus);
        Assert.Equal("Achterom leveren", updated.ShippingNotes);
        Assert.Equal(ReferenceFor(created.Id), updated.Reference);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt > created.UpdatedAt);
        Assert.Equal(53, Assert.Single(updated.Items).ItemId);
    }

    [Fact]
    public async Task UpdateOrder_UnknownOrder_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"{Url}/999999", NewOrder());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateOrder_UnknownWarehouse_ReturnsBadRequest()
    {
        var created = await CreateAsync(NewOrder());

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", NewOrder(warehouseId: 999999));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateOrderItems_ReplacesItemsAndUpdatesInventory()
    {
        var created = await CreateAsync(NewOrder(items: [Item(60, 6), Item(61, 3)]));
        var (q60, q61, q62) = (await QuantityAllocatedAsync(60), await QuantityAllocatedAsync(61), await QuantityAllocatedAsync(62));

        // 60 goes up by 4, 61 is removed and 62 is added.
        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}/items", new[] { Item(60, 10), Item(62, 5) });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var items = (await _client.GetFromJsonAsync<List<OrderItem>>($"{Url}/{created.Id}/items"))!;
        Assert.Equal([(60, 10), (62, 5)], items.Select(i => (i.ItemId, i.Amount)).OrderBy(i => i.ItemId));
        Assert.Equal(q60 + 4, await QuantityAllocatedAsync(60));
        Assert.Equal(Math.Max(0, q61 - 3), await QuantityAllocatedAsync(61));
        Assert.Equal(q62 + 5, await QuantityAllocatedAsync(62));
    }

    [Fact]
    public async Task UpdateOrderItems_InvalidItems_ChangeNothing()
    {
        var created = await CreateAsync(NewOrder(items: Item(63, 2)));
        var q63 = await QuantityAllocatedAsync(63);

        // The first item is valid, so this also checks that the change is rolled back.
        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}/items", new[] { Item(63, 8), Item(999999, 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var item = Assert.Single((await _client.GetFromJsonAsync<List<OrderItem>>($"{Url}/{created.Id}/items"))!);
        Assert.Equal((63, 2), (item.ItemId, item.Amount));
        Assert.Equal(q63, await QuantityAllocatedAsync(63));
    }

    [Fact]
    public async Task UpdateOrderItems_UnknownOrder_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"{Url}/999999/items", new[] { Item(60, 1) });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateOrderItems_ConcurrentRequests_KeepInventoryConsistent()
    {
        // Starting at 1 means no request can push quantity_allocated below 0.
        var created = await CreateAsync(NewOrder(items: Item(70, 1)));
        var before = await QuantityAllocatedAsync(70);

        var random = new Random(42);
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ =>
            _client.PutAsJsonAsync($"{Url}/{created.Id}/items", new[] { Item(70, random.Next(1, 41)) })));

        var item = Assert.Single((await _client.GetFromJsonAsync<List<OrderItem>>($"{Url}/{created.Id}/items"))!);
        Assert.Equal(before + item.Amount - 1, await QuantityAllocatedAsync(70));
    }

    [Fact]
    public async Task DeleteOrder_RemovesOrderAndItems()
    {
        var created = await CreateAsync(NewOrder(items: Item(54, 1)));

        var response = await _client.DeleteAsync($"{Url}/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{Url}/{created.Id}")).StatusCode);
        Assert.Equal(0, await fixture.QueryIntAsync(
            "SELECT COUNT(*) FROM `order_item` WHERE `order_id` = @id", ("@id", created.Id)));
    }

    [Fact]
    public async Task DeleteOrder_WithShipments_ReturnsConflictAndKeepsTheOrder()
    {
        var items = await fixture.QueryIntAsync("SELECT COUNT(*) FROM `order_item` WHERE `order_id` = 1");

        var response = await _client.DeleteAsync($"{Url}/1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"{Url}/1")).StatusCode);
        Assert.Equal(items, await fixture.QueryIntAsync("SELECT COUNT(*) FROM `order_item` WHERE `order_id` = 1"));
    }

    [Fact]
    public async Task DeleteOrder_UnknownOrder_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"{Url}/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
