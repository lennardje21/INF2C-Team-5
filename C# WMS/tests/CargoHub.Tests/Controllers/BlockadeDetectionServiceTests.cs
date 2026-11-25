using CargoHub.Api.Data;
using CargoHub.Api.Services;
using CargoHub.Api.Settings;
using CargoHub.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace CargoHub.Tests.Controllers;

// Runs the blockade check against the test database with a fake mail service. The limits are
// the defaults: 120 minutes for an order, 240 for a shipment and a transfer. Every test makes
// its own rows (reference BLOCKTEST-...) and its own floormanagers, and removes them again.
[Collection("Api")]
public sealed class BlockadeDetectionServiceTests(ApiFixture fixture) : IAsyncLifetime
{
    private const int Warehouse = 1;
    private const int OtherWarehouse = 2;
    private const int WarehouseWithoutFloormanager = 3;

    private readonly FakeEmailService _email = new();

    public async Task InitializeAsync()
    {
        await CleanUpAsync();

        // The seed has old Pending and Scheduled items. They count as already mailed, so
        // only the rows a test makes can be found.
        foreach (var (type, table, statusColumn, status) in new[]
        {
            ("order", "order", "order_status", "Pending"),
            ("shipment", "shipment", "shipment_status", "Scheduled"),
            ("transfer", "transfer", "transfer_status", "Scheduled")
        })
        {
            await fixture.ExecuteAsync($"""
                INSERT IGNORE INTO `blockade_notification` (`item_type`, `item_id`, `item_updated_at`, `notified_at`)
                SELECT '{type}', `id`, `updated_at`, UTC_TIMESTAMP(6) FROM `{table}` WHERE `{statusColumn}` = '{status}'
                """);
        }
    }

    public Task DisposeAsync() => CleanUpAsync();

    private async Task CleanUpAsync()
    {
        await fixture.ExecuteAsync("DELETE FROM `shipment` WHERE `reference` LIKE 'BLOCKTEST-%'");
        await fixture.ExecuteAsync("DELETE FROM `transfer` WHERE `reference` LIKE 'BLOCKTEST-%'");
        await fixture.ExecuteAsync("DELETE FROM `order` WHERE `reference` LIKE 'BLOCKTEST-%'");
        // Every floormanager goes, also those other tests left behind, because the mails of
        // these tests go to all floormanagers of a warehouse.
        await fixture.ExecuteAsync("DELETE FROM `account` WHERE `role` = 'floormanager'");
        await fixture.ExecuteAsync("DELETE FROM `blockade_notification`");
        await fixture.ExecuteAsync("DELETE FROM `blockade_setting`");
    }

