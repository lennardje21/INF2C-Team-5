using CargoHub.Api.Settings;

namespace CargoHub.Api.Services;

// The blockade detection times that apply right now.
public interface IBlockadeSettingsService
{
    // The times an admin or root has stored, or the defaults when nothing is stored yet.
    Task<BlockadeSettings> GetAsync(CancellationToken cancellationToken = default);

    // Replaces all four values. Throws an ArgumentException (a 400) when one is not valid.
    Task<BlockadeSettings> UpdateAsync(BlockadeSettings settings, CancellationToken cancellationToken = default);
}
