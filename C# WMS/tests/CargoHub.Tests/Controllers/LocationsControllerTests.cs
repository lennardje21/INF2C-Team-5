using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Tests.Fixtures;
using CargoHub.Api.Services;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class LocationsControllerTests(ApiFixture fixture)
{
    private const string Url = "/api/v1/locations";
    private readonly HttpClient _client = fixture.Client;

    private async Task<Location> CreateAsync(string? name = null)
    {
        var location = new Location
        {
            WarehouseId = 1,
            Code = $"TEST-{Guid.NewGuid():N}",
            Name = name ?? "Test Location"
        };

        var response = await _client.PostAsJsonAsync(Url, location);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Location>())!;
    }

    [Fact]
    public async Task GetLocations_ReturnsSeededLocations()
    {
        var locations = await _client.GetFromJsonAsync<List<Location>>(Url);

        Assert.NotNull(locations);
        Assert.True(locations.Count >= 400);
        Assert.Contains(locations, location => location.WarehouseId > 0);
    }

    [Fact]
    public async Task GetLocation_ReturnsTheLocation()
    {
        var location = await _client.GetFromJsonAsync<Location>($"{Url}/1");

        Assert.NotNull(location);
        Assert.Equal(1, location.Id);
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
    [InlineData("warehouseId", 0)]
    [InlineData("code", "")]
    [InlineData("name", " ")]
    public async Task CreateLocation_InvalidField_ReturnsBadRequest(
        string field, object value)
    {
        var location = new Dictionary<string, object>
        {
            ["warehouseId"] = 1,
            ["code"] = $"TEST-{Guid.NewGuid():N}",
            ["name"] = "Test Location"
        };
        location[field] = value;

        var response = await _client.PostAsJsonAsync(Url, location);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("name")]
    public async Task CreateLocation_FieldOver255Characters_ReturnsBadRequest(string field)
    {
        var location = new Dictionary<string, object>
        {
            ["warehouseId"] = 1,
            ["code"] = $"TEST-{Guid.NewGuid():N}",
            ["name"] = "Test Location"
        };
        location[field] = new string('x', 256);

        var response = await _client.PostAsJsonAsync(Url, location);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLocation_StoresLocationAndIgnoresIdAndTimestamps()
    {
        var location = new Location
        {
            Id = 1,
            WarehouseId = 1,
            Code = $"TEST-{Guid.NewGuid():N}",
            Name = "Created Location",
            CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var response = await _client.PostAsJsonAsync(Url, location);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<Location>())!;

        Assert.Equal($"{Url}/{created.Id}", response.Headers.Location!.AbsolutePath);
        Assert.NotEqual(1, created.Id);
        Assert.Equal(1, created.WarehouseId);
        Assert.True(created.CreatedAt.Year >= 2026);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);
    }

    [Fact]
    public async Task UpdateLocation_ChangesFieldsButKeepsCreatedAt()
    {
        var created = await CreateAsync();
        await Task.Delay(10);

        var response = await _client.PutAsJsonAsync(
            $"{Url}/{created.Id}",
            new Location
            {
                WarehouseId = 1,
                Code = $"UPDATED-{Guid.NewGuid():N}",
                Name = "Updated Location"
            });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var updated = await _client.GetFromJsonAsync<Location>($"{Url}/{created.Id}");

        Assert.NotNull(updated);
        Assert.Equal("Updated Location", updated.Name);
        Assert.Equal(1, updated.WarehouseId);
        Assert.InRange(
            updated.CreatedAt,
            created.CreatedAt.AddTicks(-10),
            created.CreatedAt.AddTicks(10));
        Assert.True(updated.UpdatedAt > created.UpdatedAt);

        Assert.Equal(
            "Updated Location",
            await fixture.QueryStringAsync(
                "SELECT `name` FROM `location` WHERE `id` = @id",
                ("@id", created.Id)));
    }

    [Fact]
    public async Task UpdateLocation_InvalidField_ReturnsBadRequestAndDoesNotChangeDatabase()
    {
        var created = await CreateAsync();

        var response = await _client.PutAsJsonAsync(
            $"{Url}/{created.Id}",
            new Location
            {
                WarehouseId = 1,
                Code = created.Code,
                Name = ""
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            created.Name,
            await fixture.QueryStringAsync(
                "SELECT `name` FROM `location` WHERE `id` = @id",
                ("@id", created.Id)));
    }

    [Fact]
    public async Task UpdateLocation_UnknownLocation_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync(
            $"{Url}/2147483647",
            new Location
            {
                WarehouseId = 1,
                Code = "UNKNOWN",
                Name = "Unknown Location"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteLocation_RemovesTheLocation()
    {
        var created = await CreateAsync();

        var response = await _client.DeleteAsync($"{Url}/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.GetAsync($"{Url}/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteLocation_WithInventory_ReturnsBadRequestAndKeepsTheLocation()
    {
        var response = await _client.DeleteAsync($"{Url}/1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            1,
            await fixture.QueryIntAsync(
                "SELECT COUNT(*) FROM `location` WHERE `id` = 1"));
    }

    [Fact]
    public async Task DeleteLocation_UnknownLocation_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"{Url}/2147483647");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public static IEnumerable<object[]> RequiredFields()
    {
        yield return [nameof(Location.Code)];
        yield return [nameof(Location.Name)];
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateAsync_InvalidWarehouseId_ThrowsArgumentException(int warehouseId)
    {
        var location = ValidLocation();
        location.WarehouseId = warehouseId;

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().CreateAsync(location));

        Assert.Contains(nameof(Location.WarehouseId), exception.Message);
    }

    [Theory]
    [MemberData(nameof(RequiredFields))]
    public async Task CreateAsync_EmptyRequiredField_ThrowsArgumentException(string field)
    {
        var location = ValidLocation();
        SetField(location, field, "");

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().CreateAsync(location));

        Assert.Contains(field, exception.Message);
    }

    [Theory]
    [MemberData(nameof(RequiredFields))]
    public async Task CreateAsync_FieldOver255Characters_ThrowsArgumentException(string field)
    {
        var location = ValidLocation();
        SetField(location, field, new string('x', 256));

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().CreateAsync(location));

        Assert.Contains(field, exception.Message);
    }

    [Fact]
    public async Task UpdateAsync_EmptyRequiredField_ThrowsBeforeDatabaseAccess()
    {
        var location = ValidLocation();
        location.Name = " ";

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().UpdateAsync(1, location));

        Assert.Contains(nameof(Location.Name), exception.Message);
    }

    private static LocationsService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());

        return new LocationsService(
            factory,
            new MySqlRepository<Location>(factory));
    }

    private static Location ValidLocation() => new()
    {
        WarehouseId = 1,
        Code = "UNIT-TEST",
        Name = "Unit Test Location"
    };

    private static void SetField(Location location, string field, string value)
    {
        var property = typeof(Location).GetProperty(field)
            ?? throw new ArgumentException($"Unknown location field: {field}");

        property.SetValue(location, value);
    }
}
