using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySqlConnector;

namespace CargoHub.Api.Data;

/// <summary>
/// Reports unhealthy when MySQL cannot be reached, because then every /api request fails too.
/// </summary>
public sealed class MySqlHealthCheck(MySqlConnectionFactory connectionFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("MySQL is unreachable.", exception);
        }
    }
}
