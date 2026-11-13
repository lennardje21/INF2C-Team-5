using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Unit;

/// <summary>
/// Tests the validation in OrdersService without a database: valid input fails with
/// InvalidOperationException when it tries to connect, invalid input with ArgumentException.
/// Database behaviour is covered by OrdersControllerTests.
/// </summary>
public sealed class OrdersServiceTests
{
    private readonly OrdersService _service = CreateService();

    /// <summary>Builds the service without a connection string, so database access throws.</summary>
    private static OrdersService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());
        return new OrdersService(factory,
            new MySqlRepository<Order>(factory),
            new MySqlRepository<OrderItem>(factory));
    }

    /// <summary>An order that passes every check.</summary>
    private static Order ValidOrder(params OrderItem[] items) => new()
    {
        ClientId = 1,
        OrderDate = Utc(2026, 10, 12, 10),
        RequestDate = Utc(2026, 10, 19, 10),
        CustomerPoNumber = "PO-12345",
        OrderStatus = "Pending",
        ShippingNotes = "Deliver at the back entrance.",
        WarehouseId = 1,
        ShipToClientId = 1,
        BillToClientId = 1,
        Items = [.. items]
    };

    /// <summary>A valid order with one field changed by name.</summary>
    private static Order OrderWith(string field, object? value)
    {
        var order = ValidOrder();
        typeof(Order).GetProperty(field)!.SetValue(order, value);
        return order;
    }

    private static OrderItem Item(int itemId, int amount, decimal unitPrice = 9.99m) =>
        new() { ItemId = itemId, Amount = amount, UnitPrice = unitPrice };

    private static DateTime Utc(int year, int month, int day, int hour = 0) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

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

    // A complete order with an item passes validation.
    [Fact]
    public async Task CreateAsync_ValidOrder_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidOrder(Item(10, 2))));
    }

    // The server sets the id and timestamps; values from the request are ignored.
    [Fact]
    public async Task CreateAsync_IgnoresIdAndTimestampsFromTheRequest()
    {
        var order = ValidOrder();
        order.Id = 42;
        order.CreatedAt = Utc(2000, 1, 1);
        order.UpdatedAt = order.CreatedAt;
        var before = DateTime.UtcNow;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(order));

        Assert.Equal(0, order.Id);
        Assert.True(order.CreatedAt >= before);
        Assert.Equal(order.CreatedAt, order.UpdatedAt);
    }

    // A date without a time zone keeps its clock time as UTC; a local date is converted to UTC.
    [Fact]
    public async Task CreateAsync_StoresBothDatesAsUtc()
    {
        var localDate = new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Local);
        var order = ValidOrder();
        order.OrderDate = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Unspecified);
        order.RequestDate = localDate;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(order));

        Assert.Equal(Utc(2026, 10, 12, 10), order.OrderDate);
        Assert.Equal(DateTimeKind.Utc, order.OrderDate.Kind);
        Assert.Equal(localDate.ToUniversalTime(), order.RequestDate);
        Assert.Equal(DateTimeKind.Utc, order.RequestDate.Kind);
    }

    // Null, empty or blank text fields are rejected, naming the field.
    [Theory]
    [InlineData(nameof(Order.CustomerPoNumber))]
    [InlineData(nameof(Order.OrderStatus))]
    public async Task CreateAsync_MissingTextField_IsRejected(string field)
    {
        foreach (var value in new[] { null, "", "   " })
        {
            var message = await AssertInvalidAsync(() => _service.CreateAsync(OrderWith(field, value)));

            Assert.Equal($"Missing required fields: {field}.", message);
        }
    }

    // An unset (default) date is rejected, naming the field.
    [Theory]
    [InlineData(nameof(Order.OrderDate))]
    [InlineData(nameof(Order.RequestDate))]
    public async Task CreateAsync_MissingDate_IsRejected(string field)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(OrderWith(field, default(DateTime))));

        Assert.Equal($"Missing required fields: {field}.", message);
    }

    // All missing fields are listed in one message.
    [Fact]
    public async Task CreateAsync_SeveralMissingFields_ListsAllOfThem()
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(new Order()));

        Assert.Equal("Missing required fields: CustomerPoNumber, OrderStatus, OrderDate, RequestDate.", message);
    }

    // Shipping notes and items are optional, and an amount of 1 or a price of 0 is allowed.
    [Fact]
    public async Task CreateAsync_OptionalFieldsAndLowestValues_PassValidation()
    {
        var order = ValidOrder(Item(10, 1, 0m));
        order.ShippingNotes = null;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(order));
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidOrder()));
    }

    // One invalid item list per item rule, with the error it should give.
    public static TheoryData<OrderItem[], string> InvalidItems => new()
    {
        { [Item(10, 0)], "Every item needs an amount greater than 0." },
        { [Item(10, -1)], "Every item needs an amount greater than 0." },
        { [Item(10, 1, -0.01m)], "Unit prices cannot be negative." },
        { [Item(10, 1), Item(11, 1), Item(10, 2)], "An item can only appear once in an order." }
    };

    // Each invalid item list is rejected with the error for that rule.
    [Theory]
    [MemberData(nameof(InvalidItems))]
    public async Task CreateAsync_InvalidItems_AreRejected(OrderItem[] items, string expected)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(ValidOrder(items)));

        Assert.Equal(expected, message);
    }

    // ---- Update (same field checks as create) ----

    // A valid order passes; its items are ignored, since they change through UpdateItemsAsync.
    [Fact]
    public async Task UpdateAsync_ValidOrder_PassesValidationAndIgnoresItems()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, ValidOrder(Item(10, 0))));
    }

    // An invalid field is rejected; the individual rules are tested under create.
    [Fact]
    public async Task UpdateAsync_InvalidOrder_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.UpdateAsync(1, OrderWith(nameof(Order.OrderStatus), " ")));

        Assert.Equal("Missing required fields: OrderStatus.", message);
    }

    // ---- Update items (same item checks as create) ----

    // A valid list passes, and an empty list is allowed (it removes all items).
    [Fact]
    public async Task UpdateItemsAsync_ValidItems_PassValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateItemsAsync(1, [Item(20, 10), Item(22, 5, 0m)]));
        await AssertReachesDatabaseAsync(() => _service.UpdateItemsAsync(1, []));
    }

    // An invalid list is rejected; the individual rules are tested under create.
    [Fact]
    public async Task UpdateItemsAsync_InvalidItems_AreRejected()
    {
        var message = await AssertInvalidAsync(() => _service.UpdateItemsAsync(1, [Item(20, 1), Item(20, 2)]));

        Assert.Equal("An item can only appear once in an order.", message);
    }
}
