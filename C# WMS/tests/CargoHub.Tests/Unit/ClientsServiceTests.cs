using CargoHub.Api.Data;
using CargoHub.Api.Models;
using CargoHub.Api.Services;
using CargoHub.Api.Validation;
using Microsoft.Extensions.Configuration;

namespace CargoHub.Tests.Unit;

/// <summary>
/// Tests the validation in ClientsService without a database: valid input fails with
/// InvalidOperationException when it tries to connect, invalid input with ArgumentException.
/// Database behaviour is covered by ClientsControllerTests.
/// </summary>
public sealed class ClientsServiceTests
{
    // The required fields, in the order the error message lists them.
    private static readonly string[] RequiredFields =
    [
        nameof(Client.Name), nameof(Client.Address), nameof(Client.City), nameof(Client.ZipCode),
        nameof(Client.Province), nameof(Client.Country), nameof(Client.ContactName),
        nameof(Client.ContactPhone), nameof(Client.ContactEmail)
    ];

    private readonly ClientsService _service = CreateService();

    /// <summary>Builds the service without a connection string, so database access throws.</summary>
    private static ClientsService CreateService()
    {
        var factory = new MySqlConnectionFactory(new ConfigurationBuilder().Build());
        return new ClientsService(factory,
            new MySqlRepository<Client>(factory),
            new MySqlRepository<Order>(factory),
            new MySqlRepository<OrderItem>(factory));
    }

    /// <summary>A Dutch client that passes every check.</summary>
    private static Client ValidClient() => new()
    {
        Name = "Jumbo Supermarkten",
        Address = "Rijnhaven 12",
        City = "Rotterdam",
        ZipCode = "3011 AD",
        Province = "Zuid-Holland",
        Country = "Netherlands",
        ContactName = "Jan de Vries",
        ContactPhone = "010-1234567",
        ContactEmail = "jan.devries@example.nl"
    };

    /// <summary>A valid client with one field changed by name, and optionally another country.</summary>
    private static Client ClientWith(string field, string? value, string country = "Netherlands")
    {
        var client = ValidClient();
        client.Country = country;
        typeof(Client).GetProperty(field)!.SetValue(client, value);
        return client;
    }

