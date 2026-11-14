using System.Runtime.CompilerServices;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests.Wiz;

public sealed class WizDeviceRelocatorTests : IDisposable
{
    private const string Mac = "a8bb50112233";

    private readonly ActionsConfigStore store = new($"BCUKCompanion.ExpiredMinotaur.Tests.{Guid.NewGuid():N}", Program.CreateRegistry());
    private readonly List<WizDiscoveredDevice> answering = [];

    public void Dispose()
    {
        string directory = Path.GetDirectoryName(store.ConfigFilePath)!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private async IAsyncEnumerable<WizDiscoveredDevice> Discover(TimeSpan window, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var device in answering)
        {
            await Task.Yield();
            yield return device;
        }
    }

    private WizDeviceRelocator Relocator() => new(Discover, store);

    private WizDevice SavedDevice(string ip, string? mac = Mac)
    {
        var device = new WizDevice(Guid.NewGuid(), "Desk", ip, WizDeviceType.Light, true, true, true, mac);
        store.Save(new ActionsConfig { Devices = [device] });
        return device;
    }

    [Fact]
    public async Task ADeviceFoundByMacAtANewAddress_IsReturnedAndSaved()
    {
        var device = SavedDevice("192.168.1.20");
        answering.Add(new WizDiscoveredDevice("192.168.1.99", "OTHER", "ffffffffffff"));
        answering.Add(new WizDiscoveredDevice("192.168.1.45", "ESP25_SHRGB_01", "A8:BB:50:11:22:33"));

        var moved = await Relocator().RelocateAsync(device, CancellationToken.None);

        Assert.Equal(device with { IpAddress = "192.168.1.45" }, moved);
        Assert.Equal("192.168.1.45", Assert.Single(store.Load().Devices).IpAddress);
    }

    [Fact]
    public async Task ADeviceWithoutAMac_IsNotLookedFor()
    {
        var device = SavedDevice("192.168.1.20", mac: null);
        answering.Add(new WizDiscoveredDevice("192.168.1.45", "ESP25_SHRGB_01", Mac));

        Assert.Null(await Relocator().RelocateAsync(device, CancellationToken.None));
    }

    [Fact]
    public async Task ADeviceThatDoesntAnswer_OrAnswersFromTheSameAddress_IsNotMoved()
    {
        var device = SavedDevice("192.168.1.20");
        Assert.Null(await Relocator().RelocateAsync(device, CancellationToken.None));

        answering.Add(new WizDiscoveredDevice("192.168.1.20", "ESP25_SHRGB_01", Mac));
        Assert.Null(await Relocator().RelocateAsync(device, CancellationToken.None));
        Assert.Equal("192.168.1.20", Assert.Single(store.Load().Devices).IpAddress);
    }

    [Fact]
    public async Task ADeviceRemovedFromTheConfig_IsStillReturnedButNotSaved()
    {
        var device = new WizDevice(Guid.NewGuid(), "Gone", "192.168.1.20", WizDeviceType.Light, true, true, true, Mac);
        store.Save(new ActionsConfig());
        answering.Add(new WizDiscoveredDevice("192.168.1.45", "ESP25_SHRGB_01", Mac));

        Assert.Equal("192.168.1.45", (await Relocator().RelocateAsync(device, CancellationToken.None))?.IpAddress);
        Assert.Empty(store.Load().Devices);
    }

    [Fact]
    public async Task AnActionToADeviceThatMoved_IsSentToItsNewAddress()
    {
        using var fake = new FakeWizDevice((_, _) => """{"result":{"success":true}}""");
        // Saved at an address nothing answers on; discovery finds it on the fake's.
        var device = SavedDevice("127.0.0.9");
        answering.Add(new WizDiscoveredDevice(fake.IpAddress, "ESP25_SHRGB_01", Mac));
        var context = new WizActionContext(fake.CreateClient(), [device], Relocator());

        Assert.True(await new WizTurnOnAction { DeviceId = device.Id }.ExecuteAsync(context, CancellationToken.None));

        Assert.NotEmpty(fake.Requests);
        Assert.Equal(fake.IpAddress, Assert.Single(store.Load().Devices).IpAddress);
    }

    [Fact]
    public async Task AnActionToADeviceThatCantBeFound_Fails()
    {
        var device = SavedDevice("127.0.0.9");
        using var fake = new FakeWizDevice((_, _) => null);
        var context = new WizActionContext(fake.CreateClient(), [device], Relocator());

        Assert.False(await new WizTurnOnAction { DeviceId = device.Id }.ExecuteAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task AContextWithoutARelocator_DoesNotLook()
    {
        var device = new WizDevice(Guid.NewGuid(), "Desk", "127.0.0.9", WizDeviceType.Light, true, true, true, Mac);
        using var fake = new FakeWizDevice((_, _) => null);

        Assert.Null(await new WizActionContext(fake.CreateClient(), [device]).RelocateAsync(device, CancellationToken.None));
    }
}
