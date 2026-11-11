using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Unit;

/// <summary>
/// Tests location validation without a database. Valid input is asserted to reach
/// the database connection, while invalid input is rejected before any connection.
/// Database behaviour is covered by LocationsControllerTests.
/// </summary>
public sealed class LocationsServiceTests
{
    private readonly LocationsService _service = CreateService();

    // Builds the service without a connection string, so database access throws.
    private static LocationsService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());
        return new LocationsService(factory, new MySqlRepository<Location>(factory));
    }

    // A location that passes every validation check.
    private static Location ValidLocation() => new()
    {
        WarehouseId = 1,
        Code = "LOC-001",
        Name = "Receiving Area"
    };

    // Asserts the input passed validation and reached the database.
    private static async Task AssertReachesDatabaseAsync(Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Contains("ConnectionStrings__CargoHub", exception.Message);
    }

    // Asserts the input was rejected and returns the error message.
    private static async Task<string> AssertInvalidAsync(Func<Task> action) =>
        (await Assert.ThrowsAsync<ArgumentException>(action)).Message;

    [Fact]
    public async Task CreateAsync_ValidLocation_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidLocation()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateAsync_WarehouseIdNotPositive_IsRejected(int warehouseId)
    {
        var location = ValidLocation();
        location.WarehouseId = warehouseId;

        var message = await AssertInvalidAsync(() => _service.CreateAsync(location));

        Assert.Equal("WarehouseId must be greater than zero.", message);
    }

    [Theory]
    [InlineData(nameof(Location.Code))]
    [InlineData(nameof(Location.Name))]
    public async Task CreateAsync_MissingRequiredField_IsRejected(string field)
    {
        foreach (var value in new[] { string.Empty, "   " })
        {
            var location = ValidLocation();
            typeof(Location).GetProperty(field)!.SetValue(location, value);

            var message = await AssertInvalidAsync(() => _service.CreateAsync(location));

            Assert.Equal($"Missing required fields: {field}.", message);
        }
    }

    [Fact]
    public async Task CreateAsync_SeveralMissingFields_ListsAllOfThem()
    {
        var location = new Location { WarehouseId = 1 };

        var message = await AssertInvalidAsync(() => _service.CreateAsync(location));

        Assert.Equal("Missing required fields: Code, Name.", message);
    }

    [Theory]
    [InlineData(nameof(Location.Code))]
    [InlineData(nameof(Location.Name))]
    public async Task CreateAsync_TextOf255Characters_PassesValidation(string field)
    {
        var location = ValidLocation();
        typeof(Location).GetProperty(field)!.SetValue(location, new string('a', 255));

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(location));
    }

    [Theory]
    [InlineData(nameof(Location.Code))]
    [InlineData(nameof(Location.Name))]
    public async Task CreateAsync_TextLongerThan255Characters_IsRejected(string field)
    {
        var location = ValidLocation();
        typeof(Location).GetProperty(field)!.SetValue(location, new string('a', 256));

        var message = await AssertInvalidAsync(() => _service.CreateAsync(location));

        Assert.Equal($"Fields must not exceed 255 characters: {field}.", message);
    }

    [Fact]
    public async Task CreateAsync_IgnoresIdAndTimestampsFromTheRequest()
    {
        var location = ValidLocation();
        location.Id = 42;
        location.CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        location.UpdatedAt = location.CreatedAt;
        var before = DateTime.UtcNow;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(location));

        Assert.Equal(0, location.Id);
        Assert.True(location.CreatedAt >= before);
        Assert.Equal(location.CreatedAt, location.UpdatedAt);
    }

    [Fact]
    public async Task UpdateAsync_ValidLocation_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, ValidLocation()));
    }

    [Fact]
    public async Task UpdateAsync_InvalidLocation_IsRejected()
    {
        var location = ValidLocation();
        location.WarehouseId = 0;

        var message = await AssertInvalidAsync(() => _service.UpdateAsync(1, location));

        Assert.Equal("WarehouseId must be greater than zero.", message);
    }
}
