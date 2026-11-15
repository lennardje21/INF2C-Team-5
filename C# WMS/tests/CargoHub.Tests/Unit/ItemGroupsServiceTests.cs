using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Unit;

/// Tests item group validation without a database. Valid input reaches the database
/// connection, while invalid input is rejected before any connection.

public sealed class ItemGroupsServiceTests
{
    private readonly ItemGroupsService _service = CreateService();

    private static ItemGroupsService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());
        return new ItemGroupsService(
            factory, new MySqlRepository<ItemGroup>(factory), new MySqlRepository<Item>(factory));
    }

    private static ItemGroup ValidItemGroup() => new()
    {
        Name = "Produce",
        Description = "Fresh produce"
    };

    private static ItemGroup ItemGroupWith(string field, string? value)
    {
        var itemGroup = ValidItemGroup();
        typeof(ItemGroup).GetProperty(field)!.SetValue(itemGroup, value);
        return itemGroup;
    }

    private static async Task AssertReachesDatabaseAsync(Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Contains("ConnectionStrings__CargoHub", exception.Message);
    }

    private static async Task<string> AssertInvalidAsync(Func<Task> action) =>
        (await Assert.ThrowsAsync<ArgumentException>(action)).Message;

    [Fact]
    public async Task CreateAsync_ValidItemGroup_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidItemGroup()));
    }

    [Theory]
    [InlineData(nameof(ItemGroup.Name))]
    [InlineData(nameof(ItemGroup.Description))]
    public async Task CreateAsync_MissingRequiredField_IsRejected(string field)
    {
        foreach (var value in new[] { null, string.Empty, "   " })
        {
            var message = await AssertInvalidAsync(() =>
                _service.CreateAsync(ItemGroupWith(field, value)));

            Assert.Equal($"Missing required fields: {field}.", message);
        }
    }

    [Fact]
    public async Task CreateAsync_TrimsFieldsAndAllowsMultilineDescription()
    {
        var itemGroup = ValidItemGroup();
        itemGroup.Name = "  Produce ";
        itemGroup.Description = "  Fresh produce\nFrom local farms  ";

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(itemGroup));

        Assert.Equal("Produce", itemGroup.Name);
        Assert.Equal("Fresh produce\nFrom local farms", itemGroup.Description);
    }

    [Fact]
    public async Task CreateAsync_NameLengthLimit_IsEnforced()
    {
        await AssertReachesDatabaseAsync(() =>
            _service.CreateAsync(ItemGroupWith(nameof(ItemGroup.Name), new string('a', 255))));

        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ItemGroupWith(nameof(ItemGroup.Name), new string('a', 256))));

        Assert.Equal("Name must not exceed 255 characters.", message);
    }

    [Fact]
    public async Task CreateAsync_DescriptionByteLimit_IsEnforced()
    {
        await AssertReachesDatabaseAsync(() =>
            _service.CreateAsync(ItemGroupWith(nameof(ItemGroup.Description), new string('é', 32_767) + "a")));

        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ItemGroupWith(nameof(ItemGroup.Description), new string('é', 32_768))));

        Assert.Equal("Description must not exceed 65535 UTF-8 bytes.", message);
    }

    [Theory]
    [InlineData(nameof(ItemGroup.Name), "Fresh\nproduce",
        "Fields must not contain control characters such as line breaks or tabs: Name.")]
    [InlineData(nameof(ItemGroup.Description), "Fresh\0produce",
        "Description must not contain control characters other than tabs or line breaks.")]
    public async Task CreateAsync_ControlCharacter_IsRejected(string field, string value, string expected)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(ItemGroupWith(field, value)));

        Assert.Equal(expected, message);
    }

    [Fact]
    public async Task UpdateAsync_ValidItemGroup_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, ValidItemGroup()));
    }

    [Fact]
    public async Task UpdateAsync_InvalidItemGroup_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.UpdateAsync(1, ItemGroupWith(nameof(ItemGroup.Name), string.Empty)));

        Assert.Equal("Missing required fields: Name.", message);
    }
}
