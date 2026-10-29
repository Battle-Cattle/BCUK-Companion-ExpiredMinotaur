using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class FitnessMachineScannerTests
{
    private readonly FakeTimeProvider time = new();
    private readonly FakeAdvertisementWatcher watcher = new();
    private readonly FitnessMachineScanner scanner;

    public FitnessMachineScannerTests()
    {
        scanner = new FitnessMachineScanner(() => watcher, time);
    }

    [Fact]
    public async Task FindsTheFirstDeviceAdvertisingTheFitnessMachineService()
    {
        watcher.OnStart.Add(new BleAdvertisement(1, [Guid.NewGuid()]));
        watcher.OnStart.Add(new BleAdvertisement(2, [FtmsConstants.FitnessMachineService]));
        watcher.OnStart.Add(new BleAdvertisement(3, [FtmsConstants.FitnessMachineService]));

        var address = await scanner.FindFitnessMachineAddressAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2UL, address);
        Assert.True(watcher.Started);
        Assert.True(watcher.Stopped);
    }

    [Fact]
    public async Task AScanThatFindsTheTreadmill_StopsItsTimeoutTimer()
    {
        var timers = new CountingTimeProvider(time);
        watcher.OnStart.Add(new BleAdvertisement(2, [FtmsConstants.FitnessMachineService]));

        Assert.Equal(2UL, await new FitnessMachineScanner(() => watcher, timers).FindFitnessMachineAddressAsync(TimeSpan.FromSeconds(10)));

        // The clean-up runs as a continuation, so give it a moment.
        for (var i = 0; i < 100 && timers.Live > 0; i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(0, timers.Live);
    }

    /// <summary>Counts the timers created through it that haven't been disposed yet.</summary>
    private sealed class CountingTimeProvider(TimeProvider inner) : TimeProvider
    {
        private int live;

        public int Live => Volatile.Read(ref live);

        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref live);
            return new CountedTimer(inner.CreateTimer(callback, state, dueTime, period), () => Interlocked.Decrement(ref live));
        }

        private sealed class CountedTimer(ITimer timer, Action disposed) : ITimer
        {
            private int isDisposed;

            public bool Change(TimeSpan dueTime, TimeSpan period) => timer.Change(dueTime, period);

            public void Dispose()
            {
                if (Interlocked.Exchange(ref isDisposed, 1) == 0)
                {
                    timer.Dispose();
                    disposed();
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    [Fact]
    public async Task ReturnsNullOnceTheTimeoutPasses()
    {
        var scan = scanner.FindFitnessMachineAddressAsync(TimeSpan.FromSeconds(10));
        time.Advance(TimeSpan.FromSeconds(10));

        Assert.Null(await scan);
        Assert.True(watcher.Stopped);

        // A device found after the scan ended doesn't change the result.
        watcher.Send(new BleAdvertisement(2, [FtmsConstants.FitnessMachineService]));
        Assert.Null(await scan);
    }

    [Fact]
    public async Task ReturnsNullWhenCancelled()
    {
        using var cts = new CancellationTokenSource();

        var scan = scanner.FindFitnessMachineAddressAsync(TimeSpan.FromSeconds(10), cts.Token);
        cts.Cancel();

        Assert.Null(await scan);
        Assert.True(watcher.Stopped);
    }
}
