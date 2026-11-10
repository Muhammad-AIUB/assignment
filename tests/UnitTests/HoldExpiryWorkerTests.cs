using Api.BackgroundServices;
using Api.Services;
using Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace UnitTests;

/// <summary>The worker only schedules sweeps; what a sweep does to the data is covered by the integration tests.</summary>
public class HoldExpiryWorkerTests
{
    private sealed class FakeSweeper(Func<int, int> onCall) : IHoldExpirySweeper
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public Task<int> SweepAsync(CancellationToken ct) => Task.FromResult(onCall(Interlocked.Increment(ref _calls)));
    }

    private static HoldExpiryWorker CreateWorker(FakeSweeper sweeper, bool enabled = true, int intervalSeconds = 1)
    {
        var services = new ServiceCollection().AddSingleton<IHoldExpirySweeper>(sweeper).BuildServiceProvider();
        return new HoldExpiryWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new HoldExpirySweepOptions { Enabled = enabled, IntervalSeconds = intervalSeconds }),
            NullLogger<HoldExpiryWorker>.Instance);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(50);
        }
        return condition();
    }

    private static Task StopAsync(HoldExpiryWorker worker) =>
        worker.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

    [Fact]
    public async Task Sweeps_once_right_away_on_start_without_waiting_for_the_first_interval()
    {
        var sweeper = new FakeSweeper(_ => 0);
        var worker = CreateWorker(sweeper, intervalSeconds: 60);

        await worker.StartAsync(CancellationToken.None);
        var swept = await WaitUntilAsync(() => sweeper.Calls >= 1, TimeSpan.FromSeconds(5));
        await StopAsync(worker);

        Assert.True(swept, "expected a sweep on start, long before the 60s interval elapsed");
    }

    [Fact]
    public async Task Sweeps_again_every_interval()
    {
        var sweeper = new FakeSweeper(_ => 0);
        var worker = CreateWorker(sweeper, intervalSeconds: 1);

        await worker.StartAsync(CancellationToken.None);
        var sweptRepeatedly = await WaitUntilAsync(() => sweeper.Calls >= 3, TimeSpan.FromSeconds(10));
        await StopAsync(worker);

        Assert.True(sweptRepeatedly, $"expected 3 sweeps within 10s at a 1s interval, got {sweeper.Calls}");
    }

    [Fact]
    public async Task Keeps_running_after_a_sweep_fails()
    {
        var sweeper = new FakeSweeper(call => call == 1
            ? throw new InvalidOperationException("database is down")
            : 0);
        var worker = CreateWorker(sweeper, intervalSeconds: 1);

        await worker.StartAsync(CancellationToken.None);
        var recovered = await WaitUntilAsync(() => sweeper.Calls >= 2, TimeSpan.FromSeconds(10));
        var faulted = worker.ExecuteTask!.IsFaulted;
        await StopAsync(worker);

        Assert.True(recovered, "the worker should try again on the next tick after a failed sweep");
        Assert.False(faulted, "a failed sweep must not take the worker (or the host) down");
    }

    [Fact]
    public async Task Does_nothing_when_disabled()
    {
        var sweeper = new FakeSweeper(_ => 0);
        var worker = CreateWorker(sweeper, enabled: false, intervalSeconds: 1);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        await StopAsync(worker);

        Assert.Equal(0, sweeper.Calls);
    }

    [Fact]
    public async Task Stops_promptly_when_the_host_shuts_down()
    {
        var sweeper = new FakeSweeper(_ => 0);
        var worker = CreateWorker(sweeper, intervalSeconds: 60);

        await worker.StartAsync(CancellationToken.None);
        var running = await WaitUntilAsync(() => sweeper.Calls >= 1, TimeSpan.FromSeconds(5));
        await StopAsync(worker);

        Assert.True(running, "the worker never started sweeping, so there was nothing to stop");
        Assert.True(worker.ExecuteTask!.IsCompleted, "the worker should end as soon as it is cancelled, not after the interval");
    }
}
