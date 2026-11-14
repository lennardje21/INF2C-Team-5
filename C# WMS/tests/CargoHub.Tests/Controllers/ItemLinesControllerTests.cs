using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Models;
using CargoHub.Tests.Fixtures;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class ItemLinesControllerTests(ApiFixture fixture)
{
    private const string Url = "/api/v2/item_lines";
    private readonly HttpClient _client = fixture.Client;

    private static Dictionary<string, object> NewItemLine(string? name = null) => new()
    {
        ["name"] = name ?? $"Test Item Line {Guid.NewGuid():N}",
        ["description"] = "Item line for API tests"
    };

    private async Task<ItemLine> CreateAsync(object itemLine)
    {
        var response = await _client.PostAsJsonAsync(Url, itemLine);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ItemLine>())!;
    }

    [Fact]
    public async Task GetItemLines_ReturnsSeededItemLines()
    {
        var itemLines = await _client.GetFromJsonAsync<List<ItemLine>>(Url);
        var expected = await fixture.QueryIntAsync("SELECT COUNT(*) FROM `item_line`");

        Assert.NotNull(itemLines);
        Assert.Equal(expected, itemLines.Count);
        Assert.NotEmpty(itemLines);
    }

    [Fact]
    public async Task GetItemLine_ReturnsTheItemLine()
    {
        var created = await CreateAsync(NewItemLine());
        var itemLine = await _client.GetFromJsonAsync<ItemLine>($"{Url}/{created.Id}");

        Assert.NotNull(itemLine);
        Assert.Equal(created.Id, itemLine.Id);
        Assert.Equal(created.Name, itemLine.Name);
        Assert.Equal(created.Description, itemLine.Description);
    }

    [Fact]
    public async Task GetItemLine_UnknownItemLine_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"{Url}/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateItemLine_StoresTheItemLine()
    {
        var itemLine = NewItemLine();
        var before = DateTime.UtcNow.AddSeconds(-1);

        var response = await _client.PostAsJsonAsync(Url, itemLine);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ItemLine>())!;
        Assert.Equal($"{Url}/{created.Id}", response.Headers.Location!.AbsolutePath);
        Assert.True(created.Id > 0);
        Assert.True(created.CreatedAt >= before);
        Assert.InRange(
            created.UpdatedAt,
            created.CreatedAt.AddTicks(-10),
            created.CreatedAt.AddTicks(10));

        var stored = await _client.GetFromJsonAsync<ItemLine>($"{Url}/{created.Id}");
        Assert.Equal(itemLine["name"], stored!.Name);
        Assert.Equal(itemLine["description"], stored.Description);
    }

    [Fact]
    public async Task CreateItemLine_IgnoresIdAndTimestampsFromTheRequest()
    {
        var itemLine = NewItemLine();
        itemLine["id"] = 1;
        itemLine["createdAt"] = "2000-01-01T00:00:00Z";
        itemLine["updatedAt"] = "2000-01-01T00:00:00Z";

        var created = await CreateAsync(itemLine);

        Assert.NotEqual(1, created.Id);
        Assert.NotEqual(2000, created.CreatedAt.Year);
        Assert.NotEqual(2000, created.UpdatedAt.Year);
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("name", "  ")]
    [InlineData("description", "")]
    [InlineData("description", "  ")]
    public async Task CreateItemLine_MissingField_ReturnsBadRequest(string field, string value)
    {
        var itemLine = NewItemLine();
        itemLine[field] = value;

        var response = await _client.PostAsJsonAsync(Url, itemLine);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateItemLine_NameOver255Characters_ReturnsBadRequest()
    {
        var itemLine = NewItemLine();
        itemLine["name"] = new string('x', 256);

        var response = await _client.PostAsJsonAsync(Url, itemLine);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateItemLine_TrimsTextAndStoresMultilineDescription()
    {
        var itemLine = NewItemLine("  Test line  ");
        itemLine["description"] = "  First line\nSecond line  ";

        var created = await CreateAsync(itemLine);
        var stored = await _client.GetFromJsonAsync<ItemLine>($"{Url}/{created.Id}");

        Assert.NotNull(stored);
        Assert.Equal("Test line", created.Name);
        Assert.Equal(created.Name, stored.Name);
        Assert.Equal("First line\nSecond line", created.Description);
        Assert.Equal(created.Description, stored.Description);
    }

    [Theory]
    [InlineData(32767, true)]
    [InlineData(32768, false)]
    public async Task CreateItemLine_DescriptionUtf8ByteLimit_IsEnforced(int count, bool accepted)
    {
        var itemLine = NewItemLine();
        itemLine["description"] = new string('é', count) + (accepted ? "a" : "");

        using var response = await _client.PostAsJsonAsync(Url, itemLine);

        Assert.Equal(accepted ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
        if (!accepted)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal("Description must not exceed 65535 UTF-8 bytes.", problem.Detail);
        }
    }

    [Theory]
    [InlineData(255, HttpStatusCode.Created)]
    [InlineData(256, HttpStatusCode.BadRequest)]
    public async Task CreateItemLine_NameUnicodeCharacterLimit_IsEnforced(int count, HttpStatusCode expected)
    {
        var itemLine = NewItemLine(string.Concat(Enumerable.Repeat("😀", count)));

        using var response = await _client.PostAsJsonAsync(Url, itemLine);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("name", "Invalid\nname", "Name")]
    [InlineData("description", "Invalid\0description", "Description")]
    public async Task UpdateItemLine_ControlCharacter_ReturnsBadRequestAndKeepsItemLine(
        string field, string value, string expectedField)
    {
        var created = await CreateAsync(NewItemLine());
        var itemLine = NewItemLine();
        itemLine[field] = value;

        using var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", itemLine);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(expectedField, problem.Detail);
        var stored = await _client.GetFromJsonAsync<ItemLine>($"{Url}/{created.Id}");
        Assert.NotNull(stored);
        Assert.Equal(created.Name, stored.Name);
        Assert.Equal(created.Description, stored.Description);
    }

    [Fact]
    public async Task UpdateItemLine_ChangesFieldsButKeepsCreatedAt()
    {
        var created = await CreateAsync(NewItemLine());
        await Task.Delay(10);
        var itemLine = NewItemLine(name: "Updated Test Item Line");
        itemLine["description"] = "Updated description";

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", itemLine);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await _client.GetFromJsonAsync<ItemLine>($"{Url}/{created.Id}");
        Assert.Equal("Updated Test Item Line", updated!.Name);
        Assert.Equal("Updated description", updated.Description);
        Assert.InRange(
            updated.CreatedAt,
            created.CreatedAt.AddTicks(-10),
            created.CreatedAt.AddTicks(10));
        Assert.True(updated.UpdatedAt > created.UpdatedAt);
    }

    [Fact]
    public async Task UpdateItemLine_UnknownItemLine_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"{Url}/999999", NewItemLine());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateItemLine_MissingField_ReturnsBadRequest()
    {
        var created = await CreateAsync(NewItemLine());
        var itemLine = NewItemLine();
        itemLine["name"] = "";

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", itemLine);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteItemLine_RemovesTheItemLine()
    {
        var created = await CreateAsync(NewItemLine());

        var response = await _client.DeleteAsync($"{Url}/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.GetAsync($"{Url}/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteItemLine_WithItems_ReturnsBadRequestAndKeepsTheItemLine()
    {
        var id = await fixture.QueryIntAsync("SELECT `item_line_id` FROM `item` LIMIT 1");

        var response = await _client.DeleteAsync($"{Url}/{id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"{Url}/{id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteItemLine_UnknownItemLine_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"{Url}/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
