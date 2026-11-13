using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Models;
using CargoHub.Tests.Fixtures;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class WarehousesControllerTests(ApiFixture fixture)
{
    private const string Url = "/api/v2/warehouses";
    private readonly HttpClient _client = fixture.Client;

    // A dictionary so a test can change or add single fields.
    private static Dictionary<string, object> NewWarehouse(string name = "Test Warehouse") => new()
    {
        ["code"] = $"TEST-{Guid.NewGuid():N}",
        ["name"] = name,
        ["address"] = "Test Street 1",
        ["city"] = "Rotterdam",
        ["zipCode"] = "3000AA",
        ["province"] = "Zuid-Holland",
        ["country"] = "Netherlands",
        ["contactName"] = "Test Person",
        ["contactPhone"] = "0101234567",
        ["contactEmail"] = "test@example.com"
    };

    private async Task<Warehouse> CreateAsync(object warehouse)
    {
        var response = await _client.PostAsJsonAsync(Url, warehouse);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Warehouse>())!;
    }

    [Fact]
    public async Task GetWarehouses_ReturnsSeededWarehouses()
    {
        var warehouses = await _client.GetFromJsonAsync<List<Warehouse>>(Url);

        Assert.NotNull(warehouses);
        Assert.NotEmpty(warehouses);
        Assert.Contains(warehouses, warehouse => warehouse.Id == 1);
    }

    [Fact]
    public async Task GetWarehouse_ReturnsTheWarehouse()
    {
        var warehouse = await _client.GetFromJsonAsync<Warehouse>($"{Url}/1");

        Assert.Equal(1, warehouse!.Id);
        Assert.NotEmpty(warehouse.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/locations")]
    public async Task GetRoutes_UnknownWarehouse_ReturnNotFound(string route)
    {
        var response = await _client.GetAsync($"{Url}/2147483647{route}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateWarehouse_StoresTheWarehouse()
    {
        var response = await _client.PostAsJsonAsync(Url, NewWarehouse());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<Warehouse>())!;

        Assert.Equal($"{Url}/{created.Id}", response.Headers.Location!.AbsolutePath);
        Assert.True(created.Id > 0);
        Assert.NotEqual(default, created.CreatedAt);
        Assert.Equal(
            "Test Warehouse",
            (await _client.GetFromJsonAsync<Warehouse>($"{Url}/{created.Id}"))!.Name);
    }

    [Fact]
    public async Task CreateWarehouse_IgnoresIdAndTimestampsFromRequest()
    {
        var warehouse = NewWarehouse();
        warehouse["id"] = 1;
        warehouse["createdAt"] = "2000-01-01T00:00:00Z";
        warehouse["updatedAt"] = "2000-01-01T00:00:00Z";

        var created = await CreateAsync(warehouse);

        Assert.NotEqual(1, created.Id);
        Assert.True(created.CreatedAt.Year >= 2026);
    }

    [Theory]
    [InlineData("code", "WH-001")]
    [InlineData("code", "  VGH-AMB  ")]
    [InlineData("contactEmail", "jan.de-vries@bedrijf.nl")]
    [InlineData("contactPhone", "+31 (0)10 123 4567")]
    [InlineData("contactPhone", "(032) 1819600")]
    [InlineData("zipCode", "3011 AD")]
    [InlineData("city", "'s-Hertogenbosch")]
    [InlineData("contactName", "Pim Dorsman-Bruggeman")]
    public async Task CreateWarehouse_ValidFormat_ReturnsCreated(string field, string value)
    {
        var warehouse = NewWarehouse();
        warehouse[field] = value;

        await CreateAsync(warehouse);
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("city", "  ")]
    [InlineData("contactEmail", "not-an-email")]
    [InlineData("contactEmail", "test@example")]
    [InlineData("contactEmail", "te..st@example.com")]
    [InlineData("contactPhone", "12345")]
    [InlineData("contactPhone", "010-12345ab")]
    [InlineData("zipCode", "0123 AB")]
    [InlineData("zipCode", "3011")]
    [InlineData("city", "Rotterdam1")]
    [InlineData("contactName", "<script>")]
    [InlineData("name", "Jumbo\nTest")]
    [InlineData("address", "12345")]
    [InlineData("code", "VGH AMB")]
    [InlineData("code", "VGH--AMB")]
    [InlineData("code", "-VGH")]
    [InlineData("code", "VGH_AMB")]
    public async Task CreateWarehouse_InvalidField_ReturnsBadRequest(string field, string value)
    {
        var warehouse = NewWarehouse();
        warehouse[field] = value;

        var response = await _client.PostAsJsonAsync(Url, warehouse);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateWarehouse_ChangesTheWarehouse()
    {
        var created = await CreateAsync(NewWarehouse());

        var response = await _client.PutAsJsonAsync(
            $"{Url}/{created.Id}",
            NewWarehouse(name: "Changed Warehouse"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await _client.GetFromJsonAsync<Warehouse>($"{Url}/{created.Id}");

        Assert.Equal("Changed Warehouse", updated!.Name);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
    }

    [Fact]
    public async Task UpdateWarehouse_InvalidField_ReturnsBadRequest()
    {
        var created = await CreateAsync(NewWarehouse());
        var warehouse = NewWarehouse();
        warehouse["name"] = "";

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", warehouse);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            created.Name,
            (await _client.GetFromJsonAsync<Warehouse>($"{Url}/{created.Id}"))!.Name);
    }

    [Fact]
    public async Task GetWarehouseLocations_ReturnsLocationsForThatWarehouse()
    {
        var warehouse = await CreateAsync(NewWarehouse());

        await fixture.ExecuteAsync(
            "INSERT INTO `location` (`warehouse_id`, `code`, `name`, `created_at`, `updated_at`) " +
            "VALUES (@warehouseId, @code, @name, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))",
            ("@warehouseId", warehouse.Id),
            ("@code", $"LOC-{Guid.NewGuid():N}"),
            ("@name", "Test Location"));

        var locations = await _client.GetFromJsonAsync<List<Location>>(
            $"{Url}/{warehouse.Id}/locations");

        var location = Assert.Single(locations!);
        Assert.Equal(warehouse.Id, location.WarehouseId);
        Assert.Equal("Test Location", location.Name);

        await fixture.ExecuteAsync(
            "DELETE FROM `location` WHERE `warehouse_id` = @id",
            ("@id", warehouse.Id));
    }

    [Fact]
    public async Task DeleteWarehouse_RemovesTheWarehouse()
    {
        var warehouse = await CreateAsync(NewWarehouse());

        var response = await _client.DeleteAsync($"{Url}/{warehouse.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await _client.GetAsync($"{Url}/{warehouse.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteWarehouse_WithLocations_ReturnsBadRequest()
    {
        var warehouse = await CreateAsync(NewWarehouse());

        await fixture.ExecuteAsync(
            "INSERT INTO `location` (`warehouse_id`, `code`, `name`, `created_at`, `updated_at`) " +
            "VALUES (@warehouseId, @code, @name, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))",
            ("@warehouseId", warehouse.Id),
            ("@code", $"LOC-{Guid.NewGuid():N}"),
            ("@name", "Referenced Location"));

        var response = await _client.DeleteAsync($"{Url}/{warehouse.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await _client.GetAsync($"{Url}/{warehouse.Id}")).StatusCode);

        await fixture.ExecuteAsync(
            "DELETE FROM `location` WHERE `warehouse_id` = @id",
            ("@id", warehouse.Id));
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await _client.DeleteAsync($"{Url}/{warehouse.Id}")).StatusCode);
    }

    [Fact]
    public async Task UpdateAndDeleteUnknownWarehouse_ReturnNotFound()
    {
        var update = await _client.PutAsJsonAsync(
            $"{Url}/2147483647", NewWarehouse());
        var delete = await _client.DeleteAsync($"{Url}/2147483647");

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }
}