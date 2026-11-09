using System.Net;
using System.Net.Http.Json;
using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using CargoHub.Tests.Fixtures;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class TransfersControllerTests(ApiFixture fixture)
{
    private const string Url = "/api/v2/transfers";
    private readonly HttpClient _client = fixture.Client;

    private static object Item(int itemId, int amount) => new { itemId, amount };

    private static Dictionary<string, object> NewTransfer(
        int fromLocationId = 1, int toLocationId = 2, params object[] items) => new()
    {
        ["fromLocationId"] = fromLocationId,
        ["toLocationId"] = toLocationId,
        ["items"] = items
    };

    private async Task<Transfer> CreateAsync(object transfer)
    {
        var response = await _client.PostAsJsonAsync(Url, transfer);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Transfer>())!;
    }

    [Fact]
    public async Task GetTransfers_ReturnsSeededTransfersWithItems()
    {
        var transfers = await _client.GetFromJsonAsync<List<Transfer>>(Url);

        Assert.NotNull(transfers);
        Assert.True(transfers.Count >= 800);
        Assert.Contains(transfers, transfer => transfer.Items.Count > 0);
    }

    [Fact]
    public async Task GetTransfer_ReturnsTransferWithItems()
    {
        var transfer = await _client.GetFromJsonAsync<Transfer>($"{Url}/1");

        Assert.NotNull(transfer);
        Assert.Equal(1, transfer.Id);
        Assert.Equal("TRF-000001", transfer.Reference);
        Assert.Equal(369, transfer.FromLocationId);
        Assert.Equal(320, transfer.ToLocationId);
        Assert.Equal("Scheduled", transfer.TransferStatus);
        Assert.NotEmpty(transfer.Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/items")]
    public async Task GetTransfer_UnknownTransfer_ReturnsNotFound(string suffix)
    {
        var response = await _client.GetAsync($"{Url}/2147483647{suffix}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTransferItems_ReturnsItemsForTransfer()
    {
        var items = await _client.GetFromJsonAsync<List<TransferItem>>($"{Url}/1/items");

        Assert.NotNull(items);
        Assert.Equal(
            await fixture.QueryIntAsync(
                "SELECT COUNT(*) FROM `transfer_item` WHERE `transfer_id` = 1"),
            items.Count);
        Assert.All(items, item => Assert.Equal(1, item.TransferId));
    }

    [Fact]
    public async Task CreateTransfer_StoresTransferAndItems()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        var created = await CreateAsync(NewTransfer(items: Item(10, 2)));

        Assert.True(created.Id > 800);
        Assert.Equal($"TRF-{created.Id:D6}", created.Reference);
        Assert.Equal("Scheduled", created.TransferStatus);
        Assert.True(created.CreatedAt >= before);
        Assert.InRange(
            created.UpdatedAt,
            created.CreatedAt.AddTicks(-10),
            created.CreatedAt.AddTicks(10));
        Assert.Equal((10, 2), (Assert.Single(created.Items).ItemId, created.Items[0].Amount));
    }

    [Theory]
    [MemberData(nameof(InvalidTransfers))]
    public async Task CreateTransfer_InvalidInput_ReturnsBadRequest(object transfer)
    {
        var count = await fixture.QueryIntAsync("SELECT COUNT(*) FROM `transfer`");

        var response = await _client.PostAsJsonAsync(Url, transfer);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(count, await fixture.QueryIntAsync("SELECT COUNT(*) FROM `transfer`"));
    }

    public static TheoryData<object> InvalidTransfers => new()
    {
        NewTransfer(fromLocationId: 0),
        NewTransfer(toLocationId: 0),
        NewTransfer(fromLocationId: 1, toLocationId: 1),
        NewTransfer(items: Item(10, 0)),
        NewTransfer(items: [Item(10, 1), Item(10, 2)]),
        NewTransfer(items: Item(999999, 1))
    };

    [Fact]
    public async Task UpdateTransfer_ChangesFieldsButKeepsReferenceAndCreatedAt()
    {
        var created = await CreateAsync(NewTransfer(items: Item(10, 2)));
        await Task.Delay(10);

        var changes = NewTransfer(fromLocationId: 3, toLocationId: 4, items: Item(11, 3));
        changes["reference"] = "TRF-CHANGED";
        changes["transferStatus"] = "In Transit";

        var response = await _client.PutAsJsonAsync($"{Url}/{created.Id}", changes);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await _client.GetFromJsonAsync<Transfer>($"{Url}/{created.Id}");
        Assert.Equal("TRF-" + created.Id.ToString("D6"), updated!.Reference);
        Assert.Equal("In Transit", updated.TransferStatus);
        Assert.Equal((3, 4), (updated.FromLocationId, updated.ToLocationId));
        Assert.InRange(
            updated.CreatedAt,
            created.CreatedAt.AddTicks(-10),
            created.CreatedAt.AddTicks(10));
        Assert.True(updated.UpdatedAt > created.UpdatedAt);
        Assert.Equal((11, 3), (Assert.Single(updated.Items).ItemId, updated.Items[0].Amount));
    }

    [Fact]
    public async Task UpdateTransfer_UnknownTransfer_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync($"{Url}/2147483647", NewTransfer());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CommitTransfer_MovesInventoryAndMarksTransferProcessed()
    {
        var sourceLocationId = await fixture.QueryIntAsync(
            "SELECT `location_id` FROM `inventory` " +
            "WHERE `item_id` = 10 AND `quantity_on_hand` > 0 " +
            "ORDER BY `quantity_on_hand` DESC LIMIT 1");
        var destinationLocationId = await fixture.QueryIntAsync(
            "SELECT `id` FROM `location` WHERE `id` <> @sourceId LIMIT 1",
            ("@sourceId", sourceLocationId));
        var created = await CreateAsync(NewTransfer(
            sourceLocationId, destinationLocationId, Item(10, 1)));
        var sourceBefore = await InventoryQuantityAsync(10, sourceLocationId);
        var destinationBefore = await InventoryQuantityOrZeroAsync(10, destinationLocationId);

        var response = await _client.PutAsync($"{Url}/{created.Id}/commit", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var processed = await _client.GetFromJsonAsync<Transfer>($"{Url}/{created.Id}");
        Assert.Equal("Processed", processed!.TransferStatus);
        Assert.Equal(sourceBefore - 1, await InventoryQuantityAsync(10, sourceLocationId));
        Assert.Equal(destinationBefore + 1, await InventoryQuantityAsync(10, destinationLocationId));
    }

    [Fact]
    public async Task CommitTransfer_AlreadyProcessed_ReturnsBadRequest()
    {
        var response = await _client.PutAsync($"{Url}/2/commit", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CommitTransfer_UnknownTransfer_ReturnsNotFound()
    {
        var response = await _client.PutAsync($"{Url}/2147483647/commit", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTransfer_RemovesTransferAndItems()
    {
        var created = await CreateAsync(NewTransfer(items: Item(12, 1)));

        var response = await _client.DeleteAsync($"{Url}/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{Url}/{created.Id}")).StatusCode);
        Assert.Equal(
            0,
            await fixture.QueryIntAsync(
                "SELECT COUNT(*) FROM `transfer_item` WHERE `transfer_id` = @id",
                ("@id", created.Id)));
    }

    [Fact]
    public async Task DeleteTransfer_UnknownTransfer_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"{Url}/2147483647");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_InvalidTransfer_ThrowsBeforeDatabaseAccess()
    {
        var transfer = new Transfer
        {
            FromLocationId = 1,
            ToLocationId = 1,
            Items = [new TransferItem { ItemId = 10, Amount = 1 }]
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().CreateAsync(transfer));

        Assert.Contains("different", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_InvalidItemAmount_ThrowsBeforeDatabaseAccess()
    {
        var transfer = new Transfer
        {
            FromLocationId = 1,
            ToLocationId = 2,
            Items = [new TransferItem { ItemId = 10, Amount = 0 }]
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => CreateService().CreateAsync(transfer));

        Assert.Contains("greater than 0", exception.Message);
    }

    private async Task<int> InventoryQuantityAsync(int itemId, int locationId)
    {
        return await fixture.QueryIntAsync(
            "SELECT `quantity_on_hand` FROM `inventory` " +
            "WHERE `item_id` = @itemId AND `location_id` = @locationId",
            ("@itemId", itemId), ("@locationId", locationId));
    }

    private async Task<int> InventoryQuantityOrZeroAsync(int itemId, int locationId)
    {
        return await fixture.QueryIntAsync(
            "SELECT COALESCE((" +
            "SELECT `quantity_on_hand` FROM `inventory` " +
            "WHERE `item_id` = @itemId AND `location_id` = @locationId), 0)",
            ("@itemId", itemId), ("@locationId", locationId));
    }

    private static TransfersService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());
        return new TransfersService(
            factory,
            new MySqlRepository<Transfer>(factory),
            new MySqlRepository<TransferItem>(factory),
            new MySqlRepository<Inventory>(factory));
    }
}
