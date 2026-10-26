using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Services;

public sealed class LocationsServiceTests
{
    private static LocationsService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());

        return new LocationsService(
            factory,
            new MySqlRepository<Location>(factory));
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
