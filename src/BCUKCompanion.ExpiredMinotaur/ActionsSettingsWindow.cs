using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BCUKCompanion.Core;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.UI;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz.UI;
using BCUKCompanion.TrayApp.Actions;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;
using TabControl = System.Windows.Controls.TabControl;

namespace BCUKCompanion.ExpiredMinotaur;

/// <summary>
/// The single settings window for every integration: a "Devices" tab for Wiz devices, a
/// Treadmill connection panel, and one "Event Mappings" tab whose actions can mix Wiz and
/// Treadmill (and Delay) kinds in the same mapping — replacing the old separate
/// WizSettingsWindow/TreadmillSettingsWindow, each of which only let a mapping use its own
/// integration's action kinds.
/// </summary>
public sealed class ActionsSettingsWindow : EventActionMappingsWindow<ActionsConfig>
{
    private readonly WizClient wizClient;
    private readonly TreadmillClient treadmillClient;
    private readonly ObservableCollection<WizDevice> devices;

    private readonly ListBox devicesList = new() { Margin = new Thickness(0, 0, 0, 8), MinHeight = 200 };

    private readonly TextBlock treadmillStatusText = new() { Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock treadmillSpeedText = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly Button treadmillConnectButton = new() { Content = "Connect Treadmill", Width = 130, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button treadmillDisconnectButton = new() { Content = "Disconnect Treadmill", Width = 130 };

    private readonly DispatcherTimer speedTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    // Tracks a Connect/Disconnect in flight so the 1-second speedTimer tick doesn't re-enable
    // the button mid-operation (TreadmillClient.IsConnected alone doesn't reflect "connecting"),
    // which would let a second click queue another request behind TreadmillClient's write lock.
    private bool treadmillConnectionOperationInProgress;

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

        // StatusChanged fires on whatever thread triggered it (a BLE callback thread, or the
        // follow timer's thread-pool thread) — marshal onto the UI thread ourselves.
        treadmillClient.StatusChanged += OnTreadmillStatusChanged;

        treadmillConnectButton.Click += async (_, _) => await OnTreadmillConnectAsync().ConfigureAwait(true);
        treadmillDisconnectButton.Click += async (_, _) => await OnTreadmillDisconnectAsync().ConfigureAwait(true);

        speedTimer.Tick += (_, _) => RefreshTreadmillSpeedText();
        RefreshTreadmillSpeedText();
        RefreshTreadmillConnectionButtons();
        speedTimer.Start();

        var treadmillButtonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { treadmillConnectButton, treadmillDisconnectButton },
        };

        var topPanel = new StackPanel
        {
            Margin = new Thickness(12, 12, 12, 0),
            Children = { treadmillButtonsPanel, treadmillStatusText, treadmillSpeedText },
        };

        var tabs = new TabControl
        {
            Margin = new Thickness(12, 12, 12, 0),
        };
        tabs.Items.Add(new TabItem { Header = "Devices", Content = BuildDevicesTab() });
        tabs.Items.Add(new TabItem { Header = "Event Mappings", Content = BuildMappingsTab() });

        var bottomPanel = BuildFooter();

        var root = new DockPanel();
        DockPanel.SetDock(topPanel, Dock.Top);
        DockPanel.SetDock(bottomPanel, Dock.Bottom);
        root.Children.Add(topPanel);
        root.Children.Add(bottomPanel);
        root.Children.Add(tabs);

        Content = root;

        RefreshRewardTitleSuggestions();

        Closed += (_, _) =>
        {
            speedTimer.Stop();
            treadmillClient.StatusChanged -= OnTreadmillStatusChanged;
        };
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

    /// <summary>Builds the "Devices" tab: the Wiz device list and its Add/Edit/Remove/Discover buttons.</summary>
    /// <returns>The tab's content.</returns>
    private UIElement BuildDevicesTab()
    {
        devicesList.ItemsSource = devices;

        var addButton = new Button { Content = "Add", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        addButton.Click += (_, _) => OnAddDevice();

        var editButton = new Button { Content = "Edit", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        editButton.Click += (_, _) => OnEditDevice();

        var removeButton = new Button { Content = "Remove", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        removeButton.Click += (_, _) => OnRemoveDevice();

        var discoverButton = new Button { Content = "Discover...", Width = 90 };
        discoverButton.Click += (_, _) => OnDiscoverDevices();

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 0),
            Children = { addButton, editButton, removeButton, discoverButton },
        };

        var devicesPanel = new DockPanel
        {
            Margin = new Thickness(0, 12, 0, 12),
            Children = { buttonPanel, devicesList },
        };
        DockPanel.SetDock(buttonPanel, Dock.Bottom);
        return devicesPanel;
    }

    /// <summary>Adds a device from the edit dialog, rejecting a duplicate IP address.</summary>
    private void OnAddDevice()
    {
        var dialog = new WizDeviceEditDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } device)
        {
            if (devices.Any(d => string.Equals(d.IpAddress, device.IpAddress, StringComparison.OrdinalIgnoreCase)))
            {
                StatusText.Text = "A device with that IP address already exists.";
                return;
            }

            devices.Add(device);
        }
    }

