using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Models;
using CargoHub.Tests.Fixtures;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class ItemTypesControllerTests(ApiFixture fixture)
{
    private const string Url = "/api/v1/item_types";
    private readonly HttpClient _client = fixture.Client;

    private static Dictionary<string, object> NewItemType(string? name = null) => new()
    {
        ["name"] = name ?? $"Test Item Type {Guid.NewGuid():N}",
        ["description"] = "Item Type for API tests"
    };

    private async Task<ItemType> CreateAsync(object ItemType)
    {
        var response = await _client.PostAsJsonAsync(Url, ItemType);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ItemType>())!;
    }

    [Fact]
    public async Task GetItemTypes_ReturnsSeededItemTypes()
    {
        var ItemTypes = await _client.GetFromJsonAsync<List<ItemType>>(Url);
        var expected = await fixture.QueryIntAsync("SELECT COUNT(*) FROM `item_type`");

        Assert.NotNull(ItemTypes);
        Assert.Equal(expected, ItemTypes.Count);
        Assert.NotEmpty(ItemTypes);
    }

    [Fact]
    public async Task GetItemType_ReturnsTheItemType()
    {
        var created = await CreateAsync(NewItemType());
        var ItemType = await _client.GetFromJsonAsync<ItemType>($"{Url}/{created.Id}");

        Assert.NotNull(ItemType);
        Assert.Equal(created.Id, ItemType.Id);
        Assert.Equal(created.Name, ItemType.Name);
        Assert.Equal(created.Description, ItemType.Description);
    }

    [Fact]
    public async Task GetItemType_UnknownItemType_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"{Url}/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateItemType_StoresTheItemType()
    {
        var ItemType = NewItemType();
        var before = DateTime.UtcNow.AddSeconds(-1);

        var response = await _client.PostAsJsonAsync(Url, ItemType);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ItemType>())!;
        Assert.Equal($"{Url}/{created.Id}", response.Headers.Location!.AbsolutePath);
        Assert.True(created.Id > 0);
        Assert.True(created.CreatedAt >= before);
        Assert.InRange(
            created.UpdatedAt,
            created.CreatedAt.AddTicks(-10),
            created.CreatedAt.AddTicks(10));

        var stored = await _client.GetFromJsonAsync<ItemType>($"{Url}/{created.Id}");
        Assert.Equal(ItemType["name"], stored!.Name);
        Assert.Equal(ItemType["description"], stored.Description);
    }

    [Fact]
    public async Task CreateItemType_IgnoresIdAndTimestampsFromTheRequest()
    {
        var ItemType = NewItemType();
        ItemType["id"] = 1;
        ItemType["createdAt"] = "2000-01-01T00:00:00Z";
        ItemType["updatedAt"] = "2000-01-01T00:00:00Z";

        var created = await CreateAsync(ItemType);

        Assert.NotEqual(1, created.Id);
        Assert.NotEqual(2000, created.CreatedAt.Year);
        Assert.NotEqual(2000, created.UpdatedAt.Year);
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("name", "  ")]
    [InlineData("description", "")]
    [InlineData("description", "  ")]
    public async Task CreateItemType_MissingField_ReturnsBadRequest(string field, string value)
    {
        var ItemType = NewItemType();
        ItemType[field] = value;

        var response = await _client.PostAsJsonAsync(Url, ItemType);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateItemType_NameOver255Characters_ReturnsBadRequest()
    {
        var ItemType = NewItemType();
        ItemType["name"] = new string('x', 256);

        var response = await _client.PostAsJsonAsync(Url, ItemType);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateItemType_ChangesFieldsButKeepsCreatedAt()
    {
        var created = await CreateAsync(NewItemType());
        await Task.Delay(10);
        var ItemType = NewItemType(name: "Updated Test Item Type");
        ItemType["description"] = "Updated description";

        var response = await _client.PutAsJsonAsync(
            $"{Url}/{created.Id}", ItemType);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await _client.GetFromJsonAsync<ItemType>($"{Url}/{created.Id}");
        Assert.Equal("Updated Test Item Type", updated!.Name);
        Assert.Equal("Updated description", updated.Description);
        Assert.InRange(
            updated.CreatedAt,
            created.CreatedAt.AddTicks(-10),
            created.CreatedAt.AddTicks(10));
        Assert.True(updated.UpdatedAt > created.UpdatedAt);
    }

    [Fact]
    public async Task UpdateItemType_UnknownItemType_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"{Url}/999999", NewItemType());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateItemType_MissingField_ReturnsBadRequest()
    {
        var created = await CreateAsync(NewItemType());
        var ItemType = NewItemType();
        ItemType["name"] = "";

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", ItemType);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteItemType_RemovesTheItemType()
    {
        var created = await CreateAsync(NewItemType());

        var response = await _client.DeleteAsync($"{Url}/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.GetAsync($"{Url}/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteItemType_WithItems_ReturnsBadRequestAndKeepsTheItemType()
    {
        var id = await fixture.QueryIntAsync("SELECT `item_type_id` FROM `item` LIMIT 1");

        var response = await _client.DeleteAsync($"{Url}/{id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"{Url}/{id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteItemType_UnknownItemType_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"{Url}/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
