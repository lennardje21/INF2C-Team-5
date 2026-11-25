using CargoHub.Api.Settings;
using Microsoft.Extensions.Options;

namespace CargoHub.Api.Services;

// Background job that starts a blockade check every CheckIntervalMinutes. The first check
// runs after one interval, not at startup.
public sealed class BlockadeDetectionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BlockadeDetectionOptions> options,
    ILogger<BlockadeDetectionWorker> logger
) : BackgroundService
{
    private readonly BlockadeDetectionOptions settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled)
        {
            logger.LogInformation("Blockade detection is switched off.");
            return;
        }

        logger.LogInformation(
            "Blockade detection starts, checking every {Minutes} minutes.",
            settings.CheckIntervalMinutes
        );

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(settings.CheckIntervalMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunCheckAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // The app is shutting down.
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Blockade check failed; trying again at the next interval.");
        }
    }
}
