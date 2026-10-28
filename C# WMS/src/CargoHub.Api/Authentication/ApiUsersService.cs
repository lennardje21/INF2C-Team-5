using CargoHub.Api.Data;
using Microsoft.Extensions.Caching.Memory;

namespace CargoHub.Api.Authentication;

public sealed class ApiUsersService(MySqlConnectionFactory factory, IMemoryCache cache)
{
    private const string CacheKey = "api-users";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

	// Get user by api key, or null when the key is unknown
    public async Task<ApiUser?> GetByApiKeyAsync(
        string apiKey, CancellationToken cancellationToken = default)
    {
        var users = await cache.GetOrCreateAsync(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return LoadAsync(cancellationToken);
        });

        return users!.GetValueOrDefault(apiKey);
    }

	// Load all users with their allowed permissions in one query
    private async Task<IReadOnlyDictionary<string, ApiUser>> LoadAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT u.`api_key`, u.`app`, p.`resource`, p.`method` FROM `user` u " +
            "LEFT JOIN `user_permission` p ON p.`api_key` = u.`api_key` AND p.`allowed` = 1";

        var apps = new Dictionary<string, string>(StringComparer.Ordinal);
        var allowed = new Dictionary<string, HashSet<(string, string)>>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var apiKey = reader.GetString(0);
            apps[apiKey] = reader.GetString(1);
            if (!allowed.TryGetValue(apiKey, out var permissions))
                allowed[apiKey] = permissions = [];
            if (!reader.IsDBNull(2))
                permissions.Add((reader.GetString(2), reader.GetString(3).ToLowerInvariant()));
        }

        return apps.ToDictionary(
            u => u.Key,
            u => new ApiUser(u.Key, u.Value, allowed[u.Key]),
            StringComparer.Ordinal);
    }
}
