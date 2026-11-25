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

        public Task<bool> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (fail)
                throw new InvalidOperationException("Database is down.");
            return Task.FromResult(true);
        }
    }

    private static BlockadeDetectionWorker CreateWorker(FakeDetectionService fake, bool enabled = true)
    {
        var provider = new ServiceCollection()
            .AddSingleton<IBlockadeDetectionService>(fake)
            .BuildServiceProvider();
        return new BlockadeDetectionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BlockadeDetectionOptions { Enabled = enabled }),
            NullLogger<BlockadeDetectionWorker>.Instance);
    }

    [Fact]
    public async Task RunCheckAsync_CallsTheDetectionService()
    {
        var fake = new FakeDetectionService();
        var worker = CreateWorker(fake);

        await worker.RunCheckAsync(CancellationToken.None);

        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task RunCheckAsync_DoesNotThrow_WhenTheDetectionServiceFails()
    {
        var fake = new FakeDetectionService(fail: true);
        var worker = CreateWorker(fake);

        var exception = await Record.ExceptionAsync(() => worker.RunCheckAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task Worker_DoesNothing_WhenDetectionIsDisabled()
    {
        var fake = new FakeDetectionService();
        var worker = CreateWorker(fake, enabled: false);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, fake.Calls);
    }
}
