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