    /// <summary>Asserts the input passed validation and reached the database.</summary>
    private static async Task AssertReachesDatabaseAsync(Func<Task> action)
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Contains("ConnectionStrings__CargoHub", e.Message);
    }

    /// <summary>Asserts the input was rejected and returns the error message.</summary>
    private static async Task<string> AssertInvalidAsync(Func<Task> action) =>
        (await Assert.ThrowsAsync<ArgumentException>(action)).Message;

    // One test case per required field.
    public static TheoryData<string> Fields => [.. RequiredFields];

    // ---- Create ----

    // A complete client passes validation.
    [Fact]
    public async Task CreateAsync_ValidClient_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ValidClient()));
    }

    // The server sets the id and timestamps (in UTC); values from the request are ignored.
    [Fact]
    public async Task CreateAsync_IgnoresIdAndTimestampsFromTheRequest()
    {
        var client = ValidClient();
        client.Id = 42;
        client.CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        client.UpdatedAt = client.CreatedAt;
        var before = DateTime.UtcNow;

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(client));

        Assert.Equal(0, client.Id);
        Assert.True(client.CreatedAt >= before);
        Assert.Equal(DateTimeKind.Utc, client.CreatedAt.Kind);
        Assert.Equal(client.CreatedAt, client.UpdatedAt);
    }

    // Whitespace around fields is removed before checking and saving.
    [Fact]
    public async Task CreateAsync_TrimsFields()
    {
        var client = ValidClient();
        client.Name = "  Jumbo Supermarkten ";
        client.ZipCode = "\t3011 AD\n";
        client.ContactEmail = " jan.devries@example.nl ";

        await AssertReachesDatabaseAsync(() => _service.CreateAsync(client));

        Assert.Equal(
            ("Jumbo Supermarkten", "3011 AD", "jan.devries@example.nl"),
            (client.Name, client.ZipCode, client.ContactEmail));
    }

    // Null, empty or blank fields are rejected, naming the field.
    [Theory]
    [MemberData(nameof(Fields))]
    public async Task CreateAsync_MissingField_IsRejected(string field)
    {
        foreach (var value in new[] { null, "", "   " })
        {
            var message = await AssertInvalidAsync(() => _service.CreateAsync(ClientWith(field, value)));

            Assert.Equal($"Missing required fields: {field}.", message);
        }
    }

    // All missing fields are listed in one message, in order.
    [Fact]
    public async Task CreateAsync_SeveralMissingFields_ListsAllOfThem()
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(new Client()));

        Assert.Equal($"Missing required fields: {string.Join(", ", RequiredFields)}.", message);
    }

    // 255 characters is allowed; 256 is rejected, naming the field.
    [Fact]
    public async Task CreateAsync_FieldLongerThan255Characters_IsRejected()
    {
        await AssertReachesDatabaseAsync(() =>
            _service.CreateAsync(ClientWith(nameof(Client.Name), new string('a', 255))));

        var message = await AssertInvalidAsync(() =>
            _service.CreateAsync(ClientWith(nameof(Client.Name), new string('a', 256))));

        Assert.Equal("Fields must not exceed 255 characters: Name.", message);
    }

    // Line breaks and tabs inside a field are rejected, naming the field.
    [Theory]
    [InlineData(nameof(Client.Name), "Jumbo\nTest")]
    [InlineData(nameof(Client.Address), "Rijnhaven\t12")]
    public async Task CreateAsync_ControlCharacterInField_IsRejected(string field, string value)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(ClientWith(field, value)));

        Assert.Equal($"Fields must not contain control characters such as line breaks or tabs: {field}.", message);
    }

    // Examples of valid formats for each field with a format rule.
    [Theory]
    [InlineData(nameof(Client.Name), "7-Eleven")]
    [InlineData(nameof(Client.Address), "1e Middellandstraat 5")]
    [InlineData(nameof(Client.City), "'s-Hertogenbosch")]
    [InlineData(nameof(Client.City), "São Paulo")]
    [InlineData(nameof(Client.ContactName), "J. de Vries")]
    [InlineData(nameof(Client.ZipCode), "3011AD")]
    [InlineData(nameof(Client.ZipCode), "3011 ad", "NL")]
    [InlineData(nameof(Client.ZipCode), "12345-6789", "United States")]
    [InlineData(nameof(Client.ZipCode), "SW1A 1AA", "United Kingdom")]
    [InlineData(nameof(Client.ContactPhone), "+31 (0)10 123 4567")]
    [InlineData(nameof(Client.ContactPhone), "0612345678")]
    [InlineData(nameof(Client.ContactEmail), "a+tag@sub.example.co.uk")]
    public async Task CreateAsync_ValidFormat_PassesValidation(string field, string value, string country = "Netherlands")
    {
        await AssertReachesDatabaseAsync(() => _service.CreateAsync(ClientWith(field, value, country)));
    }

    // Examples of invalid formats for each field with a format rule, with the error each should give.
    public static TheoryData<string, string, string, string> InvalidFormats => new()
    {
        { nameof(Client.Name), "!!!", "Netherlands", "Name must contain at least one letter or digit." },
        { nameof(Client.Address), "12345", "Netherlands", "Address must contain a street name." },
        { nameof(Client.City), "Rotterdam1", "Netherlands", $"City {FieldRules.NameError}" },
        { nameof(Client.Province), "Zuid_Holland", "Netherlands", $"Province {FieldRules.NameError}" },
        { nameof(Client.ContactName), "<script>", "Netherlands", $"ContactName {FieldRules.NameError}" },
        { nameof(Client.ZipCode), "0123 AB", "Netherlands", $"ZipCode {FieldRules.DutchZipCodeError}" },
        { nameof(Client.ZipCode), "28301", "NL", $"ZipCode {FieldRules.DutchZipCodeError}" },
        { nameof(Client.ZipCode), "123--45", "United States", $"ZipCode {FieldRules.ZipCodeError}" },
        { nameof(Client.ContactPhone), "12345678", "Netherlands", $"ContactPhone {FieldRules.PhoneError}" },
        { nameof(Client.ContactPhone), "010-12345ab", "Netherlands", $"ContactPhone {FieldRules.PhoneError}" },
        { nameof(Client.ContactPhone), "(010 1234567", "Netherlands", $"ContactPhone {FieldRules.PhoneError}" },
        { nameof(Client.ContactEmail), "test@example", "Netherlands", $"ContactEmail {FieldRules.EmailError}" },
        { nameof(Client.ContactEmail), "te..st@example.com", "Netherlands", $"ContactEmail {FieldRules.EmailError}" }
    };

    // Each invalid format is rejected with the error for that field.
    [Theory]
    [MemberData(nameof(InvalidFormats))]
    public async Task CreateAsync_InvalidFormat_IsRejected(string field, string value, string country, string expected)
    {
        var message = await AssertInvalidAsync(() => _service.CreateAsync(ClientWith(field, value, country)));

        Assert.Equal(expected, message);
    }

    // All format errors are reported together, in field order.
    [Fact]
    public async Task CreateAsync_SeveralFormatErrors_ListsAllOfThem()
    {
        var client = ValidClient();
        client.City = "Rotterdam1";
        client.ContactPhone = "123";
        client.ContactEmail = "not-an-email";

        var message = await AssertInvalidAsync(() => _service.CreateAsync(client));

        Assert.Equal(
            $"City {FieldRules.NameError} ContactPhone {FieldRules.PhoneError} ContactEmail {FieldRules.EmailError}",
            message);
    }

    // ---- Update (same checks as create) ----

    // A complete client passes validation.
    [Fact]
    public async Task UpdateAsync_ValidClient_PassesValidation()
    {
        await AssertReachesDatabaseAsync(() => _service.UpdateAsync(1, ValidClient()));
    }

    // An invalid client is rejected; the individual rules are tested under create.
    [Fact]
    public async Task UpdateAsync_InvalidClient_IsRejected()
    {
        var message = await AssertInvalidAsync(() =>
            _service.UpdateAsync(1, ClientWith(nameof(Client.ContactEmail), "not-an-email")));

        Assert.Equal($"ContactEmail {FieldRules.EmailError}", message);
    }
}
