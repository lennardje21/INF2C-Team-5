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
    private static readonly string[] RequiredFields =
    [
        nameof(Location.Code),
        nameof(Location.Name)
    ];

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

    private static Location LocationWith(string field, string? value)
    {
        var location = ValidLocation();
        typeof(Location).GetProperty(field)!.SetValue(location, value);
        return location;
    }

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

        Assert.Equal("WarehouseId must be greater than 0.", message);
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public async Task CreateAsync_MissingRequiredField_IsRejected(string field)
    {
        foreach (var value in new[] { null, string.Empty, "   " })
        {
            var message = await AssertInvalidAsync(() => _service.CreateAsync(LocationWith(field, value)));

            Assert.Equal($"Missing required fields: {field}.", message);
        }
    }

    public static TheoryData<string> Fields => [.. RequiredFields];

    [Fact]
    public async Task CreateAsync_SeveralMissingFields_ListsAllOfThem()
    {
        var location = new Location { WarehouseId = 1 };

        var message = await AssertInvalidAsync(() => _service.CreateAsync(location));

        Assert.Equal("Missing required fields: Code, Name.", message);
    }

    [Fact]
    public async Task CreateAsync_TrimsFields()
    {
        var location = ValidLocation();
        location.Code = " LOC-001 ";
        location.Name = "\tReceiving Area\n";

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(location));

        Assert.Equal(("LOC-001", "Receiving Area"), (location.Code, location.Name));
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
        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(LocationWith(field, new string('a', 256))));

        Assert.Equal($"Fields must not exceed 255 characters: {field}.", message);
    }

    [Theory]
    [InlineData(nameof(Location.Code), "LOC\n001")]
    [InlineData(nameof(Location.Name), "Receiving\tArea")]
    public async Task CreateAsync_ControlCharacterInField_IsRejected(string field, string value)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(LocationWith(field, value)));

        Assert.Equal(
            $"Fields must not contain control characters such as line breaks or tabs: {field}.",
            message);
    }

    [Theory]
    [InlineData(nameof(Location.Code), "VGH-AMB-B12-R6-B7")]
    [InlineData(nameof(Location.Code), "A1")]
    [InlineData(nameof(Location.Name), "Zone B Aisle 12 Rack 6 Bin 7")]
    [InlineData(nameof(Location.Name), "Koelcel 2 (Zuid)")]
    [InlineData(nameof(Location.Name), "Retour & Inbound")]
    [InlineData(nameof(Location.Name), "Étage 2 / Zuid")]
    public async Task CreateAsync_ValidFormat_PassesValidation(string field, string value)
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(LocationWith(field, value)));
    }

    public static TheoryData<string, string, string> InvalidFormats => new()
    {
        {
            nameof(Location.Code), "VGH--AMB",
            "Code may only contain letters and digits, optionally in groups separated by single hyphens, " +
            "such as 'VGH-AMB-B12-R6-B7'."
        },
        {
            nameof(Location.Code), "VGH_AMB",
            "Code may only contain letters and digits, optionally in groups separated by single hyphens, " +
            "such as 'VGH-AMB-B12-R6-B7'."
        },
        {
            nameof(Location.Name), "_Zone 1",
            "Name must start with a letter or digit and may only contain letters, digits, spaces and the " +
            "characters - . / ' & ( ), such as 'Zone B Aisle 12 Rack 6 Bin 7'."
        },
        {
            nameof(Location.Name), "Zone: 1",
            "Name must start with a letter or digit and may only contain letters, digits, spaces and the " +
            "characters - . / ' & ( ), such as 'Zone B Aisle 12 Rack 6 Bin 7'."
        }
    };

    [Theory]
    [MemberData(nameof(InvalidFormats))]
    public async Task CreateAsync_InvalidFormat_IsRejected(
        string field, string value, string expected)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(LocationWith(field, value)));

        Assert.Equal(expected, message);
    }

    [Fact]
    public async Task CreateAsync_SeveralFormatErrors_ListsAllOfThem()
    {
        var location = ValidLocation();
        location.Code = "VGH--AMB";
        location.Name = "_Zone 1";

        var message = await AssertInvalidAsync(() => _service.CreateAsync(location));

        Assert.Equal(
            "Code may only contain letters and digits, optionally in groups separated by single hyphens, " +
            "such as 'VGH-AMB-B12-R6-B7'. " +
            "Name must start with a letter or digit and may only contain letters, digits, spaces and the " +
            "characters - . / ' & ( ), such as 'Zone B Aisle 12 Rack 6 Bin 7'.",
            message);
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

        Assert.Equal("WarehouseId must be greater than 0.", message);
    }

    [Fact]
    public async Task UpdateAsync_InvalidFormat_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.UpdateAsync(1, LocationWith(nameof(Location.Code), "VGH--AMB")));

        Assert.StartsWith("Code may only contain letters and digits", message);
    }
}
