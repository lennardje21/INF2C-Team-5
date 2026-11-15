using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Unit;

// Tests inventory validation without a database. Valid input is asserted to reach
// the database connection, while invalid input is rejected before any connection.
// Database behaviour is covered by InventoriesControllerTests.
public sealed class InventoriesServiceTests
{
    private readonly InventoriesService _service = CreateService();

    // Builds the service without a connection string, so database access throws.
    private static InventoriesService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());
        return new InventoriesService(factory, new MySqlRepository<Inventory>(factory));
    }

    // An inventory that passes every validation check.
    private static Inventory ValidInventory() => new()
    {
        ItemId = 1,
        LocationId = 1,
        QuantityOnHand = 10,
        QuantityExpected = 5,
        QuantityOrdered = 3,
        QuantityAllocated = 2
    };

    private static Inventory InventoryWith(string field, int value)
    {
        var inventory = ValidInventory();
        typeof(Inventory).GetProperty(field)!.SetValue(inventory, value);
        return inventory;
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
    public async Task CreateAsync_ValidInventory_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidInventory()));
    }

    [Theory]
    [InlineData(nameof(Inventory.ItemId), 0)]
    [InlineData(nameof(Inventory.ItemId), -1)]
    [InlineData(nameof(Inventory.LocationId), 0)]
    [InlineData(nameof(Inventory.LocationId), -1)]
    public async Task CreateAsync_IdNotPositive_IsRejected(string field, int value)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(InventoryWith(field, value)));

        Assert.Equal($"Ids must be greater than 0: {field}.", message);
    }

    [Fact]
    public async Task CreateAsync_BothIdsMissing_ListsBoth()
    {
        var inventory = ValidInventory();
        inventory.ItemId = 0;
        inventory.LocationId = 0;

        var message = await AssertInvalidAsync(() => _service.CreateAsync(inventory));

        Assert.Equal("Ids must be greater than 0: ItemId, LocationId.", message);
    }

    [Theory]
    [InlineData(nameof(Inventory.QuantityExpected))]
    [InlineData(nameof(Inventory.QuantityOrdered))]
    [InlineData(nameof(Inventory.QuantityAllocated))]
    public async Task CreateAsync_NegativeQuantity_IsRejected(string field)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(InventoryWith(field, -1)));

        Assert.Equal($"Quantities cannot be negative: {field}.", message);
    }

    [Fact]
    public async Task CreateAsync_NegativeOnHand_ListsBothErrors()
    {
        // On hand -1 is negative and also lower than the 2 allocated.
        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(InventoryWith(nameof(Inventory.QuantityOnHand), -1)));

        Assert.Equal(
            "Quantities cannot be negative: QuantityOnHand. QuantityAllocated cannot be greater than QuantityOnHand.",
            message);
    }

    [Fact]
    public async Task CreateAsync_AllQuantitiesZero_PassesValidation()
    {
        var inventory = new Inventory { ItemId = 1, LocationId = 1 };

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(inventory));
    }

    // All negative quantities are listed in one message, in order.
    [Fact]
    public async Task CreateAsync_SeveralNegativeQuantities_ListsAllOfThem()
    {
        var inventory = ValidInventory();
        inventory.QuantityExpected = -1;
        inventory.QuantityOrdered = -2;

        var message = await AssertInvalidAsync(() => _service.CreateAsync(inventory));

        Assert.Equal("Quantities cannot be negative: QuantityExpected, QuantityOrdered.", message);
    }

    // The server sets the timestamps (in UTC); values from the request are ignored.
    [Fact]
    public async Task CreateAsync_IgnoresTimestampsFromTheRequest()
    {
        var inventory = ValidInventory();
        inventory.CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        inventory.UpdatedAt = inventory.CreatedAt;
        var before = DateTime.UtcNow;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(inventory));

        Assert.True(inventory.CreatedAt >= before);
        Assert.Equal(DateTimeKind.Utc, inventory.CreatedAt.Kind);
        Assert.Equal(inventory.CreatedAt, inventory.UpdatedAt);
    }

    // A complete inventory also passes validation on update.
    [Fact]
    public async Task UpdateAsync_ValidInventory_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, 1, ValidInventory()));
    }

    [Fact]
    public async Task CreateAsync_AllocatedMoreThanOnHand_IsRejected()
    {
        var inventory = ValidInventory();
        inventory.QuantityAllocated = 11;

        var message = await AssertInvalidAsync(() => _service.CreateAsync(inventory));

        Assert.Equal("QuantityAllocated cannot be greater than QuantityOnHand.", message);
    }

    [Fact]
    public async Task CreateAsync_AllocatedEqualToOnHand_PassesValidation()
    {
        var inventory = ValidInventory();
        inventory.QuantityAllocated = 10;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(inventory));
    }

    [Fact]
    public async Task CreateAsync_OrderedMoreThanOnHand_PassesValidation()
    {
        // Ordered stock can be more than what is on hand (backorder), so this is allowed.
        var inventory = ValidInventory();
        inventory.QuantityOrdered = 50;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(inventory));
    }

    [Fact]
    public async Task UpdateAsync_IdsMissingInBody_PassesValidation()
    {
        // On update the keys come from the URL, so the body does not need them.
        var inventory = ValidInventory();
        inventory.ItemId = 0;
        inventory.LocationId = 0;

        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, 1, inventory));
    }

    [Fact]
    public async Task UpdateAsync_AllocatedMoreThanOnHand_IsRejected()
    {
        var inventory = ValidInventory();
        inventory.QuantityAllocated = 11;

        var message = await AssertInvalidAsync(() => _service.UpdateAsync(1, 1, inventory));

        Assert.Equal("QuantityAllocated cannot be greater than QuantityOnHand.", message);
    }

    [Fact]
    public async Task UpdateAsync_NegativeQuantity_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.UpdateAsync(1, 1, InventoryWith(nameof(Inventory.QuantityExpected), -1)));

        Assert.Equal("Quantities cannot be negative: QuantityExpected.", message);
    }
}