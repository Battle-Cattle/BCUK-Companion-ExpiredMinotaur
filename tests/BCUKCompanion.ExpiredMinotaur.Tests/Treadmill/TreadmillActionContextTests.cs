using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Ble;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;

public sealed class TreadmillActionContextTests
{
    private static TreadmillClient CreateClient(FakeTreadmillDevice device, FakeTimeProvider time)
    {
        var watcher = new FakeAdvertisementWatcher();
        watcher.OnStart.Add(new BleAdvertisement(1, [FtmsConstants.FitnessMachineService]));
        return new TreadmillClient(new FitnessMachineScanner(() => watcher, time), () => device, time);
    }

    [Fact]
    public void ProvidesOnlyItself()
    {
        using var client = CreateClient(new FakeTreadmillDevice(), new FakeTimeProvider());
        var context = new TreadmillActionContext(client);

        Assert.Same(client, context.Client);
        Assert.Same(context, context.GetService(typeof(TreadmillActionContext)));
        Assert.Null(context.GetService(typeof(TreadmillClient)));
    }

    [Fact]
    public async Task NudgeSpeedAction_NudgesTheConnectedTreadmill()
    {
        var device = new FakeTreadmillDevice { SpeedWhileReadingRange = 3.0 };
        using var client = CreateClient(device, new FakeTimeProvider());
        Assert.True(await client.ConnectAsync());

        var ok = await new TreadmillNudgeSpeedAction { DeltaKmh = 1.0 }
            .ExecuteAsync(new TreadmillActionContext(client), CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(4.0, client.TargetSpeedKmh, 6);
    }

    [Fact]
    public async Task NudgeSpeedAction_FailsWhileDisconnected()
    {
        using var client = CreateClient(new FakeTreadmillDevice(), new FakeTimeProvider());

        Assert.False(await new TreadmillNudgeSpeedAction { DeltaKmh = 1.0 }
            .ExecuteAsync(new TreadmillActionContext(client), CancellationToken.None));
    }
}
