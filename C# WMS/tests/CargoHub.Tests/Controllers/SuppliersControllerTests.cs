using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Models;
using CargoHub.Tests.Fixtures;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class SuppliersControllerTests(ApiFixture fixture)
{
    private const string Url = "/api/v1/suppliers";
    private readonly HttpClient _client = fixture.Client;

    // A dictionary, so a test can change or add single fields.
    private static Dictionary<string, object> NewSupplier(string name = "Jumbo Test") => new()
    {
        ["name"] = name,
        ["address"] = "Teststraat 1",
        ["city"] = "Rotterdam",
        ["zipCode"] = "3011AA",
        ["province"] = "Zuid-Holland",
        ["country"] = "Netherlands",
        ["contactName"] = "Test Persoon",
        ["phoneNumber"] = "010-1234567",
        ["code"] = "SUP-TEST",
        ["reference"] = "TEST-REF"
    };

    private async Task<Supplier> CreateAsync(object supplier)
    {
        var response = await _client.PostAsJsonAsync(Url, supplier);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Supplier>())!;
    }

    [Fact]
    public async Task GetSuppliers_ReturnsSeededSuppliers()
    {
        var suppliers = await _client.GetFromJsonAsync<List<Supplier>>(Url);
        var expected = await fixture.QueryIntAsync("SELECT COUNT(*) FROM `supplier`");

        Assert.NotNull(suppliers);
        Assert.Equal(expected, suppliers.Count);
        Assert.NotEmpty(suppliers);
    }

    [Fact]
    public async Task GetSupplier_ReturnsTheSupplier()
    {
        var created = await CreateAsync(NewSupplier());
        var supplier = await _client.GetFromJsonAsync<Supplier>($"{Url}/{created.Id}");

        Assert.NotNull(supplier);
        Assert.Equal(created.Id, supplier.Id);
        Assert.Equal(created.Name, supplier.Name);
        Assert.Equal(created.CreatedAt, supplier.CreatedAt);
    }

    [Fact]
    public async Task GetSupplier_UnknownSupplier_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"{Url}/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateSupplier_StoresTheSupplier()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        var response = await _client.PostAsJsonAsync(Url, NewSupplier());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<Supplier>())!;
        Assert.Equal($"{Url}/{created.Id}", response.Headers.Location!.AbsolutePath);
        Assert.True(created.Id > 0);
        Assert.True(created.CreatedAt >= before);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);

        var stored = await _client.GetFromJsonAsync<Supplier>($"{Url}/{created.Id}");
        Assert.Equal("Jumbo Test", stored!.Name);
        Assert.Equal("010-1234567", stored.PhoneNumber);
        Assert.Equal("SUP-TEST", stored.Code);
        Assert.Equal("TEST-REF", stored.Reference);
    }

    [Fact]
    public async Task CreateSupplier_IgnoresIdAndTimestampsFromTheRequest()
    {
        var supplier = NewSupplier();
        supplier["id"] = 1;
        supplier["createdAt"] = "2000-01-01T00:00:00Z";

        var created = await CreateAsync(supplier);

        Assert.NotEqual(1, created.Id);
        Assert.NotEqual(2000, created.CreatedAt.Year);
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("city", "  ")]
    [InlineData("address", "")]
    [InlineData("zipCode", "")]
    [InlineData("province", "")]
    [InlineData("country", "")]
    [InlineData("contactName", "")]
    [InlineData("phoneNumber", "")]
    [InlineData("code", "")]
    [InlineData("reference", "")]
    public async Task CreateSupplier_MissingField_ReturnsBadRequest(string field, string value)
    {
        var supplier = NewSupplier();
        supplier[field] = value;

        var response = await _client.PostAsJsonAsync(Url, supplier);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSupplier_ChangesFieldsButKeepsCreatedAt()
    {
        var created = await CreateAsync(NewSupplier());
        await Task.Delay(10);

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", NewSupplier(name: "Jumbo Changed"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await _client.GetFromJsonAsync<Supplier>($"{Url}/{created.Id}");
        Assert.Equal("Jumbo Changed", updated!.Name);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt > created.UpdatedAt);
    }

    [Fact]
    public async Task UpdateSupplier_UnknownSupplier_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"{Url}/999999", NewSupplier());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSupplier_MissingField_ReturnsBadRequest()
    {
        var created = await CreateAsync(NewSupplier());

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", NewSupplier(name: ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSupplier_RemovesTheSupplier()
    {
        var created = await CreateAsync(NewSupplier());

        var response = await _client.DeleteAsync($"{Url}/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{Url}/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteSupplier_WithItems_ReturnsConflictAndKeepsTheSupplier()
    {
        var id = await fixture.QueryIntAsync("SELECT supplier_id FROM `item` LIMIT 1");
        var response = await _client.DeleteAsync($"{Url}/{id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"{Url}/{id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteSupplier_UnknownSupplier_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"{Url}/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
