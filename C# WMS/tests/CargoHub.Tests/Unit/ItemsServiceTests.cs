using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Unit;


/// Tests item validation without a database. Valid input reaches the database connection,
/// while invalid input is rejected before any connection.

public sealed class ItemsServiceTests
{
    private const int MaxDescriptionBytes = 65_535;
    private const decimal MaxUnitWeight = 999_999_999_999.999999m;

    private readonly ItemsService _service = CreateService();

    private static ItemsService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());
        return new ItemsService(factory,
            new MySqlRepository<Item>(factory),
            new MySqlRepository<Inventory>(factory));
    }

    private static Item ValidItem() => new()
    {
        Code = "ITEM-001",
        Description = "A test item",
        Barcode = "123456789",
        ModelNumber = "MODEL-001",
        CommodityCode = 1234567,
        UnitWeight = 1.25m,
        ItemLineId = 1,
        ItemGroupId = 1,
        ItemTypeId = 1,
        MinPurchaseQuantity = 1,
        CaseSize = 1,
        PackagingType = "Box",
        OrderMultiple = 1,
        SupplierId = 1,
        SupplierSku = "SKU-001"
    };

    private static Item ItemWith(string field, object? value)
    {
        var item = ValidItem();
        typeof(Item).GetProperty(field)!.SetValue(item, value);
        return item;
    }

    private static async Task AssertReachesDatabaseAsync(Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Contains("ConnectionStrings__CargoHub", exception.Message);
    }

    private static async Task<string> AssertInvalidAsync(Func<Task> action) =>
        (await Assert.ThrowsAsync<ArgumentException>(action)).Message;

    [Fact]
    public async Task CreateAsync_ValidItem_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidItem()));
    }

    [Theory]
    [InlineData(nameof(Item.Code))]
    [InlineData(nameof(Item.Description))]
    [InlineData(nameof(Item.Barcode))]
    [InlineData(nameof(Item.ModelNumber))]
    [InlineData(nameof(Item.PackagingType))]
    [InlineData(nameof(Item.SupplierSku))]
    public async Task CreateAsync_MissingTextField_IsRejected(string field)
    {
        foreach (var value in new[] { null, "", "   " })
        {
            var message = await AssertInvalidAsync(() =>
                _service.CreateAsync(ItemWith(field, value)));

            Assert.Equal($"Missing required fields: {field}.", message);
        }
    }

    [Fact]
    public async Task CreateAsync_TrimsTextFieldsAndAllowsMultilineDescription()
    {
        var item = ValidItem();
        item.Code = "  ITEM-001 ";
        item.Description = "\tFirst line\nSecond line\r\n ";
        item.Barcode = " 123456789 ";
        item.ModelNumber = " MODEL-001 ";
        item.PackagingType = " Box ";
        item.SupplierSku = " SKU-001 ";

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(item));

        Assert.Equal("ITEM-001", item.Code);
        Assert.Equal("First line\nSecond line", item.Description);
        Assert.Equal("123456789", item.Barcode);
        Assert.Equal("MODEL-001", item.ModelNumber);
        Assert.Equal("Box", item.PackagingType);
        Assert.Equal("SKU-001", item.SupplierSku);
    }

    [Theory]
    [InlineData(nameof(Item.Code))]
    [InlineData(nameof(Item.Barcode))]
    [InlineData(nameof(Item.ModelNumber))]
    [InlineData(nameof(Item.PackagingType))]
    [InlineData(nameof(Item.SupplierSku))]
    public async Task CreateAsync_VarcharAt255Characters_PassesValidation(string field)
    {
        await AssertReachesDatabaseAsync(() =>
            _service.CreateAsync(ItemWith(field, new string('a', 255))));
    }

    [Fact]
    public async Task CreateAsync_VarcharAt255UnicodeCharacters_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() =>
            _service.CreateAsync(ItemWith(nameof(Item.Code), string.Concat(Enumerable.Repeat("😀", 255)))));
    }

    [Theory]
    [InlineData(nameof(Item.Code))]
    [InlineData(nameof(Item.Barcode))]
    [InlineData(nameof(Item.ModelNumber))]
    [InlineData(nameof(Item.PackagingType))]
    [InlineData(nameof(Item.SupplierSku))]
    public async Task CreateAsync_VarcharOver255Characters_IsRejected(string field)
    {
        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ItemWith(field, new string('a', 256))));

        Assert.Equal($"Fields must not exceed 255 characters: {field}.", message);
    }

    [Fact]
    public async Task CreateAsync_VarcharOver255UnicodeCharacters_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ItemWith(nameof(Item.Code), string.Concat(Enumerable.Repeat("😀", 256)))));

        Assert.Equal($"Fields must not exceed 255 characters: Code.", message);
    }

    [Fact]
    public async Task CreateAsync_DescriptionAtTextByteLimit_PassesValidation()
    {
        var description = new string('é', 32_767) + "a";

        Assert.Equal(MaxDescriptionBytes, System.Text.Encoding.UTF8.GetByteCount(description));
        await AssertReachesDatabaseAsync(() =>
            _service.CreateAsync(ItemWith(nameof(Item.Description), description)));
    }

    [Fact]
    public async Task CreateAsync_DescriptionOverTextByteLimit_IsRejected()
    {
        var description = new string('é', 32_768);

        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ItemWith(nameof(Item.Description), description)));

        Assert.Equal($"Description must not exceed {MaxDescriptionBytes} UTF-8 bytes.", message);
    }

    [Fact]
    public async Task CreateAsync_DescriptionWithUnsupportedControlCharacter_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ItemWith(nameof(Item.Description), "Description\0")));

        Assert.Equal(
            "Description must not contain control characters other than tabs or line breaks.",
            message);
    }

    [Theory]
    [InlineData(nameof(Item.ItemLineId))]
    [InlineData(nameof(Item.ItemGroupId))]
    [InlineData(nameof(Item.ItemTypeId))]
    [InlineData(nameof(Item.SupplierId))]
    public async Task CreateAsync_NonPositiveReferenceId_IsRejected(string field)
    {
        foreach (var id in new[] { 0, -1 })
        {
            var message = await AssertInvalidAsync(() =>
                _service.CreateAsync(ItemWith(field, id)));

            Assert.Equal($"{field} must be greater than 0.", message);
        }
    }

    [Theory]
    [InlineData(nameof(Item.CommodityCode))]
    [InlineData(nameof(Item.MinPurchaseQuantity))]
    [InlineData(nameof(Item.CaseSize))]
    [InlineData(nameof(Item.OrderMultiple))]
    public async Task CreateAsync_NegativeIntegerField_IsRejected(string field)
    {
        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ItemWith(field, -1)));

        Assert.Equal($"{field} cannot be negative.", message);
    }

    [Fact]
    public async Task CreateAsync_UnitWeightAtMaximum_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() =>
            _service.CreateAsync(ItemWith(nameof(Item.UnitWeight), MaxUnitWeight)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000000000000)]
    public async Task CreateAsync_UnitWeightOutsideDecimalRange_IsRejected(decimal weight)
    {
        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ItemWith(nameof(Item.UnitWeight), weight)));

        Assert.Equal($"UnitWeight must be between 0 and {MaxUnitWeight}.", message);
    }

    [Fact]
    public async Task CreateAsync_ZeroUnitWeight_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() =>
            _service.CreateAsync(ItemWith(nameof(Item.UnitWeight), 0m)));
    }

    [Fact]
    public async Task CreateAsync_UnitWeightWithMoreThanSixDecimalPlaces_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ItemWith(nameof(Item.UnitWeight), 1.1234567m)));

        Assert.Equal("UnitWeight must not have more than 6 decimal places.", message);
    }

    [Fact]
    public async Task UpdateAsync_InvalidItem_IsRejectedBeforeDatabaseAccess()
    {
        var message = await AssertInvalidAsync(() =>
            _service.UpdateAsync(1, ItemWith(nameof(Item.SupplierId), 0)));

        Assert.Equal("SupplierId must be greater than 0.", message);
    }
}
