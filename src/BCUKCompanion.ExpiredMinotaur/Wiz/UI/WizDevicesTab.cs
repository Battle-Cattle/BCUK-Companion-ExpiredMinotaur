using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.UI;

/// <summary>What the Devices tab needs from the window that hosts it.</summary>
/// <param name="ShowStatus">Shows a message in the window's status line.</param>
/// <param name="RemoveActions">Removes every mapped action the predicate matches and returns how many it removed.</param>
/// <param name="RefreshActions">Redraws the mappings' action lists, e.g. after a device was renamed.</param>
/// <param name="BuildContext">The context mapped actions validate against, over the current device list.</param>
public sealed record WizDevicesTabHost(
    Action<string> ShowStatus,
    Func<Func<IEventAction, bool>, int> RemoveActions,
    Action RefreshActions,
    Func<IEventActionContext> BuildContext);

/// <summary>
/// The Actions window's "Devices" tab: the WiZ device list and its Add/Edit/Remove/Discover
/// buttons. Editing or removing a device also drops the mapped actions it made invalid.
/// </summary>
public sealed class WizDevicesTab : DockPanel
{
    private readonly ObservableCollection<WizDevice> devices;
    private readonly WizClient wizClient;
    private readonly WizDevicesTabHost host;

    private readonly ListBox devicesList = new() { Margin = new Thickness(0, 0, 0, 8), MinHeight = 200 };

    /// <summary>Builds the tab over <paramref name="devices"/>, which it edits in place.</summary>
    /// <param name="devices">The window's device list; the tab adds, replaces and removes entries in it.</param>
    /// <param name="wizClient">Used by LAN discovery.</param>
    /// <param name="host">The window's status line and mapping hooks.</param>
    public WizDevicesTab(ObservableCollection<WizDevice> devices, WizClient wizClient, WizDevicesTabHost host)
    {
        this.devices = devices;
        this.wizClient = wizClient;
        this.host = host;
        Margin = new Thickness(0, 12, 0, 12);

        devicesList.ItemsSource = devices;

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                CreateButton("Add", 80, OnAddDevice),
                CreateButton("Edit", 80, OnEditDevice),
                CreateButton("Remove", 80, OnRemoveDevice),
                CreateButton("Discover...", 90, OnDiscoverDevices, isLast: true),
            },
        };

        SetDock(buttonPanel, Dock.Bottom);
        Children.Add(buttonPanel);
        Children.Add(devicesList);
    }

    /// <summary>Creates one of the tab's buttons.</summary>
    /// <param name="content">The button's caption.</param>
    /// <param name="width">The button's width in device-independent pixels.</param>
    /// <param name="onClick">Runs when the button is clicked.</param>
    /// <param name="isLast">Whether it's the last button in the row, which gets no right margin.</param>
    /// <returns>The button.</returns>
    private static Button CreateButton(string content, double width, Action onClick, bool isLast = false)
    {
        var button = new Button { Content = content, Width = width, Margin = new Thickness(0, 0, isLast ? 0 : 8, 0) };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Adds a device from the edit dialog, rejecting a duplicate IP address.</summary>
    private void OnAddDevice()
    {
        var dialog = new WizDeviceEditDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.Result is not { } device)
        {
            return;
        }

        if (WizDeviceList.IsDuplicateIp(devices, device.IpAddress))
        {
            host.ShowStatus(WizDeviceList.DuplicateIpMessage);
            return;
        }

        devices.Add(device);
    }

    /// <summary>
    /// Replaces the selected device with the edited one and removes any of its actions the edit
    /// made invalid (e.g. SetColor on a device that no longer supports color).
    /// </summary>
    private void OnEditDevice()
    {
        if (devicesList.SelectedItem is not WizDevice selected)
        {
            host.ShowStatus("Select a device to edit.");
            return;
        }

        var dialog = new WizDeviceEditDialog(selected) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.Result is not { } updated)
        {
            return;
        }

        if (WizDeviceList.IsDuplicateIp(devices, updated.IpAddress, exceptId: updated.Id))
        {
            host.ShowStatus(WizDeviceList.DuplicateIpMessage);
            return;
        }

        devices[devices.IndexOf(selected)] = updated;

        var context = host.BuildContext();
        var removedActionCount = host.RemoveActions(
            a => a is WizDeviceActionBase d && d.DeviceId == updated.Id && d.Validate(context).Count > 0);

        // Even with nothing removed: the actions' descriptions include the device's name.
        host.RefreshActions();
        host.ShowStatus(WizDeviceList.UpdatedMessage(removedActionCount));
    }

    /// <summary>Removes the selected device and every action that targeted it.</summary>
    private void OnRemoveDevice()
    {
        if (devicesList.SelectedItem is not WizDevice selected)
        {
            host.ShowStatus("Select a device to remove.");
            return;
        }

        devices.Remove(selected);

        var removedActionCount = host.RemoveActions(a => a is WizDeviceActionBase d && d.DeviceId == selected.Id);
        host.ShowStatus(WizDeviceList.RemovedMessage(removedActionCount));
    }

    /// <summary>Adds the devices picked in the LAN discovery dialog.</summary>
    private void OnDiscoverDevices()
    {
        var dialog = new WizDiscoverDevicesDialog(wizClient, devices.Select(d => d.IpAddress).ToList()) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var device in dialog.AddedDevices)
        {
            devices.Add(device);
        }
    }
}
