using CargoHub.Api.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Testcontainers.MySql;

namespace CargoHub.Tests.Fixtures;

/// <summary>
/// Starts a MySQL container from the project's schema and seed, and runs the API
/// in memory against it. Shared by every test in the "Api" collection; needs Docker.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("cargohub")
        .WithUsername("cargohub")
        .WithPassword("cargohub-tests")
        .WithResourceMapping(new FileInfo(Path.Combine(SqlDirectory, "01-schema.sql")), "/docker-entrypoint-initdb.d/")
        .WithResourceMapping(new FileInfo(Path.Combine(SqlDirectory, "02-seed.sql")), "/docker-entrypoint-initdb.d/")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public HttpClient Client { get; private set; } = null!;

    // API key of a seeded account that may only use GET.
    public string ReadOnlyApiKey { get; private set; } = null!;

    public string ConnectionString => _mysql.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        await WaitForSeedAsync();

        // This superroot account exists only in the disposable test database. Keep production
        // authentication enabled while controller tests exercise all CRUD routes.
        var apiKey = Guid.NewGuid().ToString("N");
        await ExecuteAsync("""
            INSERT INTO `account` (`role`, `name`, `email`, `warehouse_id`, `app`, `api_key_hash`, `created_at`, `updated_at`)
            VALUES ('superroot', 'Integration Tests', 'integration-tests@cargohub.nl', NULL, 'integration-tests',
                    SHA2(@key, 256), UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
            """, ("@key", apiKey));
        await ExecuteAsync("""
            INSERT INTO `account_endpoint` (`account_id`, `resource`)
            SELECT a.`id`, e.`resource` FROM `account` a
            CROSS JOIN (SELECT DISTINCT `resource` FROM `account_endpoint`) e
            WHERE a.`email` = 'integration-tests@cargohub.nl'
            """);
        await ExecuteAsync("""
            INSERT INTO `account_permission` (`account_id`, `resource`, `method`, `allowed`)
            SELECT a.`id`, p.`resource`, p.`method`, 1 FROM `account` a
            CROSS JOIN (SELECT DISTINCT `resource`, `method` FROM `account_permission`) p
            WHERE a.`email` = 'integration-tests@cargohub.nl'
            """);

        // The seeded user of warehouse 1 may only read.
        ReadOnlyApiKey = Guid.NewGuid().ToString("N");
        await ExecuteAsync(
            "UPDATE `account` SET `api_key_hash` = SHA2(@key, 256) WHERE `email` = 'user.vgh-amb@cargohub.nl'",
            ("@key", ReadOnlyApiKey));

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Not "Development", so a developer's own user secrets are not loaded.
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:CargoHub"] = ConnectionString }));
        });
        Client = _factory.CreateClient();
        Client.DefaultRequestHeaders.Add(ApiKeyMiddleware.HeaderName, apiKey);
    }

    public HttpClient CreateUnauthenticatedClient() => _factory!.CreateClient();

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (_factory is not null)
            await _factory.DisposeAsync();
        await _mysql.DisposeAsync();
    }

    public async Task<int> QueryIntAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task<string> QueryStringAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    // MySQL runs the init scripts on a temporary server that is not reachable over TCP,
    // so the seed is complete once a TCP connection sees the data.
    private async Task WaitForSeedAsync()
    {
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (true)
        {
            try
            {
                if (await QueryIntAsync("SELECT COUNT(*) FROM `shipment`") > 0)
                    return;
            }
            catch (MySqlException) when (DateTime.UtcNow < deadline)
            {
            }
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    private static string SqlDirectory
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "src", "CargoHub.Api", "Data", "MySql");
                if (Directory.Exists(candidate))
                    return candidate;
            }
            throw new DirectoryNotFoundException("Could not find src/CargoHub.Api/Data/MySql.");
        }
    }
}

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
}
