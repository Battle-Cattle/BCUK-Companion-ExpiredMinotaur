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

    private void WriteLegacyFile(string name, string json)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), json);
    }

    private const string LegacyWizJson = """
        {
          "Devices": [{ "Id": "6f9619ff-8b86-d011-b42d-00cf4fc964ff", "Name": "Desk", "IpAddress": "192.168.1.20",
                        "DeviceType": 0, "SupportsColor": true, "SupportsColorTemperature": true, "SupportsDimming": true }],
          "Mappings": [{ "RewardTitle": "Lights on", "Actions": [{ "kind": "wiz.turnOn", "DeviceId": "6f9619ff-8b86-d011-b42d-00cf4fc964ff" }] }]
        }
        """;

    private const string LegacyTreadmillJson = """
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
    public void Load_BothLegacyFiles_MergesThemAndPersistsTheResult()
    {
        WriteLegacyFile("wiz-config.json", LegacyWizJson);
        WriteLegacyFile("treadmill-config.json", LegacyTreadmillJson);

        var config = store.Load();

        var device = Assert.Single(config.Devices);
        Assert.Equal("Desk", device.Name);
        Assert.Equal(["Lights on", "Faster"], config.Mappings.Select(m => m.RewardTitle));
        Assert.IsType<WizTurnOnAction>(Assert.Single(config.Mappings[0].Actions));
        Assert.Equal(1.0, Assert.IsType<TreadmillNudgeSpeedAction>(Assert.Single(config.Mappings[1].Actions)).DeltaKmh);
        Assert.True(File.Exists(store.ConfigFilePath));
        // The legacy files are kept, inert, as a fallback.
        Assert.True(File.Exists(Path.Combine(directory, "wiz-config.json")));
    }

    [Fact]
    public void Load_OnlyTreadmillLegacyFile_MigratesIt()
    {
        WriteLegacyFile("treadmill-config.json", LegacyTreadmillJson);

        var config = store.Load();

        Assert.Empty(config.Devices);
        Assert.Equal("Faster", Assert.Single(config.Mappings).RewardTitle);
        Assert.True(File.Exists(store.ConfigFilePath));
    }

    [Fact]
    public void Load_AfterMigration_ReadsTheNewFileNotTheLegacyOnes()
    {
        WriteLegacyFile("treadmill-config.json", LegacyTreadmillJson);
        store.Load();
        WriteLegacyFile("treadmill-config.json", """{ "Mappings": [] }""");

        Assert.Single(store.Load().Mappings);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("""{ "Mappings": null }""")]
    public void Load_UnreadableLegacyFile_KeepsWhatItCanWithoutPersisting(string treadmillJson)
    {
        WriteLegacyFile("wiz-config.json", LegacyWizJson);
        WriteLegacyFile("treadmill-config.json", treadmillJson);

        var config = store.Load();

        Assert.Single(config.Devices);
        Assert.Equal("Lights on", Assert.Single(config.Mappings).RewardTitle);
        // Not saved, so the next launch tries the failed file again instead of losing it.
        Assert.False(File.Exists(store.ConfigFilePath));
    }

    [Fact]
    public void Load_LegacyWizFileWithNullDevices_IsAFailedMigration()
    {
        WriteLegacyFile("wiz-config.json", """{ "Devices": null, "Mappings": [] }""");

        var config = store.Load();

        Assert.Empty(config.Devices);
        Assert.False(File.Exists(store.ConfigFilePath));
    }
}
