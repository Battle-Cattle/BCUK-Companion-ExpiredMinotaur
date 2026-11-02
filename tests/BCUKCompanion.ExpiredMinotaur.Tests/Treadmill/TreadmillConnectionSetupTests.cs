using System.Collections.Concurrent;
using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class TreadmillConnectionSetupTests
{
    private readonly FakeTimeProvider time = new();
    private readonly FakeAdvertisementWatcher watcher = new();
    private readonly FakeTreadmillDevice device = new() { SpeedWhileReadingRange = 3.0 };
    private readonly TreadmillSpeedControl speed = new();
    private readonly ConcurrentQueue<string> statuses = new();
    private readonly TreadmillConnectionSetup setup;

    public TreadmillConnectionSetupTests()
    {
        watcher.OnStart.Add(new BleAdvertisement(1, [FtmsConstants.FitnessMachineService]));
        // Stands in for the client, which feeds the device's speed readings to the speed state.
        device.TreadmillDataReceived += (_, data) =>
            speed.RecordBeltSpeed(TreadmillDataParser.Parse(data).InstantaneousSpeedKmh ?? 0, time.GetUtcNow().UtcDateTime);
        setup = new TreadmillConnectionSetup(new FitnessMachineScanner(() => watcher, time), speed, time, statuses.Enqueue);
    }

    [Fact]
    public async Task RunAsync_ConnectsAppliesTheRangeAndSeedsTheTarget()
    {
        device.SpeedRange = (1.0, 9.0, 0.5);

        var service = await setup.RunAsync(device, CancellationToken.None);

        Assert.NotNull(service);
        Assert.True(device.IsConnected);
        Assert.Equal(new TreadmillSpeedRange(1.0, 9.0, 0.5), speed.Range);
        Assert.Equal((1.0, 9.0), (service.MinSpeedKmh, service.MaxSpeedKmh));
        Assert.Equal(3.0, speed.TargetSpeedKmh);
        Assert.Equal(["Scanning...", "Connecting...", "Requesting control..."], statuses);
        Assert.Equal(new byte[] { FtmsConstants.Opcode.RequestControl }, Assert.Single(device.Writes));
    }

    [Fact]
    public async Task RunAsync_ReturnsNullWhenNothingIsFound()
    {
        watcher.OnStart.Clear();

        var run = setup.RunAsync(device, CancellationToken.None);
        time.Advance(TreadmillConnectionSetup.ScanTimeout);

        Assert.Null(await run);
        Assert.Equal("No fitness machine found. Is it powered on and in range?", statuses.Last());
        Assert.False(device.IsConnected);
    }

    [Fact]
    public async Task RunAsync_ReturnsNullWhenTheScanIsCancelled()
    {
        watcher.OnStart.Clear();
        using var cts = new CancellationTokenSource();

        var run = setup.RunAsync(device, cts.Token);
        cts.Cancel();

        Assert.Null(await run);
        Assert.Equal(["Scanning...", "Connect cancelled."], statuses);
    }

    [Fact]
    public async Task RunAsync_ReturnsNullWhenTheConnectionFails()
    {
        device.ConnectSucceeds = false;

        Assert.Null(await setup.RunAsync(device, CancellationToken.None));

        Assert.Equal("Connection failed.", statuses.Last());
        Assert.Empty(device.Writes);
    }

    [Fact]
    public async Task RunAsync_ResetsAPreviousDevicesRangeWhenTheReadFails()
    {
        speed.Range = new TreadmillSpeedRange(2.0, 6.0, 0.5);
        device.SpeedRange = null;

        var service = await setup.RunAsync(device, CancellationToken.None);

        Assert.NotNull(service);
        Assert.Equal(TreadmillSpeedRange.Default, speed.Range);
        Assert.Equal((FtmsConstants.MinSpeedKmh, FtmsConstants.MaxSpeedKmh), (service.MinSpeedKmh, service.MaxSpeedKmh));
        Assert.Contains(statuses, s => s.StartsWith("Could not read Supported Speed Range"));
    }

    [Fact]
    public async Task RunAsync_CarriesOnWhenControlIsRefused()
    {
        device.RequestControlResult = FtmsConstants.ResultControlNotPermitted;

        Assert.NotNull(await setup.RunAsync(device, CancellationToken.None));

        Assert.Contains(statuses, s => s.StartsWith("Connected, but control request failed"));
    }

    [Fact]
    public async Task RunAsync_SeedsTheTargetFromASpeedReportedWhileConnecting()
    {
        device.SpeedWhileReadingRange = null;
        device.SpeedWhileConnecting = 4.2;

        Assert.NotNull(await setup.RunAsync(device, CancellationToken.None));

        Assert.Equal(4.2, speed.TargetSpeedKmh, 3);
    }

    [Fact]
    public async Task RunAsync_StartsFromTheMinimumWhenNoSpeedArrivesInTime()
    {
        device.SpeedWhileReadingRange = null;

        var run = setup.RunAsync(device, CancellationToken.None);
        time.Advance(TreadmillConnectionSetup.FirstReadingTimeout);

        Assert.NotNull(await run);
        Assert.Equal(speed.Range.MinKmh, speed.TargetSpeedKmh);
        Assert.StartsWith("No speed reading within 3s", statuses.Last());
    }

    [Fact]
    public async Task RunAsync_ReturnsNullWhenCancelledWhileWaitingForASpeed_LeavingTheDeviceForTheCallerToDrop()
    {
        device.SpeedWhileReadingRange = null;
        using var cts = new CancellationTokenSource();

        var run = setup.RunAsync(device, cts.Token);
        cts.Cancel();

        Assert.Null(await run);
        Assert.Equal("Connect cancelled.", statuses.Last());
        Assert.True(device.IsConnected);
        Assert.Equal(0, speed.TargetSpeedKmh);
    }

    [Fact]
    public async Task RunAsync_StopsWaitingForAFirstReadingOnceDone()
    {
        device.ConnectSucceeds = false;
        await setup.RunAsync(device, CancellationToken.None);

        // A late reading after a failed connect must not seed anything or throw.
        speed.RecordBeltSpeed(5.0, time.GetUtcNow().UtcDateTime);

        Assert.Equal(5.0, speed.CurrentSpeedKmh);
        Assert.Equal(0, speed.TargetSpeedKmh);
    }
}
