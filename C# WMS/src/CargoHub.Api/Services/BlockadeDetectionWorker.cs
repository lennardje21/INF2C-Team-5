using CargoHub.Api.Settings;
using Microsoft.Extensions.Options;

namespace CargoHub.Api.Services;

// Background job that starts a blockade check every few minutes for as long as the API runs.
// Both API containers run it; the lock in BlockadeDetectionService makes sure only one of
// them checks at a time. The first check runs after one interval, not at startup.
public sealed class BlockadeDetectionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BlockadeDetectionOptions> options,
    ILogger<BlockadeDetectionWorker> logger
) : BackgroundService
{
    private readonly BlockadeDetectionOptions defaults = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!defaults.Enabled)
        {
            logger.LogInformation("Blockade detection is switched off.");
            return;
        }

        logger.LogInformation("Blockade detection starts.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Read before every wait, so a change through the settings endpoint applies
                // without a restart (from the next wait on).
                var interval = await GetIntervalAsync(stoppingToken);
                await Task.Delay(interval, stoppingToken);
                await RunCheckAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The app is shutting down.
        }
    }

    // The time between two checks: the stored setting, or the configured default when the
    // database cannot be read.
    public async Task<TimeSpan> GetIntervalAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<IBlockadeSettingsService>();
            var current = await settings.GetAsync(cancellationToken);
            return TimeSpan.FromMinutes(current.CheckIntervalMinutes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not read the blockade settings; using the configured interval.");
            return TimeSpan.FromMinutes(defaults.CheckIntervalMinutes);
        }
    }

    // One tick. Any other error is only logged, so a failed check never stops the job.
    // The service is scoped, so each tick gets its own scope.
    public async Task RunCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IBlockadeDetectionService>();
            await service.RunOnceAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Blockade check failed; trying again at the next interval.");
        }
    }
}