    private BlockadeDetectionService CreateService(int maxMailsPerCheck = 25)
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CargoHub"] = fixture.ConnectionString
            }).Build());
        var options = Options.Create(new BlockadeDetectionOptions { MaxMailsPerCheck = maxMailsPerCheck });
        return new BlockadeDetectionService(
            factory, new BlockadeSettingsService(factory, options), _email, options,
            NullLogger<BlockadeDetectionService>.Instance);
    }

    // MySQL stores microseconds, so the time is cut to that to compare it after a round trip.
    private static DateTime MinutesAgo(int minutes)
    {
        var time = DateTime.UtcNow.AddMinutes(-minutes);
        return new DateTime(time.Ticks - time.Ticks % 10, DateTimeKind.Utc);
    }

    private async Task<int> AddOrderAsync(string status, DateTime updatedAt, int warehouse = Warehouse)
    {
        var reference = $"BLOCKTEST-{Guid.NewGuid():N}";
        await fixture.ExecuteAsync("""
            INSERT INTO `order` (`client_id`, `order_date`, `request_date`, `reference`, `customer_po_number`,
                `order_status`, `shipping_notes`, `warehouse_id`, `ship_to_client_id`, `bill_to_client_id`,
                `created_at`, `updated_at`)
            VALUES (1, @updated, @updated, @reference, 'PO-1', @status, NULL, @warehouse, 1, 1, @updated, @updated)
            """,
            ("@updated", updatedAt), ("@reference", reference), ("@status", status), ("@warehouse", warehouse));
        return await fixture.QueryIntAsync("SELECT `id` FROM `order` WHERE `reference` = @reference", ("@reference", reference));
    }

    // A shipment gets the warehouse of its order.
    private async Task<int> AddShipmentAsync(int orderId, string status, DateTime updatedAt)
    {
        var reference = $"BLOCKTEST-{Guid.NewGuid():N}";
        await fixture.ExecuteAsync("""
            INSERT INTO `shipment` (`reference`, `order_id`, `shipment_date`, `shipment_type`, `shipment_status`,
                `carrier_name`, `shipping_method`, `payment_type`, `created_at`, `updated_at`)
            VALUES (@reference, @order, @updated, 'Outgoing', @status, 'Carrier', 'Road', 'Manual', @updated, @updated)
            """,
            ("@reference", reference), ("@order", orderId), ("@status", status), ("@updated", updatedAt));
        return await fixture.QueryIntAsync("SELECT `id` FROM `shipment` WHERE `reference` = @reference", ("@reference", reference));
    }

    // A transfer gets the warehouse of the location it leaves from.
    private async Task<int> AddTransferAsync(string status, DateTime updatedAt, int warehouse = Warehouse)
    {
        var reference = $"BLOCKTEST-{Guid.NewGuid():N}";
        var location = await fixture.QueryIntAsync(
            "SELECT `id` FROM `location` WHERE `warehouse_id` = @warehouse ORDER BY `id` LIMIT 1", ("@warehouse", warehouse));
        await fixture.ExecuteAsync("""
            INSERT INTO `transfer` (`reference`, `from_location_id`, `to_location_id`, `transfer_status`, `created_at`, `updated_at`)
            VALUES (@reference, @location, @location, @status, @updated, @updated)
            """,
            ("@reference", reference), ("@location", location), ("@status", status), ("@updated", updatedAt));
        return await fixture.QueryIntAsync("SELECT `id` FROM `transfer` WHERE `reference` = @reference", ("@reference", reference));
    }

    private async Task<string> AddFloormanagerAsync(int warehouse)
    {
        var email = $"blocktest-{Guid.NewGuid():N}@cargohub.nl";
        await fixture.ExecuteAsync("""
            INSERT INTO `account` (`role`, `name`, `email`, `warehouse_id`, `app`, `api_key_hash`, `created_at`, `updated_at`)
            VALUES ('floormanager', 'Blockade test floormanager', @email, @warehouse, 'integration-tests', NULL,
                    UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
            """,
            ("@email", email), ("@warehouse", warehouse));
        return email;
    }

    private Task<int> NotificationsForAsync(string type, int id) => fixture.QueryIntAsync(
        "SELECT COUNT(*) FROM `blockade_notification` WHERE `item_type` = @type AND `item_id` = @id",
        ("@type", type), ("@id", id));

    // Finding blockades

    [Fact]
    public async Task RunOnce_FindsAnOrderOverTheLimit_AndNotOneUnderIt()
    {
        var floormanager = await AddFloormanagerAsync(Warehouse);
        var blocked = await AddOrderAsync("Pending", MinutesAgo(121));
        await AddOrderAsync("Pending", MinutesAgo(119));

        var handled = await CreateService().RunOnceAsync();

        Assert.Equal(1, handled);
        var mail = Assert.Single(_email.Sent);
        Assert.Equal(floormanager, mail.To);
        Assert.Equal($"Blocked order {blocked} in warehouse {Warehouse}", mail.Subject);
    }

    [Fact]
    public async Task RunOnce_FindsAShipmentAndATransferOverTheLimit_AndNotOnesUnderIt()
    {
        await AddFloormanagerAsync(Warehouse);
        var order = await AddOrderAsync("Shipped", MinutesAgo(10));
        var blockedShipment = await AddShipmentAsync(order, "Scheduled", MinutesAgo(241));
        await AddShipmentAsync(order, "Scheduled", MinutesAgo(239));
        var blockedTransfer = await AddTransferAsync("Scheduled", MinutesAgo(241));
        await AddTransferAsync("Scheduled", MinutesAgo(239));

        var handled = await CreateService().RunOnceAsync();

        Assert.Equal(2, handled);
        Assert.Contains(_email.Sent, m => m.Subject == $"Blocked shipment {blockedShipment} in warehouse {Warehouse}");
        Assert.Contains(_email.Sent, m => m.Subject == $"Blocked transfer {blockedTransfer} in warehouse {Warehouse}");
    }

    [Fact]
    public async Task RunOnce_NeverReportsAFinishedStatus()
    {
        await AddFloormanagerAsync(Warehouse);
        var order = await AddOrderAsync("Shipped", MinutesAgo(10_000));
        await AddOrderAsync("Delivered", MinutesAgo(10_000));
        await AddShipmentAsync(order, "Transit", MinutesAgo(10_000));
        await AddShipmentAsync(order, "Delivered", MinutesAgo(10_000));
        await AddTransferAsync("In Transit", MinutesAgo(10_000));
        await AddTransferAsync("Processed", MinutesAgo(10_000));

        var handled = await CreateService().RunOnceAsync();

        Assert.Equal(0, handled);
        Assert.Empty(_email.Sent);
    }

    // The mail

    [Fact]
    public async Task RunOnce_MailContainsTheTypeIdAndUpdatedAt()
    {
        await AddFloormanagerAsync(Warehouse);
        var updatedAt = MinutesAgo(130);
        var id = await AddOrderAsync("Pending", updatedAt);

        await CreateService().RunOnceAsync();

        var mail = Assert.Single(_email.Sent);
        Assert.Contains("Type: order", mail.Body);
        Assert.Contains($"Id: {id}", mail.Body);
        Assert.Contains($"Updated at: {updatedAt:O}", mail.Body);
    }

    [Fact]
    public async Task RunOnce_SendsNoSecondMailForTheSameBlockade()
    {
        await AddFloormanagerAsync(Warehouse);
        await AddOrderAsync("Pending", MinutesAgo(130));
        var service = CreateService();

        var first = await service.RunOnceAsync();
        var second = await service.RunOnceAsync();

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task RunOnce_MailsAgain_WhenTheItemMovedOnAndGotStuckAgain()
    {
        await AddFloormanagerAsync(Warehouse);
        var id = await AddOrderAsync("Pending", MinutesAgo(130));
        var service = CreateService();
        await service.RunOnceAsync();

        // The order was changed (new updated_at) and has been waiting too long again.
        await fixture.ExecuteAsync(
            "UPDATE `order` SET `updated_at` = @updated WHERE `id` = @id",
            ("@updated", MinutesAgo(125)), ("@id", id));
        await service.RunOnceAsync();

        Assert.Equal(2, _email.Sent.Count);
    }

    // The recipients

    [Fact]
    public async Task RunOnce_MailsEveryFloormanagerOfTheWarehouse_AndNobodyElse()
    {
        var first = await AddFloormanagerAsync(Warehouse);
        var second = await AddFloormanagerAsync(Warehouse);
        await AddFloormanagerAsync(OtherWarehouse);
        await AddOrderAsync("Pending", MinutesAgo(130));

        await CreateService().RunOnceAsync();

        Assert.Equal(2, _email.Sent.Count);
        Assert.Equivalent(new[] { first, second }, _email.Sent.Select(m => m.To).ToArray());
    }

    [Fact]
    public async Task RunOnce_WithoutAFloormanager_SendsNothingAndDoesNotThrow()
    {
        var id = await AddOrderAsync("Pending", MinutesAgo(130), WarehouseWithoutFloormanager);

        var exception = await Record.ExceptionAsync(() => CreateService().RunOnceAsync());

        Assert.Null(exception);
        Assert.Empty(_email.Sent);
        // Recorded, so it is not logged again at every check.
        Assert.Equal(1, await NotificationsForAsync("order", id));
    }

    // Mail problems and limits

    [Fact]
    public async Task RunOnce_TriesAgainAtTheNextCheck_WhenTheMailCouldNotBeSent()
    {
        await AddFloormanagerAsync(Warehouse);
        var id = await AddOrderAsync("Pending", MinutesAgo(130));
        var service = CreateService();
        _email.Succeed = false;

        var failed = await service.RunOnceAsync();

        Assert.Equal(0, failed);
        Assert.Equal(0, await NotificationsForAsync("order", id));

        _email.Succeed = true;
        var retried = await service.RunOnceAsync();

        Assert.Equal(1, retried);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task RunOnce_MailsAtMostMaxMailsPerCheck_AndTheRestFollowsNextTime()
    {
        await AddFloormanagerAsync(Warehouse);
        for (var i = 0; i < 3; i++)
            await AddOrderAsync("Pending", MinutesAgo(130 + i));
        var service = CreateService(maxMailsPerCheck: 2);

        Assert.Equal(2, await service.RunOnceAsync());
        Assert.Equal(2, _email.Sent.Count);

        Assert.Equal(1, await service.RunOnceAsync());
        Assert.Equal(3, _email.Sent.Count);
    }

    // Two API containers

    [Fact]
    public async Task RunOnce_DoesNothing_WhileAnotherInstanceIsChecking()
    {
        await AddFloormanagerAsync(Warehouse);
        await AddOrderAsync("Pending", MinutesAgo(130));

        // The other instance holds the lock of the check on its own connection.
        await using var other = new MySqlConnection(fixture.ConnectionString);
        await other.OpenAsync();
        await using (var command = other.CreateCommand())
        {
            command.CommandText = "SELECT GET_LOCK('cargohub_blockade_detection', 0)";
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }

        var whileLocked = await CreateService().RunOnceAsync();

        Assert.Equal(0, whileLocked);
        Assert.Empty(_email.Sent);

        await using (var command = other.CreateCommand())
        {
            command.CommandText = "SELECT RELEASE_LOCK('cargohub_blockade_detection')";
            await command.ExecuteScalarAsync();
        }

        Assert.Equal(1, await CreateService().RunOnceAsync());
        Assert.Single(_email.Sent);
    }

    // Collects the mails instead of sending them.
    private sealed class FakeEmailService : IEmailService
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public bool Succeed { get; set; } = true;

        public Task<bool> SendAsync(
            string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            if (!Succeed)
                return Task.FromResult(false);

            Sent.Add((to, subject, body));
            return Task.FromResult(true);
        }
    }
}
