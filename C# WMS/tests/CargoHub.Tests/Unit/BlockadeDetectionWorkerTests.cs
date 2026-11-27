using CargoHub.Api.Services;
using CargoHub.Api.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CargoHub.Tests.Unit;

public sealed class BlockadeDetectionWorkerTests
{
    private sealed class FakeDetectionService(bool fail = false) : IBlockadeDetectionService
    {
        public int Calls { get; private set; }

        public Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (fail)
                throw new InvalidOperationException("Database is down.");
            return Task.FromResult(0);
        }
    }

    private sealed class FakeSettingsService(int intervalMinutes = 7, bool fail = false) : IBlockadeSettingsService
    {
        public Task<BlockadeSettings> GetAsync(CancellationToken cancellationToken = default)
        {
            if (fail)
                throw new InvalidOperationException("Database is down.");
            return Task.FromResult(new BlockadeSettings { CheckIntervalMinutes = intervalMinutes });
        }

        public Task<BlockadeSettings> UpdateAsync(
            BlockadeSettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static BlockadeDetectionWorker CreateWorker(
        FakeDetectionService detection, FakeSettingsService? settings = null, bool enabled = true)
    {
        var provider = new ServiceCollection()
            .AddSingleton<IBlockadeDetectionService>(detection)
            .AddSingleton<IBlockadeSettingsService>(settings ?? new FakeSettingsService())
            .BuildServiceProvider();
        return new BlockadeDetectionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BlockadeDetectionOptions { Enabled = enabled, CheckIntervalMinutes = 15 }),
            NullLogger<BlockadeDetectionWorker>.Instance);
    }

    [Fact]
    public async Task RunCheckAsync_CallsTheDetectionService()
    {
        var detection = new FakeDetectionService();
        var worker = CreateWorker(detection);

        await worker.RunCheckAsync(CancellationToken.None);

        Assert.Equal(1, detection.Calls);
    }

    [Fact]
    public async Task RunCheckAsync_DoesNotThrow_WhenTheDetectionServiceFails()
    {
        var detection = new FakeDetectionService(fail: true);
        var worker = CreateWorker(detection);

        var exception = await Record.ExceptionAsync(() => worker.RunCheckAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Equal(1, detection.Calls);
    }

    [Fact]
    public async Task GetIntervalAsync_ReturnsTheStoredInterval()
    {
        var worker = CreateWorker(new FakeDetectionService(), new FakeSettingsService(intervalMinutes: 7));

        var interval = await worker.GetIntervalAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(7), interval);
    }

    [Fact]
    public async Task GetIntervalAsync_UsesTheConfiguredInterval_WhenTheSettingsCannotBeRead()
    {
        var worker = CreateWorker(new FakeDetectionService(), new FakeSettingsService(fail: true));

        var interval = await worker.GetIntervalAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(15), interval);
    }

    [Fact]
    public async Task Worker_DoesNothing_WhenDetectionIsDisabled()
    {
        var detection = new FakeDetectionService();
        var worker = CreateWorker(detection, enabled: false);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, detection.Calls);
    }
}
