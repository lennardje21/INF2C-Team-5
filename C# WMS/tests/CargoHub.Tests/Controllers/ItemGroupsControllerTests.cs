using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Models;
using CargoHub.Tests.Fixtures;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class ItemGroupsControllerTests(ApiFixture fixture)
{
    private const string Url = "/api/v1/item_groups";
    private readonly HttpClient _client = fixture.Client;

    private static Dictionary<string, object> NewItemGroup(string? name = null) => new()
    {
        ["name"] = name ?? $"Test Item Group {Guid.NewGuid():N}",
        ["description"] = "Item group for API tests"
    };

    private async Task<ItemGroup> CreateAsync(object itemGroup)
    {
        var response = await _client.PostAsJsonAsync(Url, itemGroup);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ItemGroup>())!;
    }

    [Fact]
    public async Task GetItemGroups_ReturnsSeededItemGroups()
    {
        var itemGroups = await _client.GetFromJsonAsync<List<ItemGroup>>(Url);
        var expected = await fixture.QueryIntAsync("SELECT COUNT(*) FROM `item_group`");

        Assert.NotNull(itemGroups);
        Assert.Equal(expected, itemGroups.Count);
        Assert.NotEmpty(itemGroups);
    }

    [Fact]
    public async Task GetItemGroup_ReturnsTheItemGroup()
    {
        var created = await CreateAsync(NewItemGroup());
        var itemGroup = await _client.GetFromJsonAsync<ItemGroup>($"{Url}/{created.Id}");

        Assert.NotNull(itemGroup);
        Assert.Equal(created.Id, itemGroup.Id);
        Assert.Equal(created.Name, itemGroup.Name);
        Assert.Equal(created.Description, itemGroup.Description);
    }

    [Fact]
    public async Task GetItemGroup_UnknownItemGroup_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"{Url}/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateItemGroup_StoresTheItemGroup()
    {
        var itemGroup = NewItemGroup();
        var before = DateTime.UtcNow.AddSeconds(-1);

        var response = await _client.PostAsJsonAsync(Url, itemGroup);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ItemGroup>())!;
        Assert.Equal($"{Url}/{created.Id}", response.Headers.Location!.AbsolutePath);
        Assert.True(created.Id > 0);
        Assert.True(created.CreatedAt >= before);
        Assert.InRange(
            created.UpdatedAt,
            created.CreatedAt.AddTicks(-10),
            created.CreatedAt.AddTicks(10));

        var stored = await _client.GetFromJsonAsync<ItemGroup>($"{Url}/{created.Id}");
        Assert.Equal(itemGroup["name"], stored!.Name);
        Assert.Equal(itemGroup["description"], stored.Description);
    }

    [Fact]
    public async Task CreateItemGroup_IgnoresIdAndTimestampsFromTheRequest()
    {
        var itemGroup = NewItemGroup();
        itemGroup["id"] = 1;
        itemGroup["createdAt"] = "2000-01-01T00:00:00Z";
        itemGroup["updatedAt"] = "2000-01-01T00:00:00Z";

        var created = await CreateAsync(itemGroup);

        Assert.NotEqual(1, created.Id);
        Assert.NotEqual(2000, created.CreatedAt.Year);
        Assert.NotEqual(2000, created.UpdatedAt.Year);
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("name", "  ")]
    [InlineData("description", "")]
    [InlineData("description", "  ")]
    public async Task CreateItemGroup_MissingField_ReturnsBadRequest(string field, string value)
    {
        var itemGroup = NewItemGroup();
        itemGroup[field] = value;

        var response = await _client.PostAsJsonAsync(Url, itemGroup);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateItemGroup_NameOver255Characters_ReturnsBadRequest()
    {
        var itemGroup = NewItemGroup();
        itemGroup["name"] = new string('x', 256);

        var response = await _client.PostAsJsonAsync(Url, itemGroup);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateItemGroup_ChangesFieldsButKeepsCreatedAt()
    {
        var created = await CreateAsync(NewItemGroup());
        await Task.Delay(10);
        var itemGroup = NewItemGroup(name: "Updated Test Item Group");
        itemGroup["description"] = "Updated description";

        var response = await _client.PutAsJsonAsync(
            $"{Url}/{created.Id}", itemGroup);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await _client.GetFromJsonAsync<ItemGroup>($"{Url}/{created.Id}");
        Assert.Equal("Updated Test Item Group", updated!.Name);
        Assert.Equal("Updated description", updated.Description);
        Assert.InRange(
            updated.CreatedAt,
            created.CreatedAt.AddTicks(-10),
            created.CreatedAt.AddTicks(10));
        Assert.True(updated.UpdatedAt > created.UpdatedAt);
    }

    [Fact]
    public async Task UpdateItemGroup_UnknownItemGroup_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"{Url}/999999", NewItemGroup());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateItemGroup_MissingField_ReturnsBadRequest()
    {
        var created = await CreateAsync(NewItemGroup());
        var itemGroup = NewItemGroup();
        itemGroup["name"] = "";

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", itemGroup);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteItemGroup_RemovesTheItemGroup()
    {
        var created = await CreateAsync(NewItemGroup());

        var response = await _client.DeleteAsync($"{Url}/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.GetAsync($"{Url}/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteItemGroup_WithItems_ReturnsBadRequestAndKeepsTheItemGroup()
    {
        var id = await fixture.QueryIntAsync("SELECT `item_group_id` FROM `item` LIMIT 1");

        var response = await _client.DeleteAsync($"{Url}/{id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"{Url}/{id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteItemGroup_UnknownItemGroup_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"{Url}/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
