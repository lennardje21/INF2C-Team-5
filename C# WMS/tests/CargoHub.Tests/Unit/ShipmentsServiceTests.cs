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

    private static ShipmentItem Item(int itemId, int amount) => new() { ItemId = itemId, Amount = amount };

    /// <summary>Builds the given number of valid items with different item ids.</summary>
    private static List<ShipmentItem> Items(int count) =>
        Enumerable.Range(1, count).Select(i => Item(i, 1)).ToList();

    /// <summary>Asserts the input passed validation and reached the database.</summary>
    private static async Task AssertReachesDatabaseAsync(Func<Task> action)
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Contains("ConnectionStrings__CargoHub", e.Message);
    }

    /// <summary>Asserts the input was rejected and returns the error message.</summary>
    private static async Task<string> AssertInvalidAsync(Func<Task> action) =>
        (await Assert.ThrowsAsync<ArgumentException>(action)).Message;

    // ---- Create: valid input ----

    // A complete shipment with an item passes validation.
    [Fact]
    public async Task CreateAsync_ValidShipment_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidShipment(Item(10, 2))));
    }

    // Items are optional when creating a shipment.
    [Fact]
    public async Task CreateAsync_WithoutItems_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidShipment()));
    }

    // Whitespace around text fields is removed before saving.
    [Fact]
    public async Task CreateAsync_TrimsTextFields()
    {
        var shipment = ValidShipment();
        shipment.ShipmentType = "  Outgoing ";
        shipment.ShipmentStatus = "\tScheduled";
        shipment.CarrierName = " DHL Parcel ";
        shipment.ShippingMethod = "Express\n";
        shipment.PaymentType = " Manual";

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));

        Assert.Equal(
            ("Outgoing", "Scheduled", "DHL Parcel", "Express", "Manual"),
            (shipment.ShipmentType, shipment.ShipmentStatus, shipment.CarrierName,
                shipment.ShippingMethod, shipment.PaymentType));
    }

    // The server sets the id and timestamps; values from the request are ignored.
    [Fact]
    public async Task CreateAsync_IgnoresIdAndTimestampsFromTheRequest()
    {
        var shipment = ValidShipment();
        shipment.Id = 42;
        shipment.CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        shipment.UpdatedAt = shipment.CreatedAt;
        var before = DateTime.UtcNow;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));

        Assert.Equal(0, shipment.Id);
        Assert.True(shipment.CreatedAt >= before);
        Assert.Equal(shipment.CreatedAt, shipment.UpdatedAt);
    }

    // A date without a time zone keeps its clock time and is marked as UTC.
    [Fact]
    public async Task CreateAsync_DateWithoutTimeZone_IsTreatedAsUtc()
    {
        var shipment = ValidShipment();
        shipment.ShipmentDate = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Unspecified);

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));

        Assert.Equal(DateTimeKind.Utc, shipment.ShipmentDate.Kind);
        Assert.Equal(new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc), shipment.ShipmentDate);
    }

    // A local date is converted to UTC; works in any time zone.
    [Fact]
    public async Task CreateAsync_LocalDate_IsConvertedToUtc()
    {
        var local = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Local);
        var shipment = ValidShipment();
        shipment.ShipmentDate = local;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));

        Assert.Equal(DateTimeKind.Utc, shipment.ShipmentDate.Kind);
        Assert.Equal(local.ToUniversalTime(), shipment.ShipmentDate);
    }

    // ---- Create: required fields ----

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
            var shipment = ValidShipment();
            typeof(Shipment).GetProperty(field)!.SetValue(shipment, value);

            var message = await AssertInvalidAsync(() => _service.CreateAsync(shipment));

            Assert.Equal($"Missing required fields: {field}.", message);
        }
    }

    // A default (unset) date counts as missing.
    [Fact]
    public async Task CreateAsync_MissingShipmentDate_IsRejected()
    {
        var shipment = ValidShipment();
        shipment.ShipmentDate = default;

        var message = await AssertInvalidAsync(() => _service.CreateAsync(shipment));

        Assert.Equal("Missing required fields: ShipmentDate.", message);
    }

    // All missing fields are listed in one message.
    [Fact]
    public async Task CreateAsync_SeveralMissingFields_ListsAllOfThem()
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(new Shipment { OrderId = 3 }));

        Assert.Equal("Missing required fields: ShipmentType, ShipmentStatus, CarrierName, " +
            "ShippingMethod, PaymentType, ShipmentDate.", message);
    }

    // ---- Create: field length (database columns hold 255 characters) ----

    // Exactly 255 characters is allowed.
    [Fact]
    public async Task CreateAsync_TextOf255Characters_PassesValidation()
    {
        var shipment = ValidShipment();
        shipment.CarrierName = new string('a', 255);

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));
    }

    // 256 characters is rejected, naming the field.
    [Fact]
    public async Task CreateAsync_TextLongerThan255Characters_IsRejected()
    {
        var shipment = ValidShipment();
        shipment.CarrierName = new string('a', 256);

        var message = await AssertInvalidAsync(() => _service.CreateAsync(shipment));

        Assert.Equal("Fields must not exceed 255 characters: CarrierName.", message);
    }

    // Surrounding spaces don't count towards the limit, since they are trimmed first.
    [Fact]
    public async Task CreateAsync_LengthIsCheckedAfterTrimming()
    {
        var shipment = ValidShipment();
        shipment.CarrierName = $"  {new string('a', 255)}  ";

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));
    }

    // ---- Create: allowed values ----

    // Every allowed type, status, shipping method and payment type is accepted.
    [Theory]
    [InlineData("Incoming", "Scheduled", "Standard", "Automated")]
    [InlineData("Outgoing", "Transit", "Express", "Manual")]
    [InlineData("Outgoing", "Delivered", "Overnight", "Manual")]
    [InlineData("Incoming", "Delivered", "Freight", "Automated")]
    public async Task CreateAsync_KnownValues_PassValidation(string type, string status, string method, string payment)
    {
        var shipment = ValidShipment();
        shipment.ShipmentType = type;
        shipment.ShipmentStatus = status;
        shipment.ShippingMethod = method;
        shipment.PaymentType = payment;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));
    }

    // Unknown values and wrong casing are rejected; the message lists the allowed values.
    [Theory]
    [InlineData(nameof(Shipment.ShipmentType), "Sideways", "Incoming, Outgoing")]
    [InlineData(nameof(Shipment.ShipmentType), "outgoing", "Incoming, Outgoing")]
    [InlineData(nameof(Shipment.ShipmentStatus), "Lost", "Scheduled, Transit, Delivered")]
    [InlineData(nameof(Shipment.ShipmentStatus), "SCHEDULED", "Scheduled, Transit, Delivered")]
    [InlineData(nameof(Shipment.ShippingMethod), "Teleport", "Standard, Express, Overnight, Freight")]
    [InlineData(nameof(Shipment.PaymentType), "Cash", "Manual, Automated")]
    public async Task CreateAsync_UnknownValue_IsRejected(string field, string value, string allowed)
    {
        var shipment = ValidShipment();
        typeof(Shipment).GetProperty(field)!.SetValue(shipment, value);

        var message = await AssertInvalidAsync(() => _service.CreateAsync(shipment));

        Assert.Equal($"{field} must be one of: {allowed}.", message);
    }

    // ---- Create: order id and date ----

    // An order id of 0 or less is rejected; whether the order exists is checked by the database.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateAsync_OrderIdNotPositive_IsRejected(int orderId)
    {
        var shipment = ValidShipment();
        shipment.OrderId = orderId;

        var message = await AssertInvalidAsync(() => _service.CreateAsync(shipment));

        Assert.Equal("OrderId must be greater than 0.", message);
    }

    // The first and last moments of the allowed range (2000 to 2099) are accepted.
    [Theory]
    [InlineData("2000-01-01T00:00:00")]
    [InlineData("2099-12-31T23:59:59")]
    public async Task CreateAsync_DateWithinRange_PassesValidation(string date)
    {
        var shipment = ValidShipment();
        shipment.ShipmentDate = DateTime.SpecifyKind(DateTime.Parse(date), DateTimeKind.Utc);

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));
    }

    // One second outside the range on either side is rejected.
    [Theory]
    [InlineData("1999-12-31T23:59:59")]
    [InlineData("2100-01-01T00:00:00")]
    public async Task CreateAsync_DateOutsideRange_IsRejected(string date)
    {
        var shipment = ValidShipment();
        shipment.ShipmentDate = DateTime.SpecifyKind(DateTime.Parse(date), DateTimeKind.Utc);

        var message = await AssertInvalidAsync(() => _service.CreateAsync(shipment));

        Assert.Equal("ShipmentDate must be between 2000 and 2099.", message);
    }

    // ---- Create: items ----

    // Exactly 100 items is allowed.
    [Fact]
    public async Task CreateAsync_100Items_PassesValidation()
    {
        var shipment = ValidShipment();
        shipment.Items = Items(100);

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));
    }

    // More than 100 items is rejected.
    [Fact]
    public async Task CreateAsync_MoreThan100Items_IsRejected()
    {
        var shipment = ValidShipment();
        shipment.Items = Items(101);

        var message = await AssertInvalidAsync(() => _service.CreateAsync(shipment));

        Assert.Equal("A shipment can have at most 100 items.", message);
    }

    // A null entry in the item list gives a clear error instead of a crash.
    [Fact]
    public async Task CreateAsync_NullItem_IsRejected()
    {
        var shipment = ValidShipment(Item(10, 1));
        shipment.Items.Add(null!);

        var message = await AssertInvalidAsync(() => _service.CreateAsync(shipment));

        Assert.Equal("Items must not contain null.", message);
    }

    // An item id of 0 or less is rejected.
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task CreateAsync_ItemIdNotPositive_IsRejected(int itemId)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(ValidShipment(Item(itemId, 1))));

        Assert.Equal("Every item needs an itemId greater than 0.", message);
    }

    // The lowest (1) and highest (1,000,000) amounts are accepted.
    [Theory]
    [InlineData(1)]
    [InlineData(1_000_000)]
    public async Task CreateAsync_AmountWithinRange_PassesValidation(int amount)
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidShipment(Item(10, amount))));
    }

    // Amounts below 1 or above 1,000,000 are rejected.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public async Task CreateAsync_AmountOutsideRange_IsRejected(int amount)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(ValidShipment(Item(10, amount))));

        Assert.Equal("Every item needs an amount between 1 and 1000000.", message);
    }

    // The same item twice is rejected, even when the duplicates aren't next to each other.
    [Fact]
    public async Task CreateAsync_DuplicateItem_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ValidShipment(Item(10, 1), Item(11, 1), Item(10, 2))));

        Assert.Equal("An item can only appear once in a shipment.", message);
    }

    // The shipment's fields are checked before its items.
    [Fact]
    public async Task CreateAsync_InvalidShipmentAndItems_ReportsTheShipmentFirst()
    {
        var shipment = ValidShipment(Item(0, 0));
        shipment.OrderId = 0;

        var message = await AssertInvalidAsync(() => _service.CreateAsync(shipment));

        Assert.Equal("OrderId must be greater than 0.", message);
    }

    // ---- Update (same field checks as create) ----

    // A complete shipment passes validation.
    [Fact]
    public async Task UpdateAsync_ValidShipment_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, ValidShipment()));
    }

    // An invalid field is rejected; the individual rules are tested under create.
    [Fact]
    public async Task UpdateAsync_InvalidShipment_IsRejected()
    {
        var shipment = ValidShipment();
        shipment.ShipmentStatus = "Lost";

        var message = await AssertInvalidAsync(() => _service.UpdateAsync(1, shipment));

        Assert.StartsWith("ShipmentStatus must be one of", message);
    }

    // Items are ignored here; they are changed through UpdateItemsAsync.
    [Fact]
    public async Task UpdateAsync_DoesNotValidateItems()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, ValidShipment(Item(0, 0))));
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

    // A valid list of items passes validation.
    [Fact]
    public async Task UpdateItemsAsync_ValidItems_PassValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateItemsAsync(1, [Item(20, 10), Item(22, 5)]));
    }

    // An empty list is allowed and removes all items.
    [Fact]
    public async Task UpdateItemsAsync_EmptyList_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateItemsAsync(1, []));
    }

    // One invalid list per item rule, with the message it should give.
    public static TheoryData<ShipmentItem[], string> InvalidItems => new()
    {
        { [Item(20, 1), null!], "Items must not contain null." },
        { [.. Items(101)], "A shipment can have at most 100 items." },
        { [Item(0, 1)], "Every item needs an itemId greater than 0." },
        { [Item(20, 0)], "Every item needs an amount between 1 and 1000000." },
        { [Item(20, 1_000_001)], "Every item needs an amount between 1 and 1000000." },
        { [Item(20, 1), Item(20, 2)], "An item can only appear once in a shipment." }
    };

    // Each invalid list is rejected before the database is touched.
    [Theory]
    [MemberData(nameof(InvalidItems))]
    public async Task UpdateItemsAsync_InvalidItems_AreRejected(ShipmentItem[] items, string expected)
    {
        var message = await AssertInvalidAsync(() => _service.UpdateItemsAsync(1, items));

        Assert.Equal(expected, message);
    }
}
