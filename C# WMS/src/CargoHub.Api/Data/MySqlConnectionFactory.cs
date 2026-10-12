using MySqlConnector;

namespace CargoHub.Api.Data;

public sealed class MySqlConnectionFactory(IConfiguration configuration)
{
    public async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken = default)
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
