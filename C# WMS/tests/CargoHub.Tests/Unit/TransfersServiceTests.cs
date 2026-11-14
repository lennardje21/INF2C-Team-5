using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Unit;

/// <summary>
/// Tests transfer input validation without a database. Valid input is asserted to reach
/// the database connection, while invalid input is rejected before any connection.
/// Database behaviour is covered by TransfersControllerTests.
/// </summary>
public sealed class TransfersServiceTests
{
    private readonly TransfersService _service = CreateService();

    /// <summary>Builds the service without a connection string, so database access throws.</summary>
    private static TransfersService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());
        return new TransfersService(
            factory,
            new MySqlRepository<Transfer>(factory),
            new MySqlRepository<TransferItem>(factory),
            new MySqlRepository<Inventory>(factory));
    }

    private static Transfer ValidTransfer(params TransferItem[] items) => new()
    {
        FromLocationId = 1,
        ToLocationId = 2,
        Items = [.. items]
    };

    private static TransferItem Item(int itemId = 10, int amount = 1) =>
        new() { ItemId = itemId, Amount = amount };

    /// <summary>Asserts the input passed validation and reached the database.</summary>
    private static async Task AssertReachesDatabaseAsync(Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Contains("ConnectionStrings__CargoHub", exception.Message);
    }

    /// <summary>Asserts the input was rejected and returns the validation message.</summary>
    private static async Task<string> AssertInvalidAsync(Func<Task> action) =>
        (await Assert.ThrowsAsync<ArgumentException>(action)).Message;

    // ---- Create and update validation ----

    // A complete transfer with an item passes validation.
    [Fact]
    public async Task CreateAsync_ValidTransfer_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidTransfer(Item())));
    }

    // Items are optional when creating a transfer.
    [Fact]
    public async Task CreateAsync_WithoutItems_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidTransfer()));
    }

    // The server assigns Scheduled when the client sends only whitespace for the status.
    [Fact]
    public async Task CreateAsync_BlankStatus_IsReplacedWithScheduled()
    {
        var transfer = ValidTransfer(Item());
        transfer.TransferStatus = "  ";

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(transfer));

        Assert.Equal("Scheduled", transfer.TransferStatus);
    }

    // The server assigns the id, reference and timestamps during creation.
    [Fact]
    public async Task CreateAsync_IgnoresIdReferenceAndTimestampsFromTheRequest()
    {
        var transfer = ValidTransfer(Item());
        transfer.Id = 42;
        transfer.Reference = "TRF-MANUAL";
        transfer.CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        transfer.UpdatedAt = transfer.CreatedAt;
        var before = DateTime.UtcNow;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(transfer));

        Assert.Equal(0, transfer.Id);
        Assert.Equal(string.Empty, transfer.Reference);
        Assert.True(transfer.CreatedAt >= before);
        Assert.Equal(transfer.CreatedAt, transfer.UpdatedAt);
        Assert.Equal("Scheduled", transfer.TransferStatus);
    }

    // Creation status is controlled by the server and cannot be supplied by clients.
    [Theory]
    [InlineData("Scheduled")]
    [InlineData("In Transit")]
    [InlineData("Processed")]
    public async Task CreateAsync_ClientSuppliedStatus_IsRejected(string status)
    {
        var transfer = ValidTransfer(Item());
        transfer.TransferStatus = status;

        var message = await AssertInvalidAsync(() => _service.CreateAsync(transfer));

        Assert.Equal(
            "TransferStatus is controlled by the server and must not be provided when creating a transfer.",
            message);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("scheduled")]
    [InlineData("Delivered")]
    public async Task UpdateAsync_InvalidStatus_IsRejected(string status)
    {
        var transfer = ValidTransfer(Item());
        transfer.TransferStatus = status;

        var message = await AssertInvalidAsync(() => _service.UpdateAsync(1, transfer));

        Assert.Equal("TransferStatus must be one of: Scheduled, In Transit, Processed.", message);
    }

    [Theory]
    [InlineData("Scheduled")]
    [InlineData("In Transit")]
    [InlineData("Processed")]
    public async Task UpdateAsync_AllowedStatus_PassesValidation(string status)
    {
        var transfer = ValidTransfer(Item());
        transfer.TransferStatus = $"  {status}  ";

        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, transfer));

        Assert.Equal(status, transfer.TransferStatus);
    }

    [Fact]
    public async Task UpdateAsync_StatusLongerThan255Characters_IsRejected()
    {
        var transfer = ValidTransfer(Item());
        transfer.TransferStatus = new string('a', 256);

        var message = await AssertInvalidAsync(() => _service.UpdateAsync(1, transfer));

        Assert.Equal("Fields must not exceed 255 characters: TransferStatus.", message);
    }

    [Fact]
    public async Task UpdateAsync_StatusWithControlCharacter_IsRejected()
    {
        var transfer = ValidTransfer(Item());
        transfer.TransferStatus = "In\nTransit";

        var message = await AssertInvalidAsync(() => _service.UpdateAsync(1, transfer));

        Assert.Equal(
            "Fields must not contain control characters such as line breaks or tabs: TransferStatus.",
            message);
    }

    // Source and destination locations must be positive and different.
    [Theory]
    [InlineData(0, 2, "FromLocationId")]
    [InlineData(-1, 2, "FromLocationId")]
    [InlineData(1, 0, "ToLocationId")]
    [InlineData(1, -1, "ToLocationId")]
    public async Task CreateAsync_InvalidLocationId_IsRejected(
        int fromLocationId, int toLocationId, string field)
    {
        var transfer = ValidTransfer(Item());
        transfer.FromLocationId = fromLocationId;
        transfer.ToLocationId = toLocationId;

        var message = await AssertInvalidAsync(() => _service.CreateAsync(transfer));

        Assert.Equal($"Missing required fields: {field}.", message);
    }

    [Fact]
    public async Task CreateAsync_BothLocationIdsMissing_ListsBothFields()
    {
        var transfer = ValidTransfer(Item());
        transfer.FromLocationId = 0;
        transfer.ToLocationId = -1;

        var message = await AssertInvalidAsync(() => _service.CreateAsync(transfer));

        Assert.Equal("Missing required fields: FromLocationId, ToLocationId.", message);
    }

    [Fact]
    public async Task CreateAsync_SameLocations_IsRejected()
    {
        var transfer = ValidTransfer(Item());
        transfer.ToLocationId = transfer.FromLocationId;

        var message = await AssertInvalidAsync(() => _service.CreateAsync(transfer));

        Assert.Equal("FromLocationId and ToLocationId must be different.", message);
    }

    // Every transfer item must have a positive item id.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateAsync_NonPositiveItemId_IsRejected(int itemId)
    {
        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ValidTransfer(Item(itemId))));

        Assert.Equal("Every item needs an itemId greater than 0.", message);
    }

    // Every transfer item must have a positive amount.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateAsync_NonPositiveAmount_IsRejected(int amount)
    {
        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ValidTransfer(Item(amount: amount))));

        Assert.Equal("Every item needs an amount greater than 0.", message);
    }

    // Null entries are not valid transfer items.
    [Fact]
    public async Task CreateAsync_NullItem_IsRejected()
    {
        var transfer = ValidTransfer();
        transfer.Items.Add(null!);

        var message = await AssertInvalidAsync(() => _service.CreateAsync(transfer));

        Assert.Equal("Items must not contain null.", message);
    }

    // An item may occur only once in a transfer.
    [Fact]
    public async Task CreateAsync_DuplicateItem_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ValidTransfer(Item(), Item(amount: 2))));

        Assert.Equal("An item can only appear once in a transfer.", message);
    }

    // The current service treats an explicit null collection as an empty collection.
    [Fact]
    public async Task CreateAsync_ExplicitNullItems_IsTreatedAsEmpty()
    {
        var transfer = ValidTransfer();
        transfer.Items = null!;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(transfer));

        Assert.Empty(transfer.Items);
    }
}
