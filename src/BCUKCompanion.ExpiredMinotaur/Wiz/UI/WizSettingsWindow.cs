using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using BCUKCompanion.Core;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;
using TabControl = System.Windows.Controls.TabControl;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.UI;

public sealed class WizSettingsWindow : EventActionMappingsWindow<WizConfig>
{
    private readonly WizClient client;
    private readonly ObservableCollection<WizDevice> devices;

    private readonly ListBox devicesList = new() { Margin = new Thickness(0, 0, 0, 8), MinHeight = 200 };

    public WizSettingsWindow(WizConfigStore configStore, WizClient client, Func<CompanionClient?>? getCompanionClient = null)
        : base(configStore, configStore.Load(), getCompanionClient)
    {
        this.client = client;
        devices = new ObservableCollection<WizDevice>(InitialConfig.Devices);

        Title = "Wiz Devices";
        Width = 640;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

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
        DockPanel.SetDock(bottomPanel, Dock.Bottom);
        root.Children.Add(bottomPanel);
        root.Children.Add(tabs);

        Content = root;

        RefreshRewardTitleSuggestions();
    }

    protected override WizConfig BuildConfig() => new() { Devices = devices.ToList(), Mappings = Mappings.ToList() };

    protected override IEventActionContext BuildContext() => new WizActionContext(client, devices);

    protected override IEventAction? ShowAddActionDialog()
    {
        if (devices.Count == 0)
        {
            StatusText.Text = "Add a device first.";
            return null;
        }

        var dialog = new WizActionEditDialog(devices) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    protected override IEventAction? ShowEditActionDialog(IEventAction existing)
    {
        var dialog = new WizActionEditDialog(devices, existing) { Owner = this };
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
        var dialog = new WizDiscoverDevicesDialog(client, devices.Select(d => d.IpAddress).ToList()) { Owner = this };
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
}
