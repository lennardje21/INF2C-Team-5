namespace CargoHub.Api.Services;

// One round of blockade detection. The background worker calls this on every tick.
public interface IBlockadeDetectionService
{
    // Returns true when this round ran, and false when another API container already
    // holds the lock and this round was skipped.
    Task<bool> RunOnceAsync(CancellationToken cancellationToken = default);
}
