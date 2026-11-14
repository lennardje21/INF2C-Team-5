using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Unit;

/// <summary>
/// Tests item type validation without a database. Valid input reaches the database
/// connection, while invalid input is rejected before any connection.
/// </summary>
public sealed class ItemTypesServiceTests
{
    private readonly ItemTypesService _service = CreateService();

    private static ItemTypesService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());
        return new ItemTypesService(factory, new MySqlRepository<ItemType>(factory));
    }

    private static ItemType ValidItemType() => new()
    {
        Name = "Produce",
        Description = "Fresh produce"
    };

    private static ItemType ItemTypeWith(string field, string? value)
    {
        var itemType = ValidItemType();
        typeof(ItemType).GetProperty(field)!.SetValue(itemType, value);
        return itemType;
    }

    private static async Task AssertReachesDatabaseAsync(Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Contains("ConnectionStrings__CargoHub", exception.Message);
    }

    private static async Task<string> AssertInvalidAsync(Func<Task> action) =>
        (await Assert.ThrowsAsync<ArgumentException>(action)).Message;

    [Fact]
    public async Task CreateAsync_ValidItemType_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidItemType()));
    }

    [Theory]
    [InlineData(nameof(ItemType.Name))]
    [InlineData(nameof(ItemType.Description))]
    public async Task CreateAsync_MissingRequiredField_IsRejected(string field)
    {
        foreach (var value in new[] { null, string.Empty, "   " })
        {
            var message = await AssertInvalidAsync(() =>
                _service.CreateAsync(ItemTypeWith(field, value)));

            Assert.Equal($"Missing required fields: {field}.", message);
        }
    }

    [Fact]
    public async Task CreateAsync_TrimsFieldsAndAllowsMultilineDescription()
    {
        var itemType = ValidItemType();
        itemType.Name = "  Produce ";
        itemType.Description = "  Fresh produce\nFrom local farms  ";

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(itemType));

        Assert.Equal("Produce", itemType.Name);
        Assert.Equal("Fresh produce\nFrom local farms", itemType.Description);
    }

    [Fact]
    public async Task CreateAsync_NameLengthLimit_IsEnforced()
    {
        await AssertReachesDatabaseAsync(() =>
            _service.CreateAsync(ItemTypeWith(nameof(ItemType.Name), new string('a', 255))));

        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ItemTypeWith(nameof(ItemType.Name), new string('a', 256))));

        Assert.Equal("Name must not exceed 255 characters.", message);
    }

    [Fact]
    public async Task CreateAsync_DescriptionByteLimit_IsEnforced()
    {
        await AssertReachesDatabaseAsync(() =>
            _service.CreateAsync(ItemTypeWith(nameof(ItemType.Description), new string('é', 32_767) + "a")));

        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ItemTypeWith(nameof(ItemType.Description), new string('é', 32_768))));

        Assert.Equal("Description must not exceed 65535 UTF-8 bytes.", message);
    }

    [Theory]
    [InlineData(nameof(ItemType.Name), "Fresh\nproduce",
        "Fields must not contain control characters such as line breaks or tabs: Name.")]
    [InlineData(nameof(ItemType.Description), "Fresh\0produce",
        "Description must not contain control characters other than tabs or line breaks.")]
    public async Task CreateAsync_ControlCharacter_IsRejected(string field, string value, string expected)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(ItemTypeWith(field, value)));

        Assert.Equal(expected, message);
    }

    [Fact]
    public async Task UpdateAsync_ValidItemType_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, ValidItemType()));
    }

    [Fact]
    public async Task UpdateAsync_InvalidItemType_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.UpdateAsync(1, ItemTypeWith(nameof(ItemType.Name), string.Empty)));

        Assert.Equal("Missing required fields: Name.", message);
    }
}
