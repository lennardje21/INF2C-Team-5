using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Models;
using CargoHub.Tests.Fixtures;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class LocationsControllerTests(ApiFixture fixture)
{
    private const string Url = "/api/v2/locations";
    private readonly HttpClient _client = fixture.Client;

    // A dictionary so a test can change or add single fields.
    private static Dictionary<string, object> NewLocation(string name = "Test Location") => new()
    {
        ["warehouseId"] = 1,
        ["code"] = $"TEST-{Guid.NewGuid():N}",
        ["name"] = name
    };

    private async Task<Location> CreateAsync(object location)
    {
        var response = await _client.PostAsJsonAsync(Url, location);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Location>())!;
    }

    [Fact]
    public async Task GetLocations_ReturnsSeededLocations()
    {
        var locations = await _client.GetFromJsonAsync<List<Location>>(Url);

        Assert.NotNull(locations);
        Assert.NotEmpty(locations);
        Assert.Contains(locations, location => location.WarehouseId > 0);
    }

    [Fact]
    public async Task GetLocation_ReturnsTheLocation()
    {
        var location = await _client.GetFromJsonAsync<Location>($"{Url}/1");

        Assert.Equal(1, location!.Id);
        Assert.Equal(1, location.WarehouseId);
        Assert.NotEmpty(location.Code);
        Assert.NotEmpty(location.Name);
    }

    [Fact]
    public async Task GetLocation_UnknownLocation_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"{Url}/2147483647");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("code", "")]
    [InlineData("name", " ")]
    public async Task CreateLocation_EmptyField_ReturnsBadRequest(
        string field, string value)
    {
        var location = NewLocation();
        location[field] = value;

        var response = await _client.PostAsJsonAsync(Url, location);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateLocation_InvalidWarehouseId_ReturnsBadRequest(int warehouseId)
    {
        var location = NewLocation();
        location["warehouseId"] = warehouseId;

        var response = await _client.PostAsJsonAsync(Url, location);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("name")]
    public async Task CreateLocation_FieldOver255Characters_ReturnsBadRequest(string field)
    {
        var location = NewLocation();
        location[field] = new string('x', 256);

        var response = await _client.PostAsJsonAsync(Url, location);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLocation_IgnoresIdAndTimestampsFromRequest()
    {
        var location = NewLocation();
        location["id"] = 1;
        location["createdAt"] = "2000-01-01T00:00:00Z";
        location["updatedAt"] = "2000-01-01T00:00:00Z";

        var created = await CreateAsync(location);

        Assert.NotEqual(1, created.Id);
        Assert.Equal(1, created.WarehouseId);
        Assert.True(created.CreatedAt.Year >= 2026);
        Assert.InRange(
            created.UpdatedAt,
            created.CreatedAt.AddTicks(-10),
            created.CreatedAt.AddTicks(10));
    }

    [Fact]
    public async Task UpdateLocation_ChangesTheLocation()
    {
        var created = await CreateAsync(NewLocation());
        var updatedLocation = NewLocation(name: "Updated Location");

        var response = await _client.PutAsJsonAsync(
            $"{Url}/{created.Id}", updatedLocation);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await _client.GetFromJsonAsync<Location>($"{Url}/{created.Id}");

        Assert.Equal("Updated Location", updated!.Name);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
    }

    [Fact]
    public async Task UpdateLocation_InvalidField_ReturnsBadRequest()
    {
        var created = await CreateAsync(NewLocation());
        var location = NewLocation();
        location["name"] = "";

        var response = await _client.PutAsJsonAsync(
            $"{Url}/{created.Id}", location);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            created.Name,
            (await _client.GetFromJsonAsync<Location>($"{Url}/{created.Id}"))!.Name);
    }

    [Fact]
    public async Task UpdateLocation_UnknownLocation_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync(
            $"{Url}/2147483647", NewLocation());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteLocation_RemovesTheLocation()
    {
        var created = await CreateAsync(NewLocation());

        var response = await _client.DeleteAsync($"{Url}/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.GetAsync($"{Url}/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteLocation_WithInventory_ReturnsBadRequest()
    {
        var response = await _client.DeleteAsync($"{Url}/1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await _client.GetAsync($"{Url}/1")).StatusCode);
    }

    [Fact]
    public async Task DeleteLocation_UnknownLocation_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"{Url}/2147483647");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
