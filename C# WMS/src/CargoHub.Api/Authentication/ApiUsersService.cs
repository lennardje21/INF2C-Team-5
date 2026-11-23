using System.Security.Cryptography;
using System.Text;
using CargoHub.Api.Data;
using Microsoft.Extensions.Caching.Memory;

namespace CargoHub.Api.Authentication;

public sealed class ApiUsersService(MySqlConnectionFactory factory, IMemoryCache cache)
{
    private const string CacheKey = "api-users";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    // Only the SHA-256 hash of a key is stored, as lowercase hex (the same as MySQL's SHA2()).
    public static string Hash(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

	// Get the account of an api key, or null when the key is unknown
    public async Task<ApiUser?> GetByApiKeyAsync(
        string apiKey, CancellationToken cancellationToken = default)
    {
        var users = await cache.GetOrCreateAsync(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return LoadAsync(cancellationToken);
        });

        return users!.GetValueOrDefault(Hash(apiKey));
    }

	// Load all accounts that have a key, with their allowed permissions, in one query
    private async Task<IReadOnlyDictionary<string, ApiUser>> LoadAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT a.`api_key_hash`, a.`id`, a.`role`, a.`warehouse_id`, a.`app`, p.`resource`, p.`method` " +
            "FROM `account` a " +
            "LEFT JOIN `account_permission` p ON p.`account_id` = a.`id` AND p.`allowed` = 1 " +
            "WHERE a.`api_key_hash` IS NOT NULL";

        var accounts = new Dictionary<string, (int Id, string Role, int? WarehouseId, string App)>(
            StringComparer.Ordinal);
        var allowed = new Dictionary<string, HashSet<(string, string)>>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var hash = reader.GetString(0);
            if (!allowed.TryGetValue(hash, out var permissions))
            {
                accounts[hash] = (reader.GetInt32(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.GetString(4));
                allowed[hash] = permissions = [];
            }
            if (!reader.IsDBNull(5))
                permissions.Add((reader.GetString(5), reader.GetString(6).ToLowerInvariant()));
        }

        return accounts.ToDictionary(
            a => a.Key,
            a => new ApiUser(a.Value.Id, a.Value.Role, a.Value.WarehouseId, a.Value.App, allowed[a.Key]),
            StringComparer.Ordinal);
    }
}
