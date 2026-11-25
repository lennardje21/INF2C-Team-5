namespace CargoHub.Api.Services;

// One round of blockade detection. The background worker calls this on every tick.
public interface IBlockadeDetectionService
{
    // Returns how many blockades this round handled (mailed, or recorded when there was
    // nobody to mail). Returns 0 when nothing was blocked, or when another API container
    // already holds the lock and this round was skipped.
    Task<int> RunOnceAsync(CancellationToken cancellationToken = default);
}
