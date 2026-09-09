using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BCUKCompanion.Core;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.UI;
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
        // keep-alive timer's thread-pool thread) — marshal onto the UI thread ourselves.
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

        var bottomPanel = new DockPanel { Margin = new Thickness(12, 8, 12, 12) };
        DockPanel.SetDock(SaveButton, Dock.Right);
        bottomPanel.Children.Add(SaveButton);
        bottomPanel.Children.Add(StatusText);

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

    protected override ActionsConfig BuildConfig() => new() { Devices = devices.ToList(), Mappings = Mappings.ToList() };

    protected override IEventActionContext BuildContext() =>
        new ActionsContext(new WizActionContext(wizClient, devices), new TreadmillActionContext(treadmillClient));

    protected override IEventAction? ShowAddActionDialog()
    {
        var dialog = new ActionEditDialog(devices, treadmillClient) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    protected override IEventAction? ShowEditActionDialog(IEventAction existing)
    {
        var dialog = new ActionEditDialog(devices, treadmillClient, existing) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

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
            var removedActionCount = StripMatchingActions(
                a => a is WizDeviceActionBase d && d.DeviceId == updated.Id && d.Validate(context).Count > 0);

            RefreshActionsList();
            StatusText.Text = removedActionCount > 0
                ? $"Updated device and removed {removedActionCount} action(s) no longer valid for it."
                : "Updated device.";
        }
    }

    private void OnRemoveDevice()
    {
        if (devicesList.SelectedItem is not WizDevice selected)
        {
            StatusText.Text = "Select a device to remove.";
            return;
        }

        devices.Remove(selected);

        var removedActionCount = StripMatchingActions(
            a => a is WizDeviceActionBase d && d.DeviceId == selected.Id);

        RefreshActionsList();
        StatusText.Text = removedActionCount > 0
            ? $"Removed device and {removedActionCount} action(s) that referenced it."
            : "Removed device.";
    }

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

    private int StripMatchingActions(Func<IEventAction, bool> shouldRemove)
    {
        var removedCount = 0;
        foreach (var mapping in Mappings.ToList())
        {
            var remaining = mapping.Actions.Where(a => !shouldRemove(a)).ToList();
            removedCount += mapping.Actions.Count - remaining.Count;
            if (remaining.Count != mapping.Actions.Count)
                ReplaceMappingActions(mapping, remaining);
        }
        return removedCount;
    }

    private void OnTreadmillStatusChanged(object? sender, string message) =>
        Dispatcher.Invoke(() => treadmillStatusText.Text = message);

    private void RefreshTreadmillSpeedText()
    {
        treadmillSpeedText.Text = $"Current: {treadmillClient.CurrentSpeedKmh:0.0} km/h   Target: {treadmillClient.TargetSpeedKmh:0.0} km/h";
        RefreshTreadmillConnectionButtons();
    }

    private void RefreshTreadmillConnectionButtons()
    {
        treadmillConnectButton.IsEnabled = !treadmillClient.IsConnected;
        treadmillDisconnectButton.IsEnabled = treadmillClient.IsConnected;
    }

    private async Task OnTreadmillConnectAsync()
    {
        treadmillConnectButton.IsEnabled = false;
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
            RefreshTreadmillConnectionButtons();
        }
    }

    private async Task OnTreadmillDisconnectAsync()
    {
        treadmillDisconnectButton.IsEnabled = false;
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
            RefreshTreadmillConnectionButtons();
        }
    }
}
