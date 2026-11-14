using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using CargoHub.Api.Validation;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Unit;

/// <summary>
/// Tests warehouse validation without a database. Valid input is asserted to reach
/// the database connection, while invalid input is rejected before any connection.
/// Database behaviour is covered by WarehousesControllerTests.
/// </summary>
public sealed class WarehousesServiceTests
{
    private readonly WarehousesService _service = CreateService();

    // Builds the service without a connection string, so database access throws.
    private static WarehousesService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());
        return new WarehousesService(
            factory,
            new MySqlRepository<Warehouse>(factory),
            new MySqlRepository<Location>(factory));
    }

    // A warehouse that passes every validation check.
    private static Warehouse ValidWarehouse() => new()
    {
        Code = "WH-001",
        Name = "Main Warehouse",
        Address = "Warehouse Street 1",
        City = "Rotterdam",
        ZipCode = "3000AA",
        Province = "Zuid-Holland",
        Country = "Netherlands",
        ContactName = "Warehouse Contact",
        ContactPhone = "0101234567",
        ContactEmail = "contact@example.com"
    };

    private static Warehouse WarehouseWith(
        string field, string? value, string country = "Netherlands")
    {
        var warehouse = ValidWarehouse();
        warehouse.Country = country;
        typeof(Warehouse).GetProperty(field)!.SetValue(warehouse, value);
        return warehouse;
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
    public async Task CreateAsync_ValidWarehouse_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidWarehouse()));
    }

    // Asserts that when a required field is missing, the error message lists it.
    [Theory]
    [InlineData(nameof(Warehouse.Code))]
    [InlineData(nameof(Warehouse.Name))]
    [InlineData(nameof(Warehouse.Address))]
    [InlineData(nameof(Warehouse.City))]
    [InlineData(nameof(Warehouse.ZipCode))]
    [InlineData(nameof(Warehouse.Province))]
    [InlineData(nameof(Warehouse.Country))]
    [InlineData(nameof(Warehouse.ContactName))]
    [InlineData(nameof(Warehouse.ContactPhone))]
    [InlineData(nameof(Warehouse.ContactEmail))]
    public async Task CreateAsync_MissingRequiredField_IsRejected(string field)
    {
        foreach (var value in new[] { null, string.Empty, "   " })
        {
            var warehouse = ValidWarehouse();
            typeof(Warehouse).GetProperty(field)!.SetValue(warehouse, value);

            var message = await AssertInvalidAsync(() => _service.CreateAsync(warehouse));

            Assert.Equal($"Missing required fields: {field}.", message);
        }
    }

    // Asserts that when multiple required fields are missing, all of them are listed in the error message.
    [Fact]
    public async Task CreateAsync_SeveralMissingFields_ListsAllOfThem()
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(new Warehouse()));

        Assert.Equal(
            "Missing required fields: Code, Name, Address, City, ZipCode, Province, " +
            "Country, ContactName, ContactPhone, ContactEmail.",
            message);
    }

    [Fact]
    public async Task CreateAsync_TrimsFields()
    {
        var warehouse = ValidWarehouse();
        warehouse.Code = " WH-001 ";
        warehouse.Name = "\tMain Warehouse\n";
        warehouse.ZipCode = " 3000AA ";
        warehouse.ContactEmail = " contact@example.com ";

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(warehouse));

        Assert.Equal(
            ("WH-001", "Main Warehouse", "3000AA", "contact@example.com"),
            (warehouse.Code, warehouse.Name, warehouse.ZipCode, warehouse.ContactEmail));
    }

    [Fact]
    public async Task CreateAsync_TextOf255Characters_PassesValidation()
    {
        var warehouse = ValidWarehouse();
        warehouse.Name = new string('a', 255);

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(warehouse));
    }

    [Fact]
    public async Task CreateAsync_TextLongerThan255Characters_IsRejected()
    {
        var warehouse = ValidWarehouse();
        warehouse.Name = new string('a', 256);

        var message = await AssertInvalidAsync(() => _service.CreateAsync(warehouse));

        Assert.Equal("Fields must not exceed 255 characters: Name.", message);
    }

    [Theory]
    [InlineData(nameof(Warehouse.Name), "Main\nWarehouse")]
    [InlineData(nameof(Warehouse.Address), "Warehouse\tStreet 1")]
    public async Task CreateAsync_ControlCharacterInField_IsRejected(string field, string value)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(WarehouseWith(field, value)));

        Assert.Equal(
            $"Fields must not contain control characters such as line breaks or tabs: {field}.",
            message);
    }

    [Theory]
    [InlineData(nameof(Warehouse.Code), "VGH-AMB")]
    [InlineData(nameof(Warehouse.Name), "7-Eleven")]
    [InlineData(nameof(Warehouse.Address), "1e Middellandstraat 5")]
    [InlineData(nameof(Warehouse.City), "'s-Hertogenbosch")]
    [InlineData(nameof(Warehouse.Province), "São Paulo")]
    [InlineData(nameof(Warehouse.Country), "Nederland")]
    [InlineData(nameof(Warehouse.ContactName), "J. de Vries")]
    [InlineData(nameof(Warehouse.ZipCode), "3000AA")]
    [InlineData(nameof(Warehouse.ZipCode), "SW1A 1AA", "United Kingdom")]
    [InlineData(nameof(Warehouse.ContactPhone), "+31 (0)10 123 4567")]
    [InlineData(nameof(Warehouse.ContactEmail), "a+tag@sub.example.co.uk")]
    public async Task CreateAsync_ValidFormat_PassesValidation(
        string field, string value, string country = "Netherlands")
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(WarehouseWith(field, value, country)));
    }

    public static TheoryData<string, string, string, string> InvalidFormats => new()
    {
        {
            nameof(Warehouse.Code), "WH__001", "Netherlands",
            "Code may only contain letters and digits, optionally separated by single hyphens, such as 'VGH-AMB'."
        },
        {
            nameof(Warehouse.Name), "!!!", "Netherlands",
            "Name must contain at least one letter or digit."
        },
        {
            nameof(Warehouse.Address), "12345", "Netherlands",
            "Address must contain a street name."
        },
        { nameof(Warehouse.City), "Rotterdam1", "Netherlands", $"City {FieldRules.NameError}" },
        { nameof(Warehouse.Province), "Zuid_Holland", "Netherlands", $"Province {FieldRules.NameError}" },
        { nameof(Warehouse.Country), "N3therlands", "Netherlands", $"Country {FieldRules.NameError}" },
        { nameof(Warehouse.ContactName), "<script>", "Netherlands", $"ContactName {FieldRules.NameError}" },
        {
            nameof(Warehouse.ZipCode), "0123 AB", "Netherlands",
            $"ZipCode {FieldRules.DutchZipCodeError}"
        },
        {
            nameof(Warehouse.ZipCode), "123--45", "United States",
            $"ZipCode {FieldRules.ZipCodeError}"
        },
        {
            nameof(Warehouse.ContactPhone), "12345678", "Netherlands",
            $"ContactPhone {FieldRules.PhoneError}"
        },
        {
            nameof(Warehouse.ContactEmail), "test@example", "Netherlands",
            $"ContactEmail {FieldRules.EmailError}"
        }
    };

    [Theory]
    [MemberData(nameof(InvalidFormats))]
    public async Task CreateAsync_InvalidFormat_IsRejected(
        string field, string value, string country, string expected)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(WarehouseWith(field, value, country)));

        Assert.Equal(expected, message);
    }

    [Fact]
    public async Task CreateAsync_SeveralFormatErrors_ListsAllOfThem()
    {
        var warehouse = ValidWarehouse();
        warehouse.City = "Rotterdam1";
        warehouse.ContactPhone = "123";
        warehouse.ContactEmail = "not-an-email";

        var message = await AssertInvalidAsync(() => _service.CreateAsync(warehouse));

        Assert.Equal(
            $"City {FieldRules.NameError} ContactPhone {FieldRules.PhoneError} " +
            $"ContactEmail {FieldRules.EmailError}",
            message);
    }

    [Fact]
    public async Task CreateAsync_IgnoresIdAndTimestampsFromTheRequest()
    {
        var warehouse = ValidWarehouse();
        warehouse.Id = 42;
        warehouse.CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        warehouse.UpdatedAt = warehouse.CreatedAt;
        var before = DateTime.UtcNow;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(warehouse));

        Assert.Equal(0, warehouse.Id);
        Assert.True(warehouse.CreatedAt >= before);
        Assert.Equal(warehouse.CreatedAt, warehouse.UpdatedAt);
    }

    [Fact]
    public async Task UpdateAsync_ValidWarehouse_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, ValidWarehouse()));
    }

    [Fact]
    public async Task UpdateAsync_InvalidWarehouse_IsRejected()
    {
        var warehouse = ValidWarehouse();
        warehouse.ContactEmail = "not-an-email";

        var message = await AssertInvalidAsync(() => _service.UpdateAsync(1, warehouse));

        Assert.Equal($"ContactEmail {FieldRules.EmailError}", message);
    }
}