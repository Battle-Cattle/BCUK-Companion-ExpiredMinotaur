using System.Collections.Concurrent;
using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class TreadmillClientTests : IDisposable
{
    private const ulong Address = 0xA1B2C3D4E5F6;

    private readonly FakeTimeProvider time = new();
    private readonly FakeAdvertisementWatcher watcher = new();
    private readonly List<FakeTreadmillDevice> devices = [];
    private readonly ConcurrentQueue<string> statuses = new();
    private readonly TreadmillClient client;

    public TreadmillClientTests()
    {
        watcher.OnStart.Add(new BleAdvertisement(Address, [FtmsConstants.FitnessMachineService]));
        client = new TreadmillClient(
            new FitnessMachineScanner(() => watcher, time),
            () =>
            {
                var device = new FakeTreadmillDevice { SpeedWhileReadingRange = 3.0 };
                devices.Add(device);
                return device;
            },
            time);
        client.StatusChanged += (_, message) => statuses.Enqueue(message);
    }

    private FakeTreadmillDevice Device => devices[^1];

    public void Dispose() => client.Dispose();

    private async Task ConnectAsync()
    {
        Assert.True(await client.ConnectAsync());
        statuses.Clear();
    }

    [Fact]
    public async Task ConnectAsync_ConnectsAndStartsFromTheBeltsSpeed()
    {
        Assert.True(await client.ConnectAsync());

        Assert.True(client.IsConnected);
        Assert.Equal(3.0, client.TargetSpeedKmh);
        Assert.Equal(3.0, client.CurrentSpeedKmh);
        Assert.Equal((0.5, 10.0, 0.1), (client.MinSpeedKmh, client.MaxSpeedKmh, client.SpeedStepKmh));
        Assert.Equal(["Scanning...", "Connecting...", "Requesting control...", "Connected."], statuses);
        Assert.Equal(new byte[] { FtmsConstants.Opcode.RequestControl }, Assert.Single(Device.Writes));
    }

    [Fact]
    public async Task ConnectAsync_ReportsTheDevicesSetupWarnings()
    {
        Device.Warnings.AddRange(["first", "second"]);

        await client.ConnectAsync();

        Assert.Equal("Connected. WARNING: first | second", statuses.Last());
    }

    [Fact]
    public async Task ConnectAsync_RefusesWhileAlreadyConnected()
    {
        await ConnectAsync();

        Assert.False(await client.ConnectAsync());
        Assert.Equal(["Already connected."], statuses);
    }

    [Fact]
    public async Task ConnectAsync_ReportsNoTreadmillWhenTheScanTimesOut()
    {
        watcher.OnStart.Clear();

        var connect = client.ConnectAsync();
        time.Advance(TimeSpan.FromSeconds(10));

        Assert.False(await connect);
        Assert.Contains("No fitness machine found", statuses.Last());
        Assert.True(watcher.Stopped);
    }

    [Fact]
    public async Task ConnectAsync_ReportsACancelledScan()
    {
        watcher.OnStart.Clear();
        using var cts = new CancellationTokenSource();

        var connect = client.ConnectAsync(cts.Token);
        cts.Cancel();

        Assert.False(await connect);
        Assert.Equal("Connect cancelled.", statuses.Last());
    }

    [Fact]
    public async Task ConnectAsync_ReportsAFailedConnection()
    {
        Device.ConnectSucceeds = false;

        Assert.False(await client.ConnectAsync());
        Assert.Equal("Connection failed.", statuses.Last());
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_KeepsTheDefaultRangeWhenTheIncrementIsInvalid()
    {
        Device.SpeedRange = (2.0, 8.0, 0.0);

        await client.ConnectAsync();

        Assert.Equal(
            (FtmsConstants.MinSpeedKmh, FtmsConstants.MaxSpeedKmh, FtmsConstants.SpeedStepKmh),
            (client.MinSpeedKmh, client.MaxSpeedKmh, client.SpeedStepKmh));
        Assert.Contains(statuses, s => s.Contains("invalid increment"));
    }

    [Fact]
    public async Task ConnectAsync_KeepsTheDefaultRangeWhenItCantBeRead()
    {
        Device.SpeedRange = null;

        await client.ConnectAsync();

        Assert.Equal(FtmsConstants.MinSpeedKmh, client.MinSpeedKmh);
        Assert.Contains(statuses, s => s.StartsWith("Could not read Supported Speed Range"));
    }

    [Fact]
    public async Task ConnectAsync_StillConnectsWhenControlIsRefused()
    {
        Device.RequestControlResult = FtmsConstants.ResultControlNotPermitted;

        Assert.True(await client.ConnectAsync());
        Assert.Contains(statuses, s => s.StartsWith("Connected, but control request failed"));
    }

    [Fact]
    public async Task ConnectAsync_StartsFromTheMinimumWhenNoSpeedArrives()
    {
        Device.SpeedWhileReadingRange = null;

        var connect = client.ConnectAsync();
        time.Advance(TimeSpan.FromSeconds(3));

        Assert.True(await connect);
        Assert.Equal(0.5, client.TargetSpeedKmh);
        Assert.Contains(statuses, s => s.StartsWith("No speed reading within 3s"));
    }

    [Fact]
    public async Task ConnectAsync_ClampsAStoppedBeltToTheMinimum()
    {
        Device.SpeedWhileReadingRange = 0;

        await client.ConnectAsync();

        Assert.Equal(0.5, client.TargetSpeedKmh);
        Assert.Contains(statuses, s => s.Contains("(stopped)"));
    }

    [Fact]
    public async Task ConnectAsync_CancelledWhileWaitingForASpeed_DropsTheLiveConnection()
    {
        Device.SpeedWhileReadingRange = null;
        using var cts = new CancellationTokenSource();

        var connect = client.ConnectAsync(cts.Token);
        cts.Cancel();

        Assert.False(await connect);
        Assert.Equal("Connect cancelled.", statuses.Last());
        Assert.True(devices[0].Disposed);
        Assert.Equal(2, devices.Count);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Dispose_DuringConnect_FinishesTheCleanupOnceConnectEnds()
    {
        Device.SpeedWhileReadingRange = null;
        var connect = client.ConnectAsync();

        // Connect holds the lock, so Dispose gives up waiting for it after a second and leaves the cleanup to Connect.
        await Task.Run(client.Dispose);
        time.Advance(TimeSpan.FromSeconds(3));

        Assert.False(await connect);
        Assert.True(Device.Disposed);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Dispose_ReleasesTheDevice_AndLaterCallsDoNothing()
    {
        await ConnectAsync();

        client.Dispose();

        Assert.True(Device.Disposed);
        Assert.False(client.IsConnected);
        Assert.False(await client.ConnectAsync());
        Assert.False(await client.NudgeSpeedAsync(1));
        await client.DisconnectAsync();
        Assert.Empty(statuses);
    }

    [Fact]
    public async Task NudgeSpeedAsync_DoesNothingWhileDisconnected()
    {
        Assert.False(await client.NudgeSpeedAsync(1));
        Assert.Empty(Device.Writes);
    }

    [Fact]
    public async Task NudgeSpeedAsync_SendsTheNewTargetAndKeepsIt()
    {
        await ConnectAsync();

        Assert.True(await client.NudgeSpeedAsync(1.0));

        Assert.Equal(4.0, client.TargetSpeedKmh, 6);
        Assert.Equal(new byte[] { FtmsConstants.Opcode.SetTargetSpeed, 0x90, 0x01 }, Device.Writes[^1]);
    }

    [Fact]
    public async Task NudgeSpeedAsync_ClampsToTheRange()
    {
        await ConnectAsync();

        Assert.True(await client.NudgeSpeedAsync(-10));

        Assert.Equal(0.5, client.TargetSpeedKmh);
    }

    [Fact]
    public async Task NudgeSpeedAsync_StartsFromASpeedSetOnTheConsole()
    {
        await ConnectAsync();
        Device.ReportSpeed(6.0);

        await client.NudgeSpeedAsync(1.0);

        Assert.Equal(7.0, client.TargetSpeedKmh, 6);
    }

    [Fact]
    public async Task NudgeSpeedAsync_IgnoresAStaleBeltSpeed()
    {
        await ConnectAsync();
        Device.ReportSpeed(6.0);
        time.Advance(TimeSpan.FromSeconds(6));

        await client.NudgeSpeedAsync(1.0);

        Assert.Equal(4.0, client.TargetSpeedKmh, 6);
    }

    [Fact]
    public async Task NudgeSpeedAsync_KeepsTheTargetWhenRejected()
    {
        await ConnectAsync();
        Device.Answer(() => new ControlPointWriteResult(FtmsConstants.Opcode.SetTargetSpeed, true, true, FtmsConstants.ResultInvalidParameter));

        Assert.False(await client.NudgeSpeedAsync(1.0));

        Assert.Equal(3.0, client.TargetSpeedKmh);
        Assert.StartsWith("Nudge failed:", Assert.Single(statuses));
    }

    [Fact]
    public async Task NudgeSpeedAsync_ReportsAFailedWrite()
    {
        await ConnectAsync();
        Device.Answer(() => throw new InvalidOperationException("radio gone"));

        Assert.False(await client.NudgeSpeedAsync(1.0));

        Assert.Equal("Nudge write failed: radio gone", Assert.Single(statuses));
    }

    [Fact]
    public async Task NudgeSpeedAsync_ReturnsFalseWhenCancelled()
    {
        await ConnectAsync();

        Assert.False(await client.NudgeSpeedAsync(1.0, new CancellationToken(canceled: true)));
        Assert.Single(Device.Writes);
    }

    [Fact]
    public async Task FollowTick_AdoptsASpeedSetOnTheConsoleOnceItSettles()
    {
        await ConnectAsync();

        for (var tick = 0; tick < 3; tick++)
        {
            Device.ReportSpeed(6.0);
            time.Advance(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(6.0, client.TargetSpeedKmh, 6);
        Assert.Contains("now following", Assert.Single(statuses));
        Assert.Single(Device.Writes); // following never writes to the treadmill
    }

    [Fact]
    public async Task FollowTick_HoldsTheTargetWithoutReadings()
    {
        await ConnectAsync();

        time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(3.0, client.TargetSpeedKmh);
        Assert.Empty(statuses);
    }

    [Fact]
    public async Task DisconnectAsync_DropsTheConnectionAndSwapsInAFreshDevice()
    {
        await ConnectAsync();

        await client.DisconnectAsync();

        Assert.False(client.IsConnected);
        Assert.Equal(0, client.CurrentSpeedKmh);
        Assert.True(devices[0].Disposed);
        Assert.Equal(2, devices.Count);
        Assert.Equal(["Disconnected."], statuses);
    }

    [Fact]
    public async Task ConnectionLost_DisconnectsAndSaysWhy()
    {
        await ConnectAsync();

        devices[0].LoseConnection();

        Assert.False(client.IsConnected);
        Assert.StartsWith("Treadmill disconnected", Assert.Single(statuses));
    }

    [Fact]
    public async Task ConnectionLost_FromAReplacedDevice_IsIgnored()
    {
        await ConnectAsync();
        await client.DisconnectAsync();
        await ConnectAsync();

        devices[0].LoseConnection();

        Assert.True(client.IsConnected);
        Assert.Empty(statuses);
    }

    [Fact]
    public async Task UnreadableTreadmillData_IsReported()
    {
        await ConnectAsync();

        Device.ReportRaw([0x00]);

        Assert.StartsWith("Treadmill Data parse error:", Assert.Single(statuses));
    }
}
