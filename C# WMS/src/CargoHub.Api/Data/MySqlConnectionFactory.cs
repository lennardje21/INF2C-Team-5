using MySqlConnector;

namespace CargoHub.Api.Data;

public sealed class MySqlConnectionFactory(IConfiguration configuration)
{
    public Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken = default) =>
        OpenAsync(healthCheck: false, cancellationToken);

    // Opens a connection from a separate pool of at most 2 connections. MySqlConnector keeps
    // one pool per connection string, so a different application name gives a different pool.
    // When requests use up the normal pool under heavy load, /health then does not have to
    // wait behind them, and the watchdog does not restart a container that is only busy.
    public Task<MySqlConnection> OpenHealthCheckAsync(CancellationToken cancellationToken = default) =>
        OpenAsync(healthCheck: true, cancellationToken);

    private async Task<MySqlConnection> OpenAsync(bool healthCheck, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("CargoHub");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Set ConnectionStrings__CargoHub or the CargoHub user secret before accessing MySQL.");

        var settings = new MySqlConnectionStringBuilder(connectionString)
        {
            DateTimeKind = MySqlDateTimeKind.Utc,
            UseAffectedRows = false
        };
        if (healthCheck)
        {
            settings.ApplicationName = "cargohub-health";
            settings.MaximumPoolSize = 2;
        }

        var connection = new MySqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
