using BCUKCompanion.Core;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.Core.Tokens;
using BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Tests.Wiz;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using BCUKCompanion.TrayApp;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests;

public sealed class ProgramTests
{
    [Fact]
    public void CreateRegistry_RegistersEveryActionKind()
    {
        var registry = Program.CreateRegistry();

        Assert.Equal(
            new Dictionary<string, Type>
            {
                [WizTurnOnAction.ActionKind] = typeof(WizTurnOnAction),
                [WizTurnOffAction.ActionKind] = typeof(WizTurnOffAction),
                [WizToggleAction.ActionKind] = typeof(WizToggleAction),
                [WizSetBrightnessAction.ActionKind] = typeof(WizSetBrightnessAction),
                [WizSetColorAction.ActionKind] = typeof(WizSetColorAction),
                [WizSetColorTemperatureAction.ActionKind] = typeof(WizSetColorTemperatureAction),
                [TreadmillNudgeSpeedAction.ActionKind] = typeof(TreadmillNudgeSpeedAction),
                [DelayAction.ActionKind] = typeof(DelayAction),
            }.OrderBy(p => p.Key),
            registry.RegisteredKinds.OrderBy(p => p.Key));
    }

    [SkippableFact]
    public void CreateOptions_NamesThisAppsFolderAndRepo()
    {
        WindowsOnly.Skip();
        using var temp = new TempActionsStore();
        var treadmill = new FakeTreadmill();
        WpfTest.Run(() =>
        {
            var options = Program.CreateOptions(temp.Store, new WizClient(), treadmill.Client);

            Assert.Equal(Program.DataFolderName, options.DataFolderName);
            Assert.Equal(("Battle-Cattle", "BCUK-Companion-ExpiredMinotaur"), (options.Updates!.RepoOwner, options.Updates.RepoName));
            Assert.Equal("Actions...", Assert.Single(options.AdditionalMenuItems!).Text);
        });
        treadmill.Client.Dispose();
    }

    [SkippableFact]
    public void CreateOptions_DispatchesEventsToTheSavedMappings()
    {
        WindowsOnly.Skip();
        using var temp = new TempActionsStore();
        using var fake = new FakeWizDevice((_, _) => """{"result":{"success":true}}""");
        var treadmill = new FakeTreadmill();
        var device = new WizDevice(Guid.NewGuid(), "Desk", fake.IpAddress, WizDeviceType.Light, true, true, true);
        temp.Store.Save(new ActionsConfig
        {
            Devices = [device],
            Mappings = [new EventActionMapping("Lights", [new WizTurnOnAction { DeviceId = device.Id }])],
        });
        EventDispatchResult? result = null;

        WpfTest.Run(async () =>
        {
            var options = Program.CreateOptions(temp.Store, fake.CreateClient(), treadmill.Client);
            result = await options.CreateEventActionDispatcher!().DispatchAsync("Lights");
        });

        Assert.True(result!.AllSucceeded);
        // WizClient resends until a reply arrives, so a slow first reply can mean more than one request.
        Assert.All(fake.Requests, r => Assert.Equal("""{"method":"setPilot","params":{"state":true}}""", r));
        Assert.NotEmpty(fake.Requests);
        treadmill.Client.Dispose();
    }

    [SkippableFact]
    public void CreateOptions_ActionsMenuItemOpensTheActionsWindow()
    {
        WindowsOnly.Skip();
        using var temp = new TempActionsStore();
        var treadmill = new FakeTreadmill();
        WpfTest.Run(() =>
        {
            var options = Program.CreateOptions(temp.Store, new WizClient(), treadmill.Client);
            options.OnClientReady!(new CompanionClient(new Uri("https://bot.example"), new MemoryTokenStore()));

            var item = Assert.Single(options.AdditionalMenuItems!);
            item.OnClick();
            item.OnClick(); // activates the open window rather than opening another
        });
        treadmill.Client.Dispose();
    }

    [SkippableFact]
    public void Main_RunsTheTrayAppWithThisAppsOptions()
    {
        WindowsOnly.Skip();
        WpfTest.Run(() =>
        {
            var previous = Program.RunTrayApp;
            CompanionTrayAppOptions? ran = null;
            Program.RunTrayApp = options => ran = options;
            try
            {
                Program.Main();
            }
            finally
            {
                Program.RunTrayApp = previous;
            }

            Assert.Equal(Program.DataFolderName, ran!.DataFolderName);
        });
    }

    private sealed class MemoryTokenStore : ITokenStore
    {
        private string? token;

        public string? Load() => token;

        public void Save(string newToken) => token = newToken;

        public void Clear() => token = null;
    }
}
