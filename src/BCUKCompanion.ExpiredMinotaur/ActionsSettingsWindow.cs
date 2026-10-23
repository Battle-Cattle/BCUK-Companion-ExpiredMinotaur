using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using BCUKCompanion.Core;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.UI;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.UI;
using BCUKCompanion.TrayApp.Actions;
using TabControl = System.Windows.Controls.TabControl;

namespace BCUKCompanion.ExpiredMinotaur;

/// <summary>
/// The single settings window for every integration: a "Devices" tab for Wiz devices
/// (<see cref="WizDevicesTab"/>), a treadmill connection panel (<see cref="TreadmillPanel"/>),
/// and one "Event Mappings" tab whose actions can mix Wiz and Treadmill (and Delay) kinds in the
/// same mapping — replacing the old separate WizSettingsWindow/TreadmillSettingsWindow, each of
/// which only let a mapping use its own integration's action kinds.
/// </summary>
public sealed class ActionsSettingsWindow : EventActionMappingsWindow<ActionsConfig>
{
    private readonly WizClient wizClient;
    private readonly TreadmillClient treadmillClient;
    private readonly ObservableCollection<WizDevice> devices;

    /// <summary>Opens the window on the store's saved devices and mappings.</summary>
    /// <param name="configStore">Loads the initial config and saves the edited one.</param>
    /// <param name="wizClient">Used by device discovery and the Test button.</param>
    /// <param name="treadmillClient">The shared treadmill connection the top panel controls.</param>
    /// <param name="getCompanionClient">The shell's current client, for live reward/command names.</param>
    public ActionsSettingsWindow(
        ActionsConfigStore configStore, WizClient wizClient, TreadmillClient treadmillClient, Func<CompanionClient?>? getCompanionClient = null)
        : base(configStore, configStore.Load(), getCompanionClient)
    {
        this.wizClient = wizClient;
        this.treadmillClient = treadmillClient;
        devices = new ObservableCollection<WizDevice>(InitialConfig.Devices);

        Title = "Actions";
        Width = 720;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var treadmillPanel = new TreadmillPanel(treadmillClient);
        var devicesTab = new WizDevicesTab(
            devices,
            wizClient,
            new WizDevicesTabHost(message => StatusText.Text = message, RemoveActions, RefreshActionsList, BuildContext));

        var tabs = new TabControl
        {
            Margin = new Thickness(12, 12, 12, 0),
        };
        tabs.Items.Add(new TabItem { Header = "Devices", Content = devicesTab });
        tabs.Items.Add(new TabItem { Header = "Event Mappings", Content = BuildMappingsTab() });

        var bottomPanel = BuildFooter();

        var root = new DockPanel();
        DockPanel.SetDock(treadmillPanel, Dock.Top);
        DockPanel.SetDock(bottomPanel, Dock.Bottom);
        root.Children.Add(treadmillPanel);
        root.Children.Add(bottomPanel);
        root.Children.Add(tabs);

        Content = root;

        RefreshRewardTitleSuggestions();

        Closed += (_, _) => treadmillPanel.Detach();
    }

    /// <summary>Snapshots the window's current devices and mappings for saving or testing.</summary>
    /// <returns>A new config holding copies of the device list and the mappings.</returns>
    protected override ActionsConfig BuildConfig() => new() { Devices = devices.ToList(), Mappings = Mappings.ToList() };

    /// <summary>The Wiz and treadmill contexts the mapped actions validate and run against.</summary>
    /// <returns>A composite of a Wiz context over the window's device list and a treadmill context.</returns>
    protected override IEventActionContext BuildContext() =>
        new CompositeEventActionContext(new WizActionContext(wizClient, devices), new TreadmillActionContext(treadmillClient));

    /// <summary>
    /// The categories the shell's Add/Edit Action dialog offers, before its built-in Delay.
    /// </summary>
    /// <returns>The Wiz editor (over the window's device list) and the treadmill editor.</returns>
    protected override IEnumerable<EventActionEditor> CreateActionEditors() =>
        [new WizActionEditor(devices), new TreadmillActionEditor()];
}