    /// <summary>
    /// Replaces the selected device with the edited one and removes any of its actions the edit
    /// made invalid (e.g. SetColor on a device that no longer supports color).
    /// </summary>
    private void OnEditDevice()
    {
        if (devicesList.SelectedItem is not WizDevice selected)
        {
            StatusText.Text = "Select a device to edit.";
            return;
        }

        var index = devices.IndexOf(selected);
        var dialog = new WizDeviceEditDialog(selected) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } updated)
        {
            if (devices.Any(d => d.Id != updated.Id
                && string.Equals(d.IpAddress, updated.IpAddress, StringComparison.OrdinalIgnoreCase)))
            {
                StatusText.Text = "A device with that IP address already exists.";
                return;
            }

            devices[index] = updated;

            var context = BuildContext();
            var removedActionCount = RemoveActions(
                a => a is WizDeviceActionBase d && d.DeviceId == updated.Id && d.Validate(context).Count > 0);

            // Even with nothing removed: the actions' descriptions include the device's name.
            RefreshActionsList();
            StatusText.Text = removedActionCount > 0
                ? $"Updated device and removed {removedActionCount} action(s) no longer valid for it."
                : "Updated device.";
        }
    }

    /// <summary>Removes the selected device and every action that targeted it.</summary>
    private void OnRemoveDevice()
    {
        if (devicesList.SelectedItem is not WizDevice selected)
        {
            StatusText.Text = "Select a device to remove.";
            return;
        }

        devices.Remove(selected);

        var removedActionCount = RemoveActions(
            a => a is WizDeviceActionBase d && d.DeviceId == selected.Id);

        StatusText.Text = removedActionCount > 0
            ? $"Removed device and {removedActionCount} action(s) that referenced it."
            : "Removed device.";
    }

    /// <summary>Adds the devices picked in the LAN discovery dialog.</summary>
    private void OnDiscoverDevices()
    {
        var dialog = new WizDiscoverDevicesDialog(wizClient, devices.Select(d => d.IpAddress).ToList()) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            foreach (var device in dialog.AddedDevices)
            {
                devices.Add(device);
            }
        }
    }

    /// <summary>Shows a treadmill status message, marshalled onto the UI thread.</summary>
    /// <remarks>
    /// InvokeAsync, not Invoke: TreadmillClient raises StatusChanged while holding its write lock,
    /// and blocking that thread on the UI thread would deadlock if the UI thread ever waited on
    /// the same lock.
    /// </remarks>
    /// <param name="sender">The <see cref="TreadmillClient"/> that raised the event.</param>
    /// <param name="message">The status text to show.</param>
    private void OnTreadmillStatusChanged(object? sender, string message) =>
        _ = Dispatcher.InvokeAsync(() => treadmillStatusText.Text = message);

    /// <summary>Updates the current/target speed line (every second) and the connection buttons.</summary>
    private void RefreshTreadmillSpeedText()
    {
        treadmillSpeedText.Text = $"Current: {treadmillClient.CurrentSpeedKmh:0.0} km/h   Target: {treadmillClient.TargetSpeedKmh:0.0} km/h";
        RefreshTreadmillConnectionButtons();
    }

    /// <summary>
    /// Enables Connect or Disconnect to match the connection, or neither while one is in flight.
    /// </summary>
    private void RefreshTreadmillConnectionButtons()
    {
        if (treadmillConnectionOperationInProgress)
        {
            treadmillConnectButton.IsEnabled = false;
            treadmillDisconnectButton.IsEnabled = false;
            return;
        }

        treadmillConnectButton.IsEnabled = !treadmillClient.IsConnected;
        treadmillDisconnectButton.IsEnabled = treadmillClient.IsConnected;
    }

    /// <summary>Connects the treadmill, showing any failure in the status line.</summary>
    /// <returns>A task that completes once the attempt has finished, successful or not.</returns>
    private async Task OnTreadmillConnectAsync()
    {
        treadmillConnectionOperationInProgress = true;
        RefreshTreadmillConnectionButtons();
        try
        {
            await treadmillClient.ConnectAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            treadmillStatusText.Text = $"Connect failed: {ex.Message}";
        }
        finally
        {
            treadmillConnectionOperationInProgress = false;
            RefreshTreadmillConnectionButtons();
        }
    }

    /// <summary>Disconnects the treadmill, showing any failure in the status line.</summary>
    /// <returns>A task that completes once the attempt has finished, successful or not.</returns>
    private async Task OnTreadmillDisconnectAsync()
    {
        treadmillConnectionOperationInProgress = true;
        RefreshTreadmillConnectionButtons();
        try
        {
            await treadmillClient.DisconnectAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            treadmillStatusText.Text = $"Disconnect failed: {ex.Message}";
        }
        finally
        {
            treadmillConnectionOperationInProgress = false;
            RefreshTreadmillConnectionButtons();
        }
    }
}
