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
    public async Task InsertAndUpdateReturnTheSameTimestampPrecisionAsMySql()
    {
        var factory = Factory();
        var repository = new MySqlRepository<ItemGroup>(factory);
        await using var connection = await factory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var timestamp = new DateTime(2026, 10, 28, 10, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
        var group = new ItemGroup
        {
            Name = "Timestamp precision test", Description = "Test",
            CreatedAt = timestamp, UpdatedAt = timestamp
        };

        await repository.InsertAsync(group, connection, transaction);
        var filters = new Dictionary<string, object?> { ["id"] = group.Id };
        var stored = Assert.Single(await repository.GetAsync(connection, transaction, filters));
        Assert.Equal(timestamp.AddTicks(-7), group.CreatedAt);
        Assert.Equal(group.CreatedAt, stored.CreatedAt);
        Assert.Equal(group.UpdatedAt, stored.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, group.CreatedAt.Kind);

        group.UpdatedAt = timestamp.AddTicks(12);
        await repository.UpdateAsync(group, connection, transaction);
        stored = Assert.Single(await repository.GetAsync(connection, transaction, filters));
        Assert.Equal(timestamp.AddTicks(3), group.UpdatedAt);
        Assert.Equal(group.UpdatedAt, stored.UpdatedAt);
        Assert.Equal(group.CreatedAt, stored.CreatedAt);
        await transaction.RollbackAsync();
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

    // Paging, filtering and sorting. These insert rows dated in 2099 inside a transaction that is
    // rolled back, so the window from 2099-01-01 holds exactly the rows of the test and no seed data.

    private static readonly PageRequest Year2099 = new(From: new DateOnly(2099, 1, 1), To: new DateOnly(2099, 12, 31));

    private static DateTime Utc(int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        new(2099, month, day, hour, minute, second, DateTimeKind.Utc);

    private static async Task<ItemGroup> InsertGroupAsync(MySqlRepository<ItemGroup> repository,
        MySqlConnection connection, MySqlTransaction transaction, DateTime updatedAt, string? name = null)
    {
        var group = new ItemGroup
        {
            Name = name ?? $"Paging test {Guid.NewGuid():N}", Description = "Paging test",
            CreatedAt = updatedAt, UpdatedAt = updatedAt
        };
        await repository.InsertAsync(group, connection, transaction);
        return group;
    }

    // Five groups around the edges of 31 January 2099: the last microsecond of the day before,
    // midnight, noon, the last microsecond of the day, and midnight of the day after.
    private static async Task<ItemGroup[]> InsertDayEdgesAsync(MySqlRepository<ItemGroup> repository,
        MySqlConnection connection, MySqlTransaction transaction)
    {
        var lastMicrosecond = TimeSpan.TicksPerSecond - 10;
        return
        [
            await InsertGroupAsync(repository, connection, transaction, Utc(1, 30, 23, 59, 59).AddTicks(lastMicrosecond)),
            await InsertGroupAsync(repository, connection, transaction, Utc(1, 31)),
            await InsertGroupAsync(repository, connection, transaction, Utc(1, 31, 12)),
            await InsertGroupAsync(repository, connection, transaction, Utc(1, 31, 23, 59, 59).AddTicks(lastMicrosecond)),
            await InsertGroupAsync(repository, connection, transaction, Utc(2, 1))
        ];
    }

    private static List<int> Ids(IEnumerable<ItemGroup> groups) => groups.Select(group => group.Id).ToList();

    [MySqlFact]
    public async Task PagedGetReturnsTheRequestedSliceAndTheTotal()
    {
        var factory = Factory();
        var repository = new MySqlRepository<ItemGroup>(factory);
        await using var connection = await factory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var groups = await InsertDayEdgesAsync(repository, connection, transaction);

        var first = await repository.GetAsync(connection, transaction, Year2099 with { Size = 2, Sort = SortDirection.Asc });
        var second = await repository.GetAsync(connection, transaction, Year2099 with { Page = 2, Size = 2, Sort = SortDirection.Asc });
        var third = await repository.GetAsync(connection, transaction, Year2099 with { Page = 3, Size = 2, Sort = SortDirection.Asc });

        Assert.Equal(Ids(groups[..2]), Ids(first.Items));
        Assert.Equal(Ids(groups[2..4]), Ids(second.Items));
        Assert.Equal(Ids(groups[4..]), Ids(third.Items));
        Assert.All(new[] { first, second, third }, page => Assert.Equal(5, page.Total));
        Assert.All(new[] { first, second, third }, page => Assert.Equal(3, page.TotalPages));
        Assert.Equal([1, 2, 3], new[] { first.Page, second.Page, third.Page });
        await transaction.RollbackAsync();
    }

    [MySqlFact]
    public async Task PagedGetPastTheEndReturnsNoRowsButTheTotal()
    {
        var factory = Factory();
        var repository = new MySqlRepository<ItemGroup>(factory);
        await using var connection = await factory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await InsertDayEdgesAsync(repository, connection, transaction);

        var past = await repository.GetAsync(connection, transaction, Year2099 with { Page = 4, Size = 2 });
        var huge = await repository.GetAsync(connection, transaction, Year2099 with { Page = int.MaxValue, Size = 100 });

        Assert.Empty(past.Items);
        Assert.Equal((5, 3, 4), (past.Total, past.TotalPages, past.Page));
        Assert.Empty(huge.Items);
        Assert.Equal(5, huge.Total);
        await transaction.RollbackAsync();
    }

    [MySqlFact]
    public async Task DefaultsAreTheFirstPageNewestFirst()
    {
        var factory = Factory();
        var repository = new MySqlRepository<ItemGroup>(factory);
        await using var connection = await factory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var groups = await InsertDayEdgesAsync(repository, connection, transaction);

        var page = await repository.GetAsync(connection, transaction, new PageRequest());

        // The rows of 2099 are newer than anything in the seed.
        Assert.Equal(Ids(groups.AsEnumerable().Reverse()), Ids(page.Items.Take(5)));
        Assert.Equal((1, 50), (page.Page, page.Size));
        Assert.Equal(8, page.Total);
        await transaction.RollbackAsync();
    }

    [MySqlFact]
    public async Task ToCountsTheWholeDayAndFromStartsAtMidnight()
    {
        var factory = Factory();
        var repository = new MySqlRepository<ItemGroup>(factory);
        await using var connection = await factory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var groups = await InsertDayEdgesAsync(repository, connection, transaction);
        var day = new DateOnly(2099, 1, 31);

        var oneDay = await repository.GetAsync(connection, transaction,
            new PageRequest(From: day, To: day, Sort: SortDirection.Asc));
        var upToTheDay = await repository.GetAsync(connection, transaction,
            Year2099 with { To = day, Sort = SortDirection.Asc });
        var fromTheDay = await repository.GetAsync(connection, transaction,
            Year2099 with { From = day, Sort = SortDirection.Asc });

        // Midnight, noon and the last microsecond of the day count; the neighbouring days do not.
        Assert.Equal(Ids(groups[1..4]), Ids(oneDay.Items));
        Assert.Equal(3, oneDay.Total);
        Assert.Equal(Ids(groups[..4]), Ids(upToTheDay.Items));
        Assert.Equal(Ids(groups[1..]), Ids(fromTheDay.Items));
        await transaction.RollbackAsync();
    }

    [MySqlFact]
    public async Task FromAndToAreOptionalAndIndependent()
    {
        var factory = Factory();
        var repository = new MySqlRepository<ItemGroup>(factory);
        await using var connection = await factory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var groups = await InsertDayEdgesAsync(repository, connection, transaction);

        var onlyFrom = await repository.GetAsync(connection, transaction,
            new PageRequest(From: new DateOnly(2099, 2, 1), Sort: SortDirection.Asc));
        var onlyTo = await repository.GetAsync(connection, transaction,
            new PageRequest(To: new DateOnly(1999, 12, 31)));
        var nothing = await repository.GetAsync(connection, transaction,
            new PageRequest(From: new DateOnly(2100, 1, 1)));

        Assert.Equal([groups[4].Id], Ids(onlyFrom.Items));
        Assert.DoesNotContain(onlyTo.Items, group => group.UpdatedAt.Year == 2099);
        Assert.Empty(nothing.Items);
        Assert.Equal((0, 0), (nothing.Total, nothing.TotalPages));
        await transaction.RollbackAsync();
    }

    [MySqlFact]
    public async Task SortingOnUpdatedAtGoesBothWays()
    {
        var factory = Factory();
        var repository = new MySqlRepository<ItemGroup>(factory);
        await using var connection = await factory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        // Inserted out of order, so the key order cannot pass for the updated_at order.
        var newest = await InsertGroupAsync(repository, connection, transaction, Utc(5, 1));
        var oldest = await InsertGroupAsync(repository, connection, transaction, Utc(3, 1));
        var middle = await InsertGroupAsync(repository, connection, transaction, Utc(4, 1));

        var asc = await repository.GetAsync(connection, transaction, Year2099 with { Sort = SortDirection.Asc });
        var desc = await repository.GetAsync(connection, transaction, Year2099 with { Sort = SortDirection.Desc });

        Assert.Equal([oldest.Id, middle.Id, newest.Id], Ids(asc.Items));
        Assert.Equal([newest.Id, middle.Id, oldest.Id], Ids(desc.Items));
        await transaction.RollbackAsync();
    }

    [MySqlFact]
    public async Task RowsWithTheSameUpdatedAtAreOrderedByKeySoPagesNeverOverlap()
    {
        var factory = Factory();
        var repository = new MySqlRepository<ItemGroup>(factory);
        await using var connection = await factory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var groups = new List<ItemGroup>();
        for (var i = 0; i < 5; i++)
            groups.Add(await InsertGroupAsync(repository, connection, transaction, Utc(6, 1)));

        var desc = new List<int>();
        var asc = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            desc.AddRange(Ids((await repository.GetAsync(connection, transaction,
                Year2099 with { Page = page, Size = 2 })).Items));
            asc.AddRange(Ids((await repository.GetAsync(connection, transaction,
                Year2099 with { Page = page, Size = 2, Sort = SortDirection.Asc })).Items));
        }

        Assert.Equal(Ids(groups.AsEnumerable().Reverse()), desc);
        Assert.Equal(Ids(groups), asc);
        await transaction.RollbackAsync();
    }

    [MySqlFact]
    public async Task PagedGetCombinesWithEqualityAndIdListFilters()
    {
        var factory = Factory();
        var repository = new MySqlRepository<ItemGroup>(factory);
        await using var connection = await factory.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var groups = await InsertDayEdgesAsync(repository, connection, transaction);

        var byName = await repository.GetAsync(connection, transaction, Year2099,
            new Dictionary<string, object?> { ["name"] = groups[2].Name });
        var byIds = await repository.GetAsync(connection, transaction, Year2099 with { Sort = SortDirection.Asc },
            new Dictionary<string, object?> { ["id"] = new[] { groups[1].Id, groups[3].Id, groups[4].Id } });
        var byNoIds = await repository.GetAsync(connection, transaction, Year2099,
            new Dictionary<string, object?> { ["id"] = Array.Empty<int>() });

        Assert.Equal([groups[2].Id], Ids(byName.Items));
        Assert.Equal(1, byName.Total);
        Assert.Equal([groups[1].Id, groups[3].Id, groups[4].Id], Ids(byIds.Items));
        Assert.Equal(3, byIds.Total);
        Assert.Empty(byNoIds.Items);
        Assert.Equal(0, byNoIds.Total);
        await transaction.RollbackAsync();
    }

    [MySqlFact]
    public async Task UnpagedGetAcceptsAListOfIds()
    {
        var factory = Factory();
        var repository = new MySqlRepository<ItemGroup>(factory);

        var rows = await repository.GetAsync(new Dictionary<string, object?> { ["id"] = new[] { 1, 3, 999999 } });

        Assert.Equal([1, 3], Ids(rows));
    }

    [MySqlFact]
    public async Task PagedGetCountsAllRowsOfTheSeed()
    {
        var factory = Factory();

        var orders = await new MySqlRepository<Order>(factory).GetAsync(new PageRequest());
        var shipments = await new MySqlRepository<Shipment>(factory).GetAsync(new PageRequest(Size: 100));

        Assert.Equal(4854, orders.Total);
        Assert.Equal(98, orders.TotalPages);
        Assert.Equal(50, orders.Items.Count);
        Assert.Equal(6132, shipments.Total);
        Assert.Equal(62, shipments.TotalPages);
        Assert.Equal(100, shipments.Items.Count);
    }

    [MySqlFact]
    public async Task PagedGetOrdersARowWithACompositeKeyByBothKeyColumns()
    {
        var factory = Factory();
        var repository = new MySqlRepository<Inventory>(factory);
        var all = await repository.GetAsync();

        var page = await repository.GetAsync(new PageRequest(Size: 25));

        var expected = all.OrderByDescending(row => row.UpdatedAt)
            .ThenByDescending(row => row.ItemId).ThenByDescending(row => row.LocationId).Take(25)
            .Select(row => (row.ItemId, row.LocationId)).ToList();
        Assert.Equal(expected, page.Items.Select(row => (row.ItemId, row.LocationId)).ToList());
        Assert.Equal(all.Count, page.Total);
    }

    [MySqlFact]
    public async Task PagedGetRejectsARequestOutsideTheLimits()
    {
        var repository = new MySqlRepository<ItemGroup>(Factory());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetAsync(new PageRequest(Page: 0)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetAsync(new PageRequest(Size: 0)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetAsync(new PageRequest(Size: 101)));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetAsync(
            new PageRequest(From: new DateOnly(2026, 2, 1), To: new DateOnly(2026, 1, 1))));
    }

    [MySqlFact]
    public async Task PagedGetNeedsAnUpdatedAtColumn()
    {
        var repository = new MySqlRepository<OrderItem>(Factory());

        await Assert.ThrowsAsync<NotSupportedException>(() => repository.GetAsync(new PageRequest()));
    }
}
