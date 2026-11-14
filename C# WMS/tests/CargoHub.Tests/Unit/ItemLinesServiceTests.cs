using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace CargoHub.Tests.Unit;

/// <summary>
/// Tests item line validation without a database. Valid input reaches the database
/// connection, while invalid input is rejected before any connection.
/// </summary>
public sealed class ItemLinesServiceTests
{
    private const int MaxDescriptionBytes = 65_535;
    private static readonly string[] RequiredFields =
    [
        nameof(ItemLine.Name),
        nameof(ItemLine.Description)
    ];

    private readonly ItemLinesService _service = CreateService();

    private static ItemLinesService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());
        return new ItemLinesService(factory, new MySqlRepository<ItemLine>(factory));
    }

    private static ItemLine ValidItemLine() => new()
    {
        Name = "Produce",
        Description = "Fresh produce"
    };

    private static ItemLine ItemLineWith(string field, string? value)
    {
        var itemLine = ValidItemLine();
        typeof(ItemLine).GetProperty(field)!.SetValue(itemLine, value);
        return itemLine;
    }

    private static async Task AssertReachesDatabaseAsync(Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Contains("ConnectionStrings__CargoHub", exception.Message);
    }

    private static async Task<string> AssertInvalidAsync(Func<Task> action) =>
        (await Assert.ThrowsAsync<ArgumentException>(action)).Message;

    public static TheoryData<string> Fields => [.. RequiredFields];

    [Fact]
    public async Task CreateAsync_ValidItemLine_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidItemLine()));
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public async Task CreateAsync_MissingRequiredField_IsRejected(string field)
    {
        foreach (var value in new[] { null, string.Empty, "   " })
        {
            var message = await AssertInvalidAsync(() =>
                _service.CreateAsync(ItemLineWith(field, value)));

            Assert.Equal($"Missing required fields: {field}.", message);
        }
    }

    [Fact]
    public async Task CreateAsync_SeveralMissingFields_ListsAllOfThem()
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(new ItemLine()));

        Assert.Equal($"Missing required fields: {string.Join(", ", RequiredFields)}.", message);
    }

    [Fact]
    public async Task CreateAsync_TrimsFieldsAndAllowsMultilineDescription()
    {
        var itemLine = ValidItemLine();
        itemLine.Name = "  Produce ";
        itemLine.Description = "\tFresh produce\nFrom local farms\r\n";

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(itemLine));

        Assert.Equal("Produce", itemLine.Name);
        Assert.Equal("Fresh produce\nFrom local farms", itemLine.Description);
    }

    [Theory]
    [InlineData(nameof(ItemLine.Name))]
    public async Task CreateAsync_NameAt255UnicodeCharacters_PassesValidation(string field)
    {
        var itemLine = ItemLineWith(field, string.Concat(Enumerable.Repeat("😀", 255)));

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(itemLine));
    }

    [Fact]
    public async Task CreateAsync_NameOver255UnicodeCharacters_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ItemLineWith(nameof(ItemLine.Name), string.Concat(Enumerable.Repeat("😀", 256)))));

        Assert.Equal("Name must not exceed 255 characters.", message);
    }

    [Theory]
    [InlineData(nameof(ItemLine.Name), "Fresh\nproduce")]
    [InlineData(nameof(ItemLine.Name), "Fresh\tproduce")]
    public async Task CreateAsync_NameWithControlCharacter_IsRejected(string field, string value)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(ItemLineWith(field, value)));

        Assert.Equal(
            $"Fields must not contain control characters such as line breaks or tabs: {field}.",
            message);
    }

    [Fact]
    public async Task CreateAsync_DescriptionWithUnsupportedControlCharacter_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ItemLineWith(nameof(ItemLine.Description), "Fresh\0produce")));

        Assert.Equal(
            "Description must not contain control characters other than tabs or line breaks.",
            message);
    }

    [Fact]
    public async Task CreateAsync_DescriptionAtUtf8ByteLimit_PassesValidation()
    {
        var description = new string('é', 32_767) + "a";

        Assert.Equal(MaxDescriptionBytes, Encoding.UTF8.GetByteCount(description));
        await AssertReachesDatabaseAsync(() =>
            _service.CreateAsync(ItemLineWith(nameof(ItemLine.Description), description)));
    }

    [Fact]
    public async Task CreateAsync_DescriptionOverUtf8ByteLimit_IsRejected()
    {
        var description = new string('é', 32_768);

        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ItemLineWith(nameof(ItemLine.Description), description)));

        Assert.Equal($"Description must not exceed {MaxDescriptionBytes} UTF-8 bytes.", message);
    }

    [Fact]
    public async Task CreateAsync_IgnoresIdAndTimestampsFromTheRequest()
    {
        var itemLine = ValidItemLine();
        itemLine.Id = 42;
        itemLine.CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        itemLine.UpdatedAt = itemLine.CreatedAt;
        var before = DateTime.UtcNow;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(itemLine));

        Assert.Equal(0, itemLine.Id);
        Assert.True(itemLine.CreatedAt >= before);
        Assert.Equal(DateTimeKind.Utc, itemLine.CreatedAt.Kind);
        Assert.Equal(itemLine.CreatedAt, itemLine.UpdatedAt);
    }

    [Fact]
    public async Task UpdateAsync_ValidItemLine_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, ValidItemLine()));
    }

    [Fact]
    public async Task UpdateAsync_InvalidItemLine_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.UpdateAsync(1, ItemLineWith(nameof(ItemLine.Name), "Invalid\nname")));

        Assert.Equal(
            "Fields must not contain control characters such as line breaks or tabs: Name.",
            message);
    }
}
