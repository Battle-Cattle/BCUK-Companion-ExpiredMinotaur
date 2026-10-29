using System.Reflection;
using System.Windows.Controls;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Tests.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.UI;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz.UI;
using Xunit;

namespace BCUKCompanion.ExpiredMinotaur.Tests;

public sealed class ActionsSettingsWindowTests
{
    private static readonly WizDevice Desk = new(Guid.NewGuid(), "Desk", "192.168.1.20", WizDeviceType.Light, true, true, true);

    // Takes the window as an object, so this class's own signatures name no WPF type (see WindowsOnly).
    private static object? CallProtected(object window, string name)
    {
        var type = window.GetType();
        var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(type.Name, name);
        return method.Invoke(window, null);
    }

    [SkippableFact]
    public void ShowsTheTreadmillPanelAndBothTabs()
    {
        WindowsOnly.Skip();
        using var temp = new TempActionsStore();
        var treadmill = new FakeTreadmill();
        WpfTest.Run(() =>
        {
            var window = new ActionsSettingsWindow(temp.Store, new WizClient(), treadmill.Client);

            Assert.Equal("Actions", window.Title);
            Assert.Single(WpfTest.All<TreadmillPanel>(window));
            Assert.Equal(["Devices", "Event Mappings"], WpfTest.All<TabItem>(window).Select(t => t.Header));
        });
        treadmill.Client.Dispose();
    }

    [SkippableFact]
    public void Save_WritesTheDevicesAndMappings()
    {
        WindowsOnly.Skip();
        using var temp = new TempActionsStore();
        temp.Store.Save(new ActionsConfig
        {
            Devices = [Desk],
            Mappings = [new EventActionMapping("Hydrate", [new WizTurnOnAction { DeviceId = Desk.Id }])],
        });
        var treadmill = new FakeTreadmill();
        WpfTest.Run(async () =>
        {
            var window = new ActionsSettingsWindow(temp.Store, new WizClient(), treadmill.Client);
            var combos = WpfTest.All<ComboBox>(window).Where(c => c.IsEditable).ToList();
            Assert.Single(combos).Text = "Lights";
            WpfTest.Click(window, "Add Mapping");

            WpfTest.Click(window, "Save");
            await WpfTest.WaitUntil(() => WpfTest.Texts(window).Any(t => t.StartsWith("Saved to")));
        });

        var saved = temp.Store.Load();
        Assert.Equal([Desk], saved.Devices);
        Assert.Equal(["Hydrate", "Lights"], saved.Mappings.Select(m => m.RewardTitle));
        var action = Assert.IsType<WizTurnOnAction>(Assert.Single(saved.Mappings[0].Actions));
        Assert.Equal(Desk.Id, action.DeviceId);
        Assert.Empty(saved.Mappings[1].Actions);
        treadmill.Client.Dispose();
    }

    [SkippableFact]
    public void TheDevicesTab_ReportsInTheWindowsStatusLine()
    {
        WindowsOnly.Skip();
        using var temp = new TempActionsStore();
        var treadmill = new FakeTreadmill();
        WpfTest.Run(() =>
        {
            var window = new ActionsSettingsWindow(temp.Store, new WizClient(), treadmill.Client);

            WpfTest.Click(WpfTest.All<WizDevicesTab>(window).Single(), "Remove");

            Assert.Contains("Select a device to remove.", WpfTest.Texts(window));
        });
        treadmill.Client.Dispose();
    }

    [SkippableFact]
    public void OffersTheWizAndTreadmillEditors_OverBothContexts()
    {
        WindowsOnly.Skip();
        using var temp = new TempActionsStore();
        temp.Store.Save(new ActionsConfig { Devices = [Desk] });
        var treadmill = new FakeTreadmill();
        WpfTest.Run(() =>
        {
            var window = new ActionsSettingsWindow(temp.Store, new WizClient(), treadmill.Client);

            var editors = Assert.IsAssignableFrom<IEnumerable<BCUKCompanion.TrayApp.Actions.EventActionEditor>>(
                CallProtected(window, "CreateActionEditors")).ToList();
            Assert.Collection(editors, e => Assert.IsType<WizActionEditor>(e), e => Assert.IsType<TreadmillActionEditor>(e));

            var context = Assert.IsAssignableFrom<IEventActionContext>(CallProtected(window, "BuildContext"));
            Assert.Equal(Desk, Assert.IsType<WizActionContext>(context.GetService<WizActionContext>()).ResolveDevice(Desk.Id));
            Assert.Same(treadmill.Client, Assert.IsType<TreadmillActionContext>(context.GetService<TreadmillActionContext>()).Client);
            Assert.Equal("Desk: TurnOn", new WizTurnOnAction { DeviceId = Desk.Id }.Describe(context));
        });
        treadmill.Client.Dispose();
    }

    [SkippableFact]
    public void Closing_StopsThePanelFollowingTheTreadmill()
    {
        WindowsOnly.Skip();
        using var temp = new TempActionsStore();
        var treadmill = new FakeTreadmill();
        WpfTest.Run(async () =>
        {
            var window = new ActionsSettingsWindow(temp.Store, new WizClient(), treadmill.Client) { ShowActivated = false };
            window.Show();
            window.Close();

            await treadmill.Client.ConnectAsync();
            await Task.Delay(100);

            Assert.DoesNotContain("Connected.", WpfTest.Texts(window));
        });
        treadmill.Client.Dispose();
    }
}
