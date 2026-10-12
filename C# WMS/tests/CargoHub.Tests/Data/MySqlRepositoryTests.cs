using CargoHub.Api.Data;
using CargoHub.Api.Models;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace CargoHub.Tests.Data;

public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CARGOHUB_TEST_MYSQL")))
            Skip = "Set CARGOHUB_TEST_MYSQL to an isolated database containing the seed.";
    }
}

public sealed class MySqlRepositoryTests
{
    private static MySqlConnectionFactory Factory() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CargoHub"] = Environment.GetEnvironmentVariable("CARGOHUB_TEST_MYSQL")
        }).Build());

    private static async Task Count<T>(int expected) where T : class, new() =>
        Assert.Equal(expected, (await new MySqlRepository<T>(Factory()).GetAsync()).Count);

    [MySqlFact]
    public async Task AllModelsReadTheImportedRecords()
    {
        await Count<Warehouse>(10); await Count<Client>(300); await Count<Supplier>(21);
        await Count<ItemLine>(14); await Count<ItemGroup>(3); await Count<ItemType>(3);
        await Count<Location>(400); await Count<Item>(600); await Count<Inventory>(4800);
        await Count<Order>(4854); await Count<OrderItem>(26498); await Count<Shipment>(6132);
        await Count<ShipmentItem>(33660); await Count<Transfer>(800); await Count<TransferItem>(2370);
    }

    [MySqlFact]
    public async Task CrudAndRollbackPreserveTheSeed()
    {
        var factory = Factory();
        var repository = new MySqlRepository<ItemGroup>(factory);
        await using var connection = await factory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var group = new ItemGroup
        {
            Name = "O'Brien 🚚", Description = "Test", CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await repository.InsertAsync(group, connection, transaction);
        Assert.True(group.Id > 3);
        group.Name = "Updated";
        await repository.UpdateAsync(group, connection, transaction);
        // An unchanged UPDATE must still count as a matched row.
        await repository.UpdateAsync(group, connection, transaction);
        var filters = new Dictionary<string, object?> { ["id"] = group.Id };
        var rows = await repository.GetAsync(connection, transaction, filters);
        Assert.Equal("Updated", Assert.Single(rows).Name);
        Assert.Equal(DateTimeKind.Utc, rows[0].CreatedAt.Kind);
        await repository.DeleteAsync(group, connection, transaction);
        Assert.Empty(await repository.GetAsync(connection, transaction, filters));
        await transaction.RollbackAsync();
        Assert.Equal(3, (await repository.GetAsync()).Count);
    }

    [MySqlFact]
    public async Task InventoryUsesBothKeyColumnsAndRollsBack()
    {
        var factory = Factory();
        var repository = new MySqlRepository<Inventory>(factory);
        await using var connection = await factory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var row = (await repository.GetAsync(connection, transaction))[0];
        var original = row.QuantityOnHand;
        row.QuantityOnHand++;
        await repository.UpdateAsync(row, connection, transaction);
        var filters = new Dictionary<string, object?>
        {
            ["item_id"] = row.ItemId, ["location_id"] = row.LocationId
        };
        Assert.Equal(original + 1,
            Assert.Single(await repository.GetAsync(connection, transaction, filters)).QuantityOnHand);
        await transaction.RollbackAsync();
        Assert.Equal(original, Assert.Single(await repository.GetAsync(filters)).QuantityOnHand);
    }

    [MySqlFact]
    public async Task ForeignKeysRejectAnInvalidLocation()
    {
        var factory = Factory();
        var repository = new MySqlRepository<Location>(factory);
        await using var connection = await factory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var invalid = new Location
        {
            WarehouseId = -1, Code = "Test", Name = "Test",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        var error = await Assert.ThrowsAsync<MySqlException>(() =>
            repository.InsertAsync(invalid, connection, transaction));
        Assert.Equal(1452, error.Number);
        await transaction.RollbackAsync();
    }

    [MySqlFact]
    public async Task SpecialPropertyDecimalsAndNullsMapCorrectly()
    {
        var factory = Factory();
        var items = await new MySqlRepository<Item>(factory).GetAsync();
        await using var connection = await factory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT min_purchase_qty, unit_weight FROM item WHERE id = @id";
        command.Parameters.AddWithValue("@id", items[0].Id);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(reader.GetInt32(0), items[0].MinPurchaseQuantity);
            Assert.Equal(reader.GetDecimal(1), items[0].UnitWeight);
        }
        var orders = await new MySqlRepository<Order>(factory).GetAsync(
            new Dictionary<string, object?> { ["shipping_notes"] = null });
        Assert.NotEmpty(orders);
        Assert.All(orders, order => Assert.Null(order.ShippingNotes));
    }
}
