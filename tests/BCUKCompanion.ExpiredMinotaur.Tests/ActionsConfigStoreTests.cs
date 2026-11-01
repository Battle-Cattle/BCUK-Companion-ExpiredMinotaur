using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests;

public sealed class ActionsConfigStoreTests : IDisposable
{
    private static readonly Guid DeviceId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    private readonly string dataFolderName = $"BCUKCompanion.ExpiredMinotaur.Tests.{Guid.NewGuid():N}";
    private readonly ActionsConfigStore store;
    private readonly string directory;

    public ActionsConfigStoreTests()
    {
        var registry = new EventActionTypeRegistry();
        registry.Register<WizTurnOnAction>(WizTurnOnAction.ActionKind);
        registry.Register<TreadmillNudgeSpeedAction>(TreadmillNudgeSpeedAction.ActionKind);
        store = new ActionsConfigStore(dataFolderName, registry);
        directory = Path.GetDirectoryName(store.ConfigFilePath)!;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // The shapes of the per-integration files a pre-consolidation install used.
    private const string WizJson = """
        {
          "Devices": [{ "Id": "6f9619ff-8b86-d011-b42d-00cf4fc964ff", "Name": "Desk", "IpAddress": "192.168.1.20",
                        "DeviceType": 0, "SupportsColor": true, "SupportsColorTemperature": true, "SupportsDimming": true }],
          "Mappings": [{ "RewardTitle": "Lights on", "Actions": [{ "kind": "wiz.turnOn", "DeviceId": "6f9619ff-8b86-d011-b42d-00cf4fc964ff" }] }]
        }
        """;

    private const string TreadmillJson = """
        { "Mappings": [{ "RewardTitle": "Faster", "Actions": [{ "kind": "treadmill.nudgeSpeed", "DeltaKmh": 1.0 }] }] }
        """;

    [Fact]
    public void Load_FreshInstall_ReturnsEmptyConfigWithoutWritingIt()
    {
        var config = store.Load();

        Assert.Empty(config.Devices);
        Assert.Empty(config.Mappings);
        Assert.False(File.Exists(store.ConfigFilePath));
    }

    [Fact]
    public void Load_DeviceWithInvalidIp_IsDropped()
    {
        var good = new WizDevice(DeviceId, "Desk", "192.168.1.20", WizDeviceType.Light, true, true, true);
        var bad = new WizDevice(Guid.NewGuid(), "Typo", "lamp.local", WizDeviceType.Light, true, true, true);
        var alsoGood = good with { Id = Guid.NewGuid(), IpAddress = "10.0.0.5" };
        store.Save(new ActionsConfig { Devices = [good, bad, alsoGood] });

        var config = store.Load();

        Assert.Equal([good, alsoGood], config.Devices);
    }

    [Theory]
    [InlineData("192.168.1")]
    [InlineData("10.5")]
    [InlineData("3232235777")]
    public void Load_DeviceWithShorthandIp_IsDropped(string shorthand)
    {
        var good = new WizDevice(DeviceId, "Desk", "192.168.1.20", WizDeviceType.Light, true, true, true);
        var typo = good with { Id = Guid.NewGuid(), Name = "Typo", IpAddress = shorthand };
        store.Save(new ActionsConfig { Devices = [good, typo] });

        var config = store.Load();

        Assert.Equal([good], config.Devices);
    }

    [Fact]
    public void Load_AllDevicesValid_ReturnsThemUnchanged()
    {
        var device = new WizDevice(DeviceId, "Desk", "192.168.1.20", WizDeviceType.Light, true, true, true);
        store.Save(new ActionsConfig { Devices = [device] });

        Assert.Equal([device], store.Load().Devices);
    }

    [Fact]
    public void Load_IgnoresLeftoverPreConsolidationFiles()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "wiz-config.json"), WizJson);
        File.WriteAllText(Path.Combine(directory, "treadmill-config.json"), TreadmillJson);

        var config = store.Load();

        Assert.Empty(config.Devices);
        Assert.Empty(config.Mappings);
        Assert.False(File.Exists(store.ConfigFilePath));
    }
}
