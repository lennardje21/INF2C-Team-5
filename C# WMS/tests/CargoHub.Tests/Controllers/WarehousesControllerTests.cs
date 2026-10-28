using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using CargoHub.Tests.Fixtures;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Controllers;

 [Collection("Api")]
public sealed class WarehousesControllerTests(ApiFixture fixture)
{
    private const string Url = "/api/v1/warehouses";

    [Fact]
    public async Task Post_InvalidEmail_ReturnsBadRequest()
    {
        var client = fixture.Client;

        var response = await client.PostAsJsonAsync(
            "/api/v1/warehouses",
            WithEmail(ValidWarehouse(), "bad-email"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Code")]
    [InlineData("Name")]
    [InlineData("Address")]
    [InlineData("City")]
    [InlineData("ZipCode")]
    [InlineData("Province")]
    [InlineData("Country")]
    [InlineData("ContactName")]
    [InlineData("ContactPhone")]
    [InlineData("ContactEmail")]
    public async Task Post_EmptyRequiredField_ReturnsBadRequest(string field)
    {
        var client = fixture.Client;

        var warehouse = ValidWarehouse();
        typeof(Warehouse).GetProperty(field)!.SetValue(warehouse, "");

        var response = await client.PostAsJsonAsync(
            "/api/v1/warehouses", warehouse);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Code")]
    [InlineData("Name")]
    [InlineData("Address")]
    [InlineData("City")]
    [InlineData("ZipCode")]
    [InlineData("Province")]
    [InlineData("Country")]
    [InlineData("ContactName")]
    [InlineData("ContactPhone")]
    [InlineData("ContactEmail")]
    public async Task Post_FieldOver255Characters_ReturnsBadRequest(string field)
    {
        var client = fixture.Client;

        var warehouse = ValidWarehouse();
        typeof(Warehouse).GetProperty(field)!
            .SetValue(warehouse, new string('x', 256));

        var response = await client.PostAsJsonAsync(
            "/api/v1/warehouses", warehouse);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_InvalidInput_DoesNotChangeDatabase()
    {
        var client = fixture.Client;

        var before = await fixture.QueryIntAsync("SELECT COUNT(*) FROM `warehouse`");

        var warehouse = ValidWarehouse();
        warehouse.ContactEmail = "not-an-email";

        var response = await client.PostAsJsonAsync(
            "/api/v1/warehouses", warehouse);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            before,
            await fixture.QueryIntAsync("SELECT COUNT(*) FROM `warehouse`"));
    }

    [Fact]
    public async Task WarehouseCrudAndLocationsEndpoints_Work()
    {
        var client = fixture.Client;

        var warehouse = ValidWarehouse();
        warehouse.Code = $"TEST-{Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/warehouses", warehouse);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<Warehouse>();

        Assert.NotNull(created);
        Assert.True(created.Id > 0);
        Assert.NotEqual(default, created.CreatedAt);
        Assert.NotEqual(default, created.UpdatedAt);

        var getResponse = await client.GetAsync(
            $"/api/v1/warehouses/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var fetched = await getResponse.Content.ReadFromJsonAsync<Warehouse>();

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);

        var listResponse = await client.GetAsync("/api/v1/warehouses");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var all = await listResponse.Content.ReadFromJsonAsync<List<Warehouse>>();

        Assert.NotNull(all);
        Assert.Contains(all, item => item.Id == created.Id);

        var locationsResponse = await client.GetAsync(
            $"/api/v1/warehouses/{created.Id}/locations");

        Assert.Equal(HttpStatusCode.OK, locationsResponse.StatusCode);

        var locations = await locationsResponse.Content.ReadFromJsonAsync<List<Location>>();

        Assert.NotNull(locations);
        Assert.Empty(locations);

        var originalCreatedAt = created.CreatedAt;
        var originalUpdatedAt = created.UpdatedAt;

        await Task.Delay(10);

        created.Name = "Updated Test Warehouse";

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/warehouses/{created.Id}", created);

        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var updatedResponse = await client.GetAsync(
            $"/api/v1/warehouses/{created.Id}");

        var updated = await updatedResponse.Content.ReadFromJsonAsync<Warehouse>();

        Assert.NotNull(updated);
        Assert.Equal("Updated Test Warehouse", updated.Name);
        Assert.InRange(updated.CreatedAt, originalCreatedAt.AddTicks(-10), originalCreatedAt.AddTicks(10));
        Assert.True(updated.UpdatedAt > originalUpdatedAt);

        Assert.Equal(
            "Updated Test Warehouse",
            await fixture.QueryStringAsync(
                "SELECT `name` FROM `warehouse` WHERE `id` = @id", ("@id", created.Id)));

        var deleteResponse = await client.DeleteAsync(
            $"/api/v1/warehouses/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var missingResponse = await client.GetAsync(
            $"/api/v1/warehouses/{created.Id}");

        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        Assert.Equal(
            0,
            await fixture.QueryIntAsync(
                "SELECT COUNT(*) FROM `warehouse` WHERE `id` = @id",
                ("@id", created.Id)));
    }

    [Fact]
    public async Task CreateWarehouse_IgnoresIdAndTimestampsFromRequest()
    {
        var client = fixture.Client;

        var warehouse = ValidWarehouse();
        warehouse.Code = $"TEST-{Guid.NewGuid():N}";
        warehouse.Id = 1;
        warehouse.CreatedAt = new DateTime(
            2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        warehouse.UpdatedAt = warehouse.CreatedAt;

        var response = await client.PostAsJsonAsync(
            "/api/v1/warehouses", warehouse);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = (await response.Content.ReadFromJsonAsync<Warehouse>())!;

        Assert.NotEqual(1, created.Id);
        Assert.True(created.CreatedAt.Year >= DateTime.UtcNow.Year);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync(
                $"/api/v1/warehouses/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task GetWarehouseLocations_ReturnsLocationsForThatWarehouse()
    {
        var client = fixture.Client;

        var warehouse = ValidWarehouse();
        warehouse.Code = $"TEST-{Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync(
            "/api/v1/warehouses", warehouse);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = (await create.Content.ReadFromJsonAsync<Warehouse>())!;

        await fixture.ExecuteAsync(
            "INSERT INTO `location` (`warehouse_id`, `code`, `name`, `created_at`, `updated_at`) " +
            "VALUES (@warehouseId, @code, @name, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))",
            ("@warehouseId", created.Id),
            ("@code", $"LOC-{Guid.NewGuid():N}"),
            ("@name", "Test Location"));

        var response = await client.GetAsync(
            $"/api/v1/warehouses/{created.Id}/locations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var locations = await response.Content.ReadFromJsonAsync<List<Location>>();

        Assert.NotNull(locations);

        var location = Assert.Single(locations);

        Assert.Equal(created.Id, location.WarehouseId);
        Assert.Equal("Test Location", location.Name);

        await fixture.ExecuteAsync(
            "DELETE FROM `location` WHERE `warehouse_id` = @id",
            ("@id", created.Id));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync(
                $"/api/v1/warehouses/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteWarehouse_WithLocations_ReturnsBadRequestAndPreservesRecords()
    {
        var client = fixture.Client;

        var warehouse = ValidWarehouse();
        warehouse.Code = $"TEST-{Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync(
            "/api/v1/warehouses", warehouse);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = (await create.Content.ReadFromJsonAsync<Warehouse>())!;

        await fixture.ExecuteAsync(
            "INSERT INTO `location` (`warehouse_id`, `code`, `name`, `created_at`, `updated_at`) " +
            "VALUES (@warehouseId, @code, @name, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))",
            ("@warehouseId", created.Id),
            ("@code", $"LOC-{Guid.NewGuid():N}"),
            ("@name", "Referenced Location"));

        var response = await client.DeleteAsync(
            $"/api/v1/warehouses/{created.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.Equal(
            1,
            await fixture.QueryIntAsync(
                "SELECT COUNT(*) FROM `warehouse` WHERE `id` = @id",
                ("@id", created.Id)));

        Assert.Equal(
            1,
            await fixture.QueryIntAsync(
                "SELECT COUNT(*) FROM `location` WHERE `warehouse_id` = @id",
                ("@id", created.Id)));

        await fixture.ExecuteAsync(
            "DELETE FROM `location` WHERE `warehouse_id` = @id",
            ("@id", created.Id));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync(
                $"/api/v1/warehouses/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task CreateWarehouse_ConcurrentRequests_CreateDistinctRecords()
    {
        var client = fixture.Client;

        var createdTasks = Enumerable.Range(0, 10).Select(async index =>
        {
            var warehouse = ValidWarehouse();
            warehouse.Code = $"CONCURRENT-{Guid.NewGuid():N}-{index}";

            var response = await client.PostAsJsonAsync(
                "/api/v1/warehouses", warehouse);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            return (await response.Content.ReadFromJsonAsync<Warehouse>())!;
        });

        var created = await Task.WhenAll(createdTasks);

        Assert.Equal(10, created.Select(w => w.Id).Distinct().Count());
        Assert.All(created, w => Assert.True(w.Id > 0));

        Assert.Equal(
            10,
            await fixture.QueryIntAsync(
                "SELECT COUNT(*) FROM `warehouse` WHERE `id` IN (" +
                string.Join(",", created.Select(w => w.Id)) + ")"));

        foreach (var warehouse in created)
        {
            Assert.Equal(
                HttpStatusCode.NoContent,
                (await client.DeleteAsync(
                    $"/api/v1/warehouses/{warehouse.Id}")).StatusCode);
        }
    }

    [Fact]
    public async Task GetMissingWarehouse_ReturnsNotFound()
    {
        var client = fixture.Client;

        var response = await client.GetAsync(
            "/api/v1/warehouses/2147483647");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetLocationsForMissingWarehouse_ReturnsNotFound()
    {
        var client = fixture.Client;

        var response = await client.GetAsync(
            "/api/v1/warehouses/2147483647/locations");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateWarehouse_InvalidInput_ReturnsBadRequestAndDoesNotChangeDatabase()
    {
        var client = fixture.Client;

        var warehouse = ValidWarehouse();
        warehouse.Code = $"TEST-{Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync(
            "/api/v1/warehouses", warehouse);

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = (await create.Content.ReadFromJsonAsync<Warehouse>())!;
        var originalName = created.Name;
        created.Name = "";

        var response = await client.PutAsJsonAsync(
            $"/api/v1/warehouses/{created.Id}", created);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(originalName, await fixture.QueryStringAsync(
            "SELECT `name` FROM `warehouse` WHERE `id` = @id", ("@id", created.Id)));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync(
                $"/api/v1/warehouses/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task UpdateAndDeleteMissingWarehouse_ReturnNotFound()
    {
        var client = fixture.Client;

        var warehouse = ValidWarehouse();

        var update = await client.PutAsJsonAsync(
            "/api/v1/warehouses/2147483647", warehouse);

        var delete = await client.DeleteAsync(
            "/api/v1/warehouses/2147483647");

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    public static IEnumerable<object[]> RequiredFields()
    {
        yield return [nameof(Warehouse.Code)];
        yield return [nameof(Warehouse.Name)];
        yield return [nameof(Warehouse.Address)];
        yield return [nameof(Warehouse.City)];
        yield return [nameof(Warehouse.ZipCode)];
        yield return [nameof(Warehouse.Province)];
        yield return [nameof(Warehouse.Country)];
        yield return [nameof(Warehouse.ContactName)];
        yield return [nameof(Warehouse.ContactPhone)];
        yield return [nameof(Warehouse.ContactEmail)];
    }

    public static IEnumerable<object[]> StringFields()
    {
        foreach (var field in RequiredFields())
        {
            yield return field;
        }
    }

    [Theory]
    [MemberData(nameof(RequiredFields))]
    public async Task CreateAsync_EmptyRequiredField_ThrowsArgumentException(string field)
    {
        var warehouse = ValidWarehouse();
        SetField(warehouse, field, "");

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().CreateAsync(warehouse));

        Assert.Contains(field, exception.Message);
    }

    [Theory]
    [MemberData(nameof(StringFields))]
    public async Task CreateAsync_FieldOver255Characters_ThrowsArgumentException(string field)
    {
        var warehouse = ValidWarehouse();
        SetField(warehouse, field, new string('x', 256));

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().CreateAsync(warehouse));

        Assert.Contains(field, exception.Message);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing-at-sign.example.com")]
    [InlineData("person@")]
    [InlineData("@example.com")]
    public async Task CreateAsync_InvalidEmail_ThrowsArgumentException(string email)
    {
        var warehouse = ValidWarehouse();
        warehouse.ContactEmail = email;

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().CreateAsync(warehouse));

        Assert.Contains(nameof(Warehouse.ContactEmail), exception.Message);
    }

    [Fact]
    public async Task UpdateAsync_EmptyRequiredField_ThrowsBeforeDatabaseAccess()
    {
        var warehouse = ValidWarehouse();
        warehouse.Name = " ";

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().UpdateAsync(1, warehouse));

        Assert.Contains(nameof(Warehouse.Name), exception.Message);
    }

    [Fact]
    public async Task UpdateAsync_InvalidEmail_ThrowsBeforeDatabaseAccess()
    {
        var warehouse = ValidWarehouse();
        warehouse.ContactEmail = "invalid";

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().UpdateAsync(1, warehouse));

        Assert.Contains(nameof(Warehouse.ContactEmail), exception.Message);
    }

    private static WarehousesService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());

        return new WarehousesService(
            factory,
            new MySqlRepository<Warehouse>(factory),
            new MySqlRepository<Location>(factory));
    }

    private static void SetField(Warehouse warehouse, string field, string value)
    {
        var property = typeof(Warehouse).GetProperty(field)
            ?? throw new ArgumentException($"Unknown warehouse field: {field}");

        property.SetValue(warehouse, value);
    }

    private static Warehouse ValidWarehouse() => new()
    {
        Code = "ENDPOINT-TEST",
        Name = "Endpoint Test Warehouse",
        Address = "Test Street 1",
        City = "Rotterdam",
        ZipCode = "3000AA",
        Province = "Zuid-Holland",
        Country = "Netherlands",
        ContactName = "Endpoint Tester",
        ContactPhone = "0101234567",
        ContactEmail = "endpoint@example.com"
    };

    private static Warehouse WithEmail(Warehouse warehouse, string email)
    {
        warehouse.ContactEmail = email;
        return warehouse;
    }
}