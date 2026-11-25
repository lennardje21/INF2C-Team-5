using System.Security.Cryptography;
using System.Text;
using CargoHub.Api.Data;

namespace CargoHub.Api.Authentication;

/// <summary>
/// Looks up the account of an API key in the database on every request, without a cache:
/// a new, replaced or deleted key, or changed access, applies at once on every API
/// container, also when changed with scripts/set-api-key.sh. The lookup uses the unique
/// index on api_key_hash.
/// </summary>
public sealed class ApiUsersService(MySqlConnectionFactory factory)
{
    // A new random key of 256 bits, as 64 hex characters (like scripts/set-api-key.sh).
    public static string GenerateKey() => RandomNumberGenerator.GetHexString(64, lowercase: true);

    // Only the SHA-256 hash of a key is stored, as lowercase hex (the same as MySQL's SHA2()).
    public static string Hash(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

	// Get the account of an api key with its allowed permissions, or null when the key is unknown
    public async Task<ApiUser?> GetByApiKeyAsync(
        string apiKey, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT a.`id`, a.`role`, a.`warehouse_id`, a.`app`, p.`resource`, p.`method` " +
            "FROM `account` a " +
            "LEFT JOIN `account_permission` p ON p.`account_id` = a.`id` AND p.`allowed` = 1 " +
            "WHERE a.`api_key_hash` = @hash AND a.`deleted_at` IS NULL";
        command.Parameters.AddWithValue("@hash", Hash(apiKey));

        ApiUser? user = null;
        var allowed = new HashSet<(string Resource, string Method)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            user ??= new ApiUser(reader.GetInt32(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.GetString(3), allowed);
            if (!reader.IsDBNull(4))
                allowed.Add((reader.GetString(4), reader.GetString(5).ToLowerInvariant()));
        }

        return user;
    }
}
