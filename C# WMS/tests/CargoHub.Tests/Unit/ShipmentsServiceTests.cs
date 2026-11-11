using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Unit;

/// <summary>
/// Tests the validation in ShipmentsService, which runs before the database is used.
/// The service has no connection string, so input that passes validation fails with
/// InvalidOperationException when it opens a connection, and invalid input fails with
/// ArgumentException before that.
/// </summary>
public sealed class ShipmentsServiceTests
{
    private readonly ShipmentsService _service = CreateService();

    private static ShipmentsService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());
        return new ShipmentsService(factory,
            new MySqlRepository<Shipment>(factory),
            new MySqlRepository<ShipmentItem>(factory));
    }

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

    private static List<ShipmentItem> Items(int count) =>
        Enumerable.Range(1, count).Select(i => Item(i, 1)).ToList();

    /// <summary>Asserts that the input passed validation and the service tried to open the database.</summary>
    private static async Task AssertReachesDatabaseAsync(Func<Task> action)
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Contains("ConnectionStrings__CargoHub", e.Message);
    }

    private static async Task<string> AssertInvalidAsync(Func<Task> action) =>
        (await Assert.ThrowsAsync<ArgumentException>(action)).Message;

    // Create: valid input

    [Fact]
    public async Task CreateAsync_ValidShipment_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidShipment(Item(10, 2))));
    }

    [Fact]
    public async Task CreateAsync_WithoutItems_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidShipment()));
    }

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

    [Fact]
    public async Task CreateAsync_DateWithoutTimeZone_IsTreatedAsUtc()
    {
        var shipment = ValidShipment();
        shipment.ShipmentDate = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Unspecified);

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));

        Assert.Equal(DateTimeKind.Utc, shipment.ShipmentDate.Kind);
        Assert.Equal(new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc), shipment.ShipmentDate);
    }

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

    // Create: required fields

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

    [Fact]
    public async Task CreateAsync_MissingShipmentDate_IsRejected()
    {
        var shipment = ValidShipment();
        shipment.ShipmentDate = default;

        var message = await AssertInvalidAsync(() => _service.CreateAsync(shipment));

        Assert.Equal("Missing required fields: ShipmentDate.", message);
    }

    [Fact]
    public async Task CreateAsync_SeveralMissingFields_ListsAllOfThem()
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(new Shipment { OrderId = 3 }));

        Assert.Equal("Missing required fields: ShipmentType, ShipmentStatus, CarrierName, " +
            "ShippingMethod, PaymentType, ShipmentDate.", message);
    }

    // Create: field length

    [Fact]
    public async Task CreateAsync_TextOf255Characters_PassesValidation()
    {
        var shipment = ValidShipment();
        shipment.CarrierName = new string('a', 255);

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));
    }

    [Fact]
    public async Task CreateAsync_TextLongerThan255Characters_IsRejected()
    {
        var shipment = ValidShipment();
        shipment.CarrierName = new string('a', 256);

        var message = await AssertInvalidAsync(() => _service.CreateAsync(shipment));

        Assert.Equal("Fields must not exceed 255 characters: CarrierName.", message);
    }

    [Fact]
    public async Task CreateAsync_LengthIsCheckedAfterTrimming()
    {
        var shipment = ValidShipment();
        shipment.CarrierName = $"  {new string('a', 255)}  ";

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));
    }

    // Create: allowed values

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

    // Create: order id and date

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

    [Theory]
    [InlineData("2000-01-01T00:00:00")]
    [InlineData("2099-12-31T23:59:59")]
    public async Task CreateAsync_DateWithinRange_PassesValidation(string date)
    {
        var shipment = ValidShipment();
        shipment.ShipmentDate = DateTime.SpecifyKind(DateTime.Parse(date), DateTimeKind.Utc);

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));
    }

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

    // Create: items

    [Fact]
    public async Task CreateAsync_100Items_PassesValidation()
    {
        var shipment = ValidShipment();
        shipment.Items = Items(100);

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(shipment));
    }

    [Fact]
    public async Task CreateAsync_MoreThan100Items_IsRejected()
    {
        var shipment = ValidShipment();
        shipment.Items = Items(101);

        var message = await AssertInvalidAsync(() => _service.CreateAsync(shipment));

        Assert.Equal("A shipment can have at most 100 items.", message);
    }

    [Fact]
    public async Task CreateAsync_NullItem_IsRejected()
    {
        var shipment = ValidShipment(Item(10, 1));
        shipment.Items.Add(null!);

        var message = await AssertInvalidAsync(() => _service.CreateAsync(shipment));

        Assert.Equal("Items must not contain null.", message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task CreateAsync_ItemIdNotPositive_IsRejected(int itemId)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(ValidShipment(Item(itemId, 1))));

        Assert.Equal("Every item needs an itemId greater than 0.", message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1_000_000)]
    public async Task CreateAsync_AmountWithinRange_PassesValidation(int amount)
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidShipment(Item(10, amount))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public async Task CreateAsync_AmountOutsideRange_IsRejected(int amount)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(ValidShipment(Item(10, amount))));

        Assert.Equal("Every item needs an amount between 1 and 1000000.", message);
    }

    [Fact]
    public async Task CreateAsync_DuplicateItem_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ValidShipment(Item(10, 1), Item(11, 1), Item(10, 2))));

        Assert.Equal("An item can only appear once in a shipment.", message);
    }

    [Fact]
    public async Task CreateAsync_InvalidShipmentAndItems_ReportsTheShipmentFirst()
    {
        var shipment = ValidShipment(Item(0, 0));
        shipment.OrderId = 0;

        var message = await AssertInvalidAsync(() => _service.CreateAsync(shipment));

        Assert.Equal("OrderId must be greater than 0.", message);
    }

    // Update

    [Fact]
    public async Task UpdateAsync_ValidShipment_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, ValidShipment()));
    }

    [Fact]
    public async Task UpdateAsync_InvalidShipment_IsRejected()
    {
        var shipment = ValidShipment();
        shipment.ShipmentStatus = "Lost";

        var message = await AssertInvalidAsync(() => _service.UpdateAsync(1, shipment));

        Assert.StartsWith("ShipmentStatus must be one of", message);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotValidateItems()
    {
        // Items are changed through UpdateItemsAsync, so UpdateAsync ignores them.
        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, ValidShipment(Item(0, 0))));
    }

    // Update orders

    [Fact]
    public async Task UpdateOrdersAsync_OneOrder_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateOrdersAsync(1, [4]));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 4, 5 })]
    public async Task UpdateOrdersAsync_NotExactlyOneOrder_IsRejected(int[] orderIds)
    {
        var message = await AssertInvalidAsync(() => _service.UpdateOrdersAsync(1, orderIds));

        Assert.Equal("A shipment must belong to exactly one order.", message);
    }

    // Update items

    [Fact]
    public async Task UpdateItemsAsync_ValidItems_PassValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateItemsAsync(1, [Item(20, 10), Item(22, 5)]));
    }

    [Fact]
    public async Task UpdateItemsAsync_EmptyList_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateItemsAsync(1, []));
    }

    public static TheoryData<ShipmentItem[], string> InvalidItems => new()
    {
        { [Item(20, 1), null!], "Items must not contain null." },
        { [.. Items(101)], "A shipment can have at most 100 items." },
        { [Item(0, 1)], "Every item needs an itemId greater than 0." },
        { [Item(20, 0)], "Every item needs an amount between 1 and 1000000." },
        { [Item(20, 1_000_001)], "Every item needs an amount between 1 and 1000000." },
        { [Item(20, 1), Item(20, 2)], "An item can only appear once in a shipment." }
    };

    [Theory]
    [MemberData(nameof(InvalidItems))]
    public async Task UpdateItemsAsync_InvalidItems_AreRejected(ShipmentItem[] items, string expected)
    {
        var message = await AssertInvalidAsync(() => _service.UpdateItemsAsync(1, items));

        Assert.Equal(expected, message);
    }
}
