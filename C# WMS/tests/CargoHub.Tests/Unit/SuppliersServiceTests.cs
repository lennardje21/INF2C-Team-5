using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using CargoHub.Api.Validation;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Unit;

// Tests supplier validation without a database. Valid input is asserted to reach
// the database connection, while invalid input is rejected before any connection.
// Database behaviour is covered by SuppliersControllerTests.
public sealed class SuppliersServiceTests
{
    // The required fields, in the order the error message lists them.
    private static readonly string[] RequiredFields =
    [
        nameof(Supplier.Name), nameof(Supplier.Address), nameof(Supplier.City), nameof(Supplier.ZipCode),
        nameof(Supplier.Province), nameof(Supplier.Country), nameof(Supplier.ContactName),
        nameof(Supplier.PhoneNumber), nameof(Supplier.Code), nameof(Supplier.Reference)
    ];

    private readonly SuppliersService _service = CreateService();

    // Builds the service without a connection string, so database access throws.
    private static SuppliersService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());
        return new SuppliersService(factory, new MySqlRepository<Supplier>(factory));
    }

    // A Dutch supplier that passes every check.
    private static Supplier ValidSupplier() => new()
    {
        Code = "SUP-0001",
        Name = "Koffie Groothandel BV",
        Address = "Waalhaven 5",
        City = "Rotterdam",
        ZipCode = "3089 JH",
        Province = "Zuid-Holland",
        Country = "Netherlands",
        ContactName = "Fatima el Amrani",
        PhoneNumber = "010-7654321",
        Reference = "REF-2026-01"
    };

    // A valid supplier with one field changed by name, and optionally another country.
    private static Supplier SupplierWith(string field, string? value, string country = "Netherlands")
    {
        var supplier = ValidSupplier();
        supplier.Country = country;
        typeof(Supplier).GetProperty(field)!.SetValue(supplier, value);
        return supplier;
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

    // One test case per required field.
    public static TheoryData<string> Fields => [.. RequiredFields];

    // ---- Create ----

    // A complete supplier passes validation.
    [Fact]
    public async Task CreateAsync_ValidSupplier_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidSupplier()));
    }

    // The server sets the id and timestamps (in UTC); values from the request are ignored.
    [Fact]
    public async Task CreateAsync_IgnoresIdAndTimestampsFromTheRequest()
    {
        var supplier = ValidSupplier();
        supplier.Id = 42;
        supplier.CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        supplier.UpdatedAt = supplier.CreatedAt;
        var before = DateTime.UtcNow;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(supplier));

        Assert.Equal(0, supplier.Id);
        Assert.True(supplier.CreatedAt >= before);
        Assert.Equal(DateTimeKind.Utc, supplier.CreatedAt.Kind);
        Assert.Equal(supplier.CreatedAt, supplier.UpdatedAt);
    }

    // Whitespace around fields is removed before checking and saving.
    [Fact]
    public async Task CreateAsync_TrimsFields()
    {
        var supplier = ValidSupplier();
        supplier.Code = "  SUP-0001 ";
        supplier.ZipCode = "\t3089 JH\n";
        supplier.PhoneNumber = " 010-7654321 ";

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(supplier));

        Assert.Equal(
            ("SUP-0001", "3089 JH", "010-7654321"),
            (supplier.Code, supplier.ZipCode, supplier.PhoneNumber));
    }

    // Null, empty or blank fields are rejected, naming the field.
    [Theory]
    [MemberData(nameof(Fields))]
    public async Task CreateAsync_MissingField_IsRejected(string field)
    {
        foreach (var value in new[] { null, "", "   " })
        {
            var message = await AssertInvalidAsync(() => _service.CreateAsync(SupplierWith(field, value)));

            Assert.Equal($"Missing required fields: {field}.", message);
        }
    }

    // All missing fields are listed in one message, in order.
    [Fact]
    public async Task CreateAsync_SeveralMissingFields_ListsAllOfThem()
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(new Supplier()));

        Assert.Equal($"Missing required fields: {string.Join(", ", RequiredFields)}.", message);
    }

    // 255 characters is allowed; 256 is rejected, naming the field.
    [Fact]
    public async Task CreateAsync_FieldLongerThan255Characters_IsRejected()
    {
        await AssertReachesDatabaseAsync(() =>
            _service.CreateAsync(SupplierWith(nameof(Supplier.Name), new string('a', 255))));

        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(SupplierWith(nameof(Supplier.Name), new string('a', 256))));

        Assert.Equal("Fields must not exceed 255 characters: Name.", message);
    }

    // Line breaks and tabs inside a field are rejected, naming the field.
    [Theory]
    [InlineData(nameof(Supplier.Name), "Koffie\nGroothandel")]
    [InlineData(nameof(Supplier.Reference), "REF\t01")]
    public async Task CreateAsync_ControlCharacterInField_IsRejected(string field, string value)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(SupplierWith(field, value)));

        Assert.Equal($"Fields must not contain control characters such as line breaks or tabs: {field}.", message);
    }

    // Examples of valid formats for each field with a format rule.
    [Theory]
    [InlineData(nameof(Supplier.Code), "SUP-0001")]
    [InlineData(nameof(Supplier.Code), "ABC123")]
    [InlineData(nameof(Supplier.Code), "a-b-c")]
    [InlineData(nameof(Supplier.Name), "7-Eleven")]
    [InlineData(nameof(Supplier.Address), "1e Middellandstraat 5")]
    [InlineData(nameof(Supplier.City), "'s-Hertogenbosch")]
    [InlineData(nameof(Supplier.City), "São Paulo")]
    [InlineData(nameof(Supplier.ContactName), "J. de Vries")]
    [InlineData(nameof(Supplier.ZipCode), "3089JH")]
    [InlineData(nameof(Supplier.ZipCode), "3089 jh", "NL")]
    [InlineData(nameof(Supplier.ZipCode), "12345-6789", "United States")]
    [InlineData(nameof(Supplier.PhoneNumber), "+31 (0)10 765 4321")]
    [InlineData(nameof(Supplier.PhoneNumber), "0612345678")]
    [InlineData(nameof(Supplier.Reference), "#1")]
    public async Task CreateAsync_ValidFormat_PassesValidation(string field, string value, string country = "Netherlands")
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(SupplierWith(field, value, country)));
    }

    // Examples of invalid formats for each field with a format rule, with the error each should give.
    public static TheoryData<string, string, string, string> InvalidFormats => new()
    {
        { nameof(Supplier.Code), "SUP_0001", "Netherlands", CodeError },
        { nameof(Supplier.Code), "SUP--0001", "Netherlands", CodeError },
        { nameof(Supplier.Code), "-SUP0001", "Netherlands", CodeError },
        { nameof(Supplier.Code), "SUP 0001", "Netherlands", CodeError },
        { nameof(Supplier.Name), "!!!", "Netherlands", "Name must contain at least one letter or digit." },
        { nameof(Supplier.Address), "12345", "Netherlands", "Address must contain a street name." },
        { nameof(Supplier.City), "Rotterdam1", "Netherlands", $"City {FieldRules.NameError}" },
        { nameof(Supplier.Province), "Zuid_Holland", "Netherlands", $"Province {FieldRules.NameError}" },
        { nameof(Supplier.Country), "123", "Netherlands", $"Country {FieldRules.NameError}" },
        { nameof(Supplier.ContactName), "<script>", "Netherlands", $"ContactName {FieldRules.NameError}" },
        { nameof(Supplier.ZipCode), "0123 AB", "Netherlands", $"ZipCode {FieldRules.DutchZipCodeError}" },
        { nameof(Supplier.ZipCode), "28301", "NL", $"ZipCode {FieldRules.DutchZipCodeError}" },
        { nameof(Supplier.ZipCode), "123--45", "United States", $"ZipCode {FieldRules.ZipCodeError}" },
        { nameof(Supplier.PhoneNumber), "12345678", "Netherlands", $"PhoneNumber {FieldRules.PhoneError}" },
        { nameof(Supplier.PhoneNumber), "010-76543ab", "Netherlands", $"PhoneNumber {FieldRules.PhoneError}" },
        { nameof(Supplier.Reference), "---", "Netherlands", "Reference must contain at least one letter or digit." }
    };

    private const string CodeError = "Code may only contain letters, digits and single hyphens, such as 'SUP-0001'.";

    // Each invalid format is rejected with the error for that field.
    [Theory]
    [MemberData(nameof(InvalidFormats))]
    public async Task CreateAsync_InvalidFormat_IsRejected(string field, string value, string country, string expected)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(SupplierWith(field, value, country)));

        Assert.Equal(expected, message);
    }

    // All format errors are reported together, in field order.
    [Fact]
    public async Task CreateAsync_SeveralFormatErrors_ListsAllOfThem()
    {
        var supplier = ValidSupplier();
        supplier.Code = "SUP_1";
        supplier.City = "Rotterdam1";
        supplier.PhoneNumber = "123";

        var message = await AssertInvalidAsync(() => _service.CreateAsync(supplier));

        Assert.Equal(
            $"{CodeError} City {FieldRules.NameError} PhoneNumber {FieldRules.PhoneError}",
            message);
    }

    // ---- Update (same checks as create) ----

    // A complete supplier passes validation.
    [Fact]
    public async Task UpdateAsync_ValidSupplier_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, ValidSupplier()));
    }

    // An invalid supplier is rejected; the individual rules are tested under create.
    [Fact]
    public async Task UpdateAsync_InvalidSupplier_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.UpdateAsync(1, SupplierWith(nameof(Supplier.PhoneNumber), "123")));

        Assert.Equal($"PhoneNumber {FieldRules.PhoneError}", message);
    }

    // A missing field is also rejected on update.
    [Fact]
    public async Task UpdateAsync_MissingField_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.UpdateAsync(1, SupplierWith(nameof(Supplier.Code), "")));

        Assert.Equal("Missing required fields: Code.", message);
    }
}