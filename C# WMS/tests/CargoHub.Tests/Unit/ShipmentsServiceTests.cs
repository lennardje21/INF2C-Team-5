using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Unit;

/// <summary>
/// Tests the validation in ShipmentsService without a database: valid input fails with
/// InvalidOperationException when it tries to connect, invalid input with ArgumentException.
/// Database behaviour is covered by ShipmentsControllerTests.
/// </summary>
public sealed class ShipmentsServiceTests
{
    private readonly ShipmentsService _service = CreateService();

    /// <summary>Builds the service without a connection string, so database access throws.</summary>
    private static ShipmentsService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());
        return new ShipmentsService(factory,
            new MySqlRepository<Shipment>(factory),
            new MySqlRepository<ShipmentItem>(factory));
    }

    /// <summary>A shipment that passes every check.</summary>
    private static Shipment ValidShipment(params ShipmentItem[] items) => new()
    {
        OrderId = 3,
        ShipmentDate = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc),
        ShipmentType = "Outgoing",
        ShipmentStatus = "Scheduled",
        CarrierName = "DHL Parcel",
        ShippingMethod = "Express",
        PaymentType = "Manual",
        Items = [.. items]
    };

    /// <summary>A valid shipment with one field changed by name.</summary>
    private static Shipment ShipmentWith(string field, object? value)
    {
        var shipment = ValidShipment();
        typeof(Shipment).GetProperty(field)!.SetValue(shipment, value);
        return shipment;
    }

    private static ShipmentItem Item(int itemId, int amount) => new() { ItemId = itemId, Amount = amount };

    /// <summary>Builds the given number of valid items with different item ids.</summary>
    private static List<ShipmentItem> Items(int count) =>
        Enumerable.Range(1, count).Select(i => Item(i, 1)).ToList();

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    /// <summary>Asserts the input passed validation and reached the database.</summary>
    private static async Task AssertReachesDatabaseAsync(Func<Task> action)
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Contains("ConnectionStrings__CargoHub", e.Message);
    }

    /// <summary>Asserts the input was rejected and returns the error message.</summary>
    private static async Task<string> AssertInvalidAsync(Func<Task> action) =>
        (await Assert.ThrowsAsync<ArgumentException>(action)).Message;

    // ---- Create ----

    // A complete shipment with an item passes validation.
    [Fact]
    public async Task CreateAsync_ValidShipment_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidShipment(Item(10, 2))));
    }

    // The server sets the id and timestamps; values from the request are ignored.
    [Fact]
    public async Task CreateAsync_IgnoresIdAndTimestampsFromTheRequest()
    {
        var shipment = ValidShipment();
        shipment.Id = 42;
        shipment.CreatedAt = Utc(2000, 1, 1);
        shipment.UpdatedAt = shipment.CreatedAt;
        var before = DateTime.UtcNow;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));

        Assert.Equal(0, shipment.Id);
        Assert.True(shipment.CreatedAt >= before);
        Assert.Equal(shipment.CreatedAt, shipment.UpdatedAt);
    }

    // Whitespace around text fields is removed before saving.
    [Fact]
    public async Task CreateAsync_TrimsTextFields()
    {
        var shipment = ValidShipment();
        shipment.ShipmentType = "  Outgoing ";
        shipment.CarrierName = "\tDHL Parcel\n";
        shipment.PaymentType = " Manual";

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));

        Assert.Equal(("Outgoing", "DHL Parcel", "Manual"),
            (shipment.ShipmentType, shipment.CarrierName, shipment.PaymentType));
    }

    // A date without a time zone keeps its clock time as UTC; a local date is converted to UTC.
    [Fact]
    public async Task CreateAsync_StoresTheDateAsUtc()
    {
        var unspecified = ShipmentWith(nameof(Shipment.ShipmentDate),
            new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Unspecified));
        var localDate = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Local);
        var local = ShipmentWith(nameof(Shipment.ShipmentDate), localDate);

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(unspecified));
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(local));

        Assert.Equal(Utc(2026, 10, 12, 10), unspecified.ShipmentDate);
        Assert.Equal(DateTimeKind.Utc, unspecified.ShipmentDate.Kind);
        Assert.Equal(localDate.ToUniversalTime(), local.ShipmentDate);
        Assert.Equal(DateTimeKind.Utc, local.ShipmentDate.Kind);
    }

    // Null, empty or blank text fields are rejected, naming the field.
    [Theory]
    [InlineData(nameof(Shipment.ShipmentType))]
    [InlineData(nameof(Shipment.ShipmentStatus))]
    [InlineData(nameof(Shipment.CarrierName))]
    [InlineData(nameof(Shipment.ShippingMethod))]
    [InlineData(nameof(Shipment.PaymentType))]
    public async Task CreateAsync_MissingTextField_IsRejected(string field)
    {
        foreach (var value in new[] { null, "", "   " })
        {
            var message = await AssertInvalidAsync(() => _service.CreateAsync(ShipmentWith(field, value)));

            Assert.Equal($"Missing required fields: {field}.", message);
        }
    }

    // All missing fields are listed in one message; an unset date counts as missing.
    [Fact]
    public async Task CreateAsync_SeveralMissingFields_ListsAllOfThem()
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(new Shipment { OrderId = 3 }));

        Assert.Equal("Missing required fields: ShipmentType, ShipmentStatus, CarrierName, " +
            "ShippingMethod, PaymentType, ShipmentDate.", message);
    }

    // 255 characters is allowed; 256 is rejected, naming the field.
    [Fact]
    public async Task CreateAsync_FieldLongerThan255Characters_IsRejected()
    {
        await AssertReachesDatabaseAsync(() =>
            _service.CreateAsync(ShipmentWith(nameof(Shipment.CarrierName), new string('a', 255))));

        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ShipmentWith(nameof(Shipment.CarrierName), new string('a', 256))));

        Assert.Equal("Fields must not exceed 255 characters: CarrierName.", message);
    }

    // Allowed values not used by ValidShipment, and the first and last moments of the date range.
    public static TheoryData<string, object> ValidFields => new()
    {
        { nameof(Shipment.ShipmentType), "Incoming" },
        { nameof(Shipment.ShipmentStatus), "Transit" },
        { nameof(Shipment.ShipmentStatus), "Delivered" },
        { nameof(Shipment.ShippingMethod), "Standard" },
        { nameof(Shipment.ShippingMethod), "Overnight" },
        { nameof(Shipment.ShippingMethod), "Freight" },
        { nameof(Shipment.PaymentType), "Automated" },
        { nameof(Shipment.ShipmentDate), Utc(2000, 1, 1) },
        { nameof(Shipment.ShipmentDate), Utc(2099, 12, 31, 23, 59, 59) }
    };

    // Each valid value passes validation.
    [Theory]
    [MemberData(nameof(ValidFields))]
    public async Task CreateAsync_ValidFieldValue_PassesValidation(string field, object value)
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ShipmentWith(field, value)));
    }

    // Unknown values (the check is case-sensitive), a non-positive order id and dates outside 2000-2099.
    public static TheoryData<string, object, string> InvalidFields => new()
    {
        { nameof(Shipment.ShipmentType), "Sideways", "ShipmentType must be one of: Incoming, Outgoing." },
        { nameof(Shipment.ShipmentStatus), "SCHEDULED", "ShipmentStatus must be one of: Scheduled, Transit, Delivered." },
        { nameof(Shipment.ShippingMethod), "Teleport", "ShippingMethod must be one of: Standard, Express, Overnight, Freight." },
        { nameof(Shipment.PaymentType), "Cash", "PaymentType must be one of: Manual, Automated." },
        { nameof(Shipment.OrderId), 0, "OrderId must be greater than 0." },
        { nameof(Shipment.ShipmentDate), Utc(1999, 12, 31, 23, 59, 59), "ShipmentDate must be between 2000 and 2099." },
        { nameof(Shipment.ShipmentDate), Utc(2100, 1, 1), "ShipmentDate must be between 2000 and 2099." }
    };

    // Each invalid value is rejected with the error for that field.
    [Theory]
    [MemberData(nameof(InvalidFields))]
    public async Task CreateAsync_InvalidFieldValue_IsRejected(string field, object value, string expected)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(ShipmentWith(field, value)));

        Assert.Equal(expected, message);
    }

    // No items, exactly 100 items and an amount of 1,000,000 are all allowed.
    [Fact]
    public async Task CreateAsync_ItemsAtTheLimits_PassValidation()
    {
        var hundredItems = ValidShipment();
        hundredItems.Items = Items(100);

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidShipment()));
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(hundredItems));
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidShipment(Item(10, 1_000_000))));
    }

    // One invalid item list per item rule, with the error it should give.
    public static TheoryData<ShipmentItem[], string> InvalidItems => new()
    {
        { [Item(20, 1), null!], "Items must not contain null." },
        { [.. Items(101)], "A shipment can have at most 100 items." },
        { [Item(0, 1)], "Every item needs an itemId greater than 0." },
        { [Item(20, 0)], "Every item needs an amount between 1 and 1000000." },
        { [Item(20, 1_000_001)], "Every item needs an amount between 1 and 1000000." },
        { [Item(10, 1), Item(11, 1), Item(10, 2)], "An item can only appear once in a shipment." }
    };

    // Each invalid item list is rejected with the error for that rule.
    [Theory]
    [MemberData(nameof(InvalidItems))]
    public async Task CreateAsync_InvalidItems_AreRejected(ShipmentItem[] items, string expected)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(ValidShipment(items)));

        Assert.Equal(expected, message);
    }

    // ---- Update (same field checks as create) ----

    // A valid shipment passes; its items are ignored, since they change through UpdateItemsAsync.
    [Fact]
    public async Task UpdateAsync_ValidShipment_PassesValidationAndIgnoresItems()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, ValidShipment(Item(0, 0))));
    }

    // An invalid field is rejected; the individual rules are tested under create.
    [Fact]
    public async Task UpdateAsync_InvalidShipment_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.UpdateAsync(1, ShipmentWith(nameof(Shipment.ShipmentStatus), "Lost")));

        Assert.StartsWith("ShipmentStatus must be one of", message);
    }

    // ---- Update orders (a shipment belongs to exactly one order) ----

    // A list with one order id passes validation.
    [Fact]
    public async Task UpdateOrdersAsync_OneOrder_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateOrdersAsync(1, [4]));
    }

    // An empty list or more than one order id is rejected.
    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 4, 5 })]
    public async Task UpdateOrdersAsync_NotExactlyOneOrder_IsRejected(int[] orderIds)
    {
        var message = await AssertInvalidAsync(() => _service.UpdateOrdersAsync(1, orderIds));

        Assert.Equal("A shipment must belong to exactly one order.", message);
    }

    // ---- Update items (same item checks as create) ----

    // A valid list passes, and an empty list is allowed (it removes all items).
    [Fact]
    public async Task UpdateItemsAsync_ValidItems_PassValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateItemsAsync(1, [Item(20, 10), Item(22, 5)]));
        await AssertReachesDatabaseAsync(() => _service.UpdateItemsAsync(1, []));
    }

    // An invalid list is rejected; the individual rules are tested under create.
    [Fact]
    public async Task UpdateItemsAsync_InvalidItems_AreRejected()
    {
        var message = await AssertInvalidAsync(() => _service.UpdateItemsAsync(1, [Item(20, 1), Item(20, 2)]));

        Assert.Equal("An item can only appear once in a shipment.", message);
    }
}
