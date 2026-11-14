using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Services;
using CargoHub.Api.Models;
using CargoHub.Tests.Fixtures;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class ItemsControllerTests(ApiFixture fixture)
{
    private const string Url = "/api/v2/items";
    private readonly HttpClient _client = fixture.Client;

    // A dictionoary so a test can change

    private static Dictionary<string, object> NewItem(string? code = null) => new()
    {
        ["code"] = code ?? $"TEST-{Guid.NewGuid():N}",
        ["description"] = "Item for API tests",
        ["barcode"] = "1234567890123",
        ["modelNumber"] = "MOD-TEST",
        ["commodityCode"] = 100,
        ["unitWeight"] =1.5,
        ["itemLineId"] = 1,
        ["itemGroupId"] = 1,
        ["itemTypeId"] = 1,
        ["minPurchaseQuantity"] = 1,
        ["caseSize"] = 10,
        ["packagingType"] = "Box",
        ["orderMultiple"] = 1,
        ["supplierId"] = 1,
        ["supplierSku"] = "SKU-TEST"
    };

    private async Task<Item> CreateAsync(object item)
    {
        var response = await _client.PostAsJsonAsync(Url, item);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Item>())!;
    }

    [Fact]
    public async Task GetItems_ReturnsSeededItems()
    {
        var items = await _client.GetFromJsonAsync<List<Item>>(Url);
        var expected = await fixture.QueryIntAsync("SELECT COUNT(*) FROM `item`");

        Assert.NotNull(items);
        Assert.Equal(expected, items.Count);
        Assert.NotEmpty(items);
    }

    [Fact]
    public async Task GetItem_ReturnsTheItem()
    {
        var created = await CreateAsync(NewItem());
        var item = await _client.GetFromJsonAsync<Item>($"{Url}/{created.Id}");

        Assert.NotNull(item);
        Assert.Equal(created.Id, item.Id);
        Assert.Equal(created.Code, item.Code);
        Assert.Equal(created.Description, item.Description);
    }

    [Fact]
    public async Task GetItem_UnknownItem_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"{Url}/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetItemInventory_ReturnsTheInventoryOfTheItem()
    {
        var id = await fixture.QueryIntAsync("SELECT `item_id` FROM `inventory` LIMIT 1");
        var expected = await fixture.QueryIntAsync("SELECT COUNT(*) FROM `inventory` WHERE `item_id` = @id", ("@id", id));


        var inventory = await _client.GetFromJsonAsync<List<Inventory>>($"{Url}/{id}/inventory");
        
        Assert.NotNull(inventory);
        Assert.Equal(expected, inventory.Count);
        Assert.All(inventory, i => Assert.Equal(id, i.ItemId));
    }

    [Fact]
    public async Task GetItemInventory_UnknownItem_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"{Url}/999999/inventory");
 
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
 
    [Fact]
    public async Task GetItemInventoryTotals_ReturnsTheSums()
    {
        var id = await fixture.QueryIntAsync("SELECT `item_id` FROM `inventory` LIMIT 1");
        var expected = await fixture.QueryIntAsync(
            "SELECT SUM(`quantity_expected`) FROM `inventory` WHERE `item_id` = @id", ("@id", id));
        var available = await fixture.QueryIntAsync(
            "SELECT SUM(`quantity_on_hand` - `quantity_allocated`) FROM `inventory` WHERE `item_id` = @id",
            ("@id", id));
 
        var totals = await _client.GetFromJsonAsync<ItemInventoryTotals>($"{Url}/{id}/inventory/totals");
 
        Assert.NotNull(totals);
        Assert.Equal((long)expected, totals.TotalExpected);
        Assert.Equal((long)available, totals.TotalAvailable);
    }
 
    [Fact]
    public async Task GetItemInventoryTotals_UnknownItem_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"{Url}/999999/inventory/totals");
 
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
 
    [Fact]
    public async Task CreateItem_StoresTheItem()
    {
        var item = NewItem();
        var before = DateTime.UtcNow.AddSeconds(-1);
 
        var response = await _client.PostAsJsonAsync(Url, item);
 
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<Item>())!;
        Assert.Equal($"{Url}/{created.Id}", response.Headers.Location!.AbsolutePath);
        Assert.True(created.Id > 0);
        Assert.True(created.CreatedAt >= before);
        Assert.InRange(
            created.UpdatedAt,
            created.CreatedAt.AddTicks(-10),
            created.CreatedAt.AddTicks(10));
 
        var stored = await _client.GetFromJsonAsync<Item>($"{Url}/{created.Id}");
        Assert.Equal(item["code"], stored!.Code);
        Assert.Equal(item["description"], stored.Description);
        Assert.Equal(item["supplierSku"], stored.SupplierSku);
        Assert.Equal(1.5m, stored.UnitWeight);
    }

    [Fact]
    public async Task CreateItem_TrimsFieldsBeforeStoring()
    {
        var item = NewItem();
        item["description"] = "  First line\nSecond line  ";
        item["supplierSku"] = "  SKU-TEST  ";

        var created = await CreateAsync(item);
        var stored = await _client.GetFromJsonAsync<Item>($"{Url}/{created.Id}");

        Assert.NotNull(stored);
        Assert.Equal("First line\nSecond line", created.Description);
        Assert.Equal(created.Description, stored.Description);
        Assert.Equal("SKU-TEST", created.SupplierSku);
        Assert.Equal(created.SupplierSku, stored.SupplierSku);
    }

    [Fact]
    public async Task CreateItem_WeightWithTooManyDecimalPlaces_ReturnsValidationDetails()
    {
        var item = NewItem();
        item["unitWeight"] = 1.1234567m;

        using var response = await _client.PostAsJsonAsync(Url, item);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.Equal("UnitWeight must not have more than 6 decimal places.", problem.Detail);
    }
 
    [Fact]
    public async Task CreateItem_IgnoresIdAndTimestampsFromTheRequest()
    {
        var item = NewItem();
        item["id"] = 1;
        item["createdAt"] = "2000-01-01T00:00:00Z";
        item["updatedAt"] = "2000-01-01T00:00:00Z";
 
        var created = await CreateAsync(item);
 
        Assert.NotEqual(1, created.Id);
        Assert.NotEqual(2000, created.CreatedAt.Year);
        Assert.NotEqual(2000, created.UpdatedAt.Year);
    }
 
    [Theory]
    [InlineData("code", "")]
    [InlineData("code", "  ")]
    [InlineData("description", "")]
    [InlineData("barcode", "")]
    [InlineData("modelNumber", "")]
    [InlineData("packagingType", "")]
    [InlineData("supplierSku", "")]
    public async Task CreateItem_MissingField_ReturnsBadRequest(string field, string value)
    {
        var item = NewItem();
        item[field] = value;
 
        var response = await _client.PostAsJsonAsync(Url, item);
 
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
 
    [Fact]
    public async Task CreateItem_CodeOver255Characters_ReturnsBadRequest()
    {
        var item = NewItem();
        item["code"] = new string('x', 256);
 
        var response = await _client.PostAsJsonAsync(Url, item);
 
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
 
    [Theory]
    [InlineData("unitWeight")]
    [InlineData("commodityCode")]
    [InlineData("minPurchaseQuantity")]
    [InlineData("caseSize")]
    [InlineData("orderMultiple")]
    public async Task CreateItem_NegativeNumber_ReturnsBadRequest(string field)
    {
        var item = NewItem();
        item[field] = -1;
 
        var response = await _client.PostAsJsonAsync(Url, item);
 
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
 
    [Theory]
    [InlineData("itemLineId")]
    [InlineData("itemGroupId")]
    [InlineData("itemTypeId")]
    [InlineData("supplierId")]
    public async Task CreateItem_UnknownReference_ReturnsBadRequest(string field)
    {
        var item = NewItem();
        item[field] = 999999;
 
        var response = await _client.PostAsJsonAsync(Url, item);
 
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
 
    [Fact]
    public async Task UpdateItem_ChangesFieldsButKeepsCreatedAt()
    {
        var created = await CreateAsync(NewItem());
        await Task.Delay(10);
        var item = NewItem(code: "UPDATED-TEST");
        item["description"] = "Updated description";
 
        var response = await _client.PutAsJsonAsync(
            $"{Url}/{created.Id}", item);
 
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await _client.GetFromJsonAsync<Item>($"{Url}/{created.Id}");
        Assert.Equal("UPDATED-TEST", updated!.Code);
        Assert.Equal("Updated description", updated.Description);
        Assert.InRange(
            updated.CreatedAt,
            created.CreatedAt.AddTicks(-10),
            created.CreatedAt.AddTicks(10));
        Assert.True(updated.UpdatedAt > created.UpdatedAt);
    }
 
    [Fact]
    public async Task UpdateItem_UnknownItem_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"{Url}/999999", NewItem());
 
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
 
    [Fact]
    public async Task UpdateItem_MissingField_ReturnsBadRequest()
    {
        var created = await CreateAsync(NewItem());
        var item = NewItem();
        item["code"] = "";
 
        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", item);
 
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateItem_NonPositiveReference_ReturnsValidationDetailsAndKeepsItem()
    {
        var created = await CreateAsync(NewItem());
        var item = NewItem();
        item["supplierId"] = 0;

        using var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", item);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.Equal("SupplierId must be greater than 0.", problem.Detail);

        var stored = await _client.GetFromJsonAsync<Item>($"{Url}/{created.Id}");
        Assert.NotNull(stored);
        Assert.Equal(created.Code, stored.Code);
        Assert.Equal(created.SupplierId, stored.SupplierId);
        Assert.Equal(created.UpdatedAt, stored.UpdatedAt);
    }
 
    [Fact]
    public async Task DeleteItem_RemovesTheItem()
    {
        var created = await CreateAsync(NewItem());
 
        var response = await _client.DeleteAsync($"{Url}/{created.Id}");
 
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.GetAsync($"{Url}/{created.Id}")).StatusCode);
    }
 
    [Fact]
    public async Task DeleteItem_WithInventory_ReturnsBadRequestAndKeepsTheItem()
    {
        var id = await fixture.QueryIntAsync("SELECT `item_id` FROM `inventory` LIMIT 1");
 
        var response = await _client.DeleteAsync($"{Url}/{id}");
 
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"{Url}/{id}")).StatusCode);
    }
 
    [Fact]
    public async Task DeleteItem_UnknownItem_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"{Url}/999999");
 
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}