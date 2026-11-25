using CargoHub.Api.Data;
using MySqlConnector;

namespace CargoHub.Api.Services;

// Runs one round of blockade detection. Production runs several API containers that all
// have their own background job, so a MySQL lock makes sure only one of them checks at a time.
public sealed class BlockadeDetectionService(
    MySqlConnectionFactory factory,
    ILogger<BlockadeDetectionService> logger
) : IBlockadeDetectionService
{
    private const string LockName = "cargohub_blockade_detection";

    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        // A MySQL lock belongs to one connection, so the same connection stays open until
        // the round is finished.
        await using var connection = await factory.OpenAsync(cancellationToken);

        if (!await TryAcquireLockAsync(connection, cancellationToken))
        {
            logger.LogInformation("Blockade check skipped: another container is already running it.");
            return false;
        }

        try
        {
            // FR-7.4 puts the detection and the mails here.
            logger.LogInformation("Blockade check ran.");
            return true;
        }
        finally
        {
            await ReleaseLockAsync(connection);
        }
    }

    // GET_LOCK with timeout 0 returns at once: 1 when we got the lock, 0 when someone else has it.
    private static async Task<bool> TryAcquireLockAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GET_LOCK(@name, 0)";
        command.Parameters.AddWithValue("@name", LockName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long acquired && acquired == 1;
    }

    // The connection goes back to the pool and is reused, so the lock is released by hand.
    private async Task ReleaseLockAsync(MySqlConnection connection)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT RELEASE_LOCK(@name)";
            command.Parameters.AddWithValue("@name", LockName);
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not release the blockade detection lock.");
        }
    }
}
