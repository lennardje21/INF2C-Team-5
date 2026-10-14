using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Services;

public sealed class WarehousesServiceTests
{
    private static WarehousesService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());

        return new WarehousesService(
            factory,
            new MySqlRepository<Warehouse>(factory),
            new MySqlRepository<Location>(factory));
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
    public async Task CreateAsync_EmptyRequiredField_ThrowsArgumentException(
        string field)
    {
        var warehouse = ValidWarehouse();
        SetField(warehouse, field, "");

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().CreateAsync(warehouse));

        Assert.Contains(field, exception.Message);
    }

    [Theory]
    [MemberData(nameof(StringFields))]
    public async Task CreateAsync_FieldOver255Characters_ThrowsArgumentException(
        string field)
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

        Assert.Contains("ContactEmail", exception.Message);
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

    private static Warehouse ValidWarehouse() => new()
    {
        Code = "UNIT-TEST",
        Name = "Unit Test Warehouse",
        Address = "Test Street 1",
        City = "Rotterdam",
        ZipCode = "3000AA",
        Province = "Zuid-Holland",
        Country = "Netherlands",
        ContactName = "Test Contact",
        ContactPhone = "0101234567",
        ContactEmail = "test@example.com"
    };

    private static void SetField(Warehouse warehouse, string field, string value)
    {
        var property = typeof(Warehouse).GetProperty(field)
            ?? throw new ArgumentException($"Unknown warehouse field: {field}");

        property.SetValue(warehouse, value);
    }
}