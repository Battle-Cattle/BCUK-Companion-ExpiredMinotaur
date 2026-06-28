using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;
using SelectionMode = System.Windows.Controls.SelectionMode;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.UI;

public sealed class WizDiscoverDevicesDialog : Window
{
    private sealed class DiscoveredItem(WizDiscoveredDevice device)
    {
        public WizDiscoveredDevice Device { get; } = device;

        public override string ToString() =>
            $"{Device.ModuleName} - {Device.IpAddress} (inferred: {InferDeviceType(Device.ModuleName)})";
    }

    private readonly WizClient client;
    private readonly ListBox resultsList = new() { Margin = new Thickness(0, 0, 0, 8), MinHeight = 160 };
    private readonly TextBlock statusText = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly List<DiscoveredItem> items = [];

    public IReadOnlyList<WizDevice> AddedDevices { get; private set; } = [];

    public WizDiscoverDevicesDialog(WizClient client, IReadOnlyCollection<string> knownIpAddresses)
    {
        this.client = client;

        Title = "Discover Wiz Devices";
        Width = 420;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        resultsList.SelectionMode = SelectionMode.Extended;

        var rescanButton = new Button { Content = "Rescan", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        rescanButton.Click += async (_, _) => await RunDiscoveryAsync(knownIpAddresses).ConfigureAwait(true);

        var addButton = new Button { Content = "Add Selected", Width = 110, Margin = new Thickness(0, 0, 8, 0) };
        addButton.Click += (_, _) => OnAddSelected();

        var closeButton = new Button { Content = "Close", Width = 90 };
        closeButton.Click += (_, _) => { DialogResult = false; };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { rescanButton, addButton, closeButton },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                new TextBlock { Text = "Select discovered devices (ctrl/shift-click for multiple), then click Add Selected.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) },
                statusText,
                resultsList,
                buttonPanel,
            },
        };

        Loaded += async (_, _) => await RunDiscoveryAsync(knownIpAddresses).ConfigureAwait(true);
    }

    private static WizDeviceType InferDeviceType(string moduleName)
    {
        return moduleName.Contains("SOCKET", StringComparison.OrdinalIgnoreCase) ? WizDeviceType.Plug : WizDeviceType.Light;
    }

    private static (bool Color, bool ColorTemp, bool Dimming) InferCapabilities(string moduleName)
    {
        if (moduleName.Contains("SOCKET", StringComparison.OrdinalIgnoreCase))
        {
            return (false, false, false);
        }

        var supportsColor = moduleName.Contains("RGB", StringComparison.OrdinalIgnoreCase);
        var supportsColorTemp = supportsColor || moduleName.Contains("TW", StringComparison.OrdinalIgnoreCase);
        return (supportsColor, supportsColorTemp, true);
    }

    private async Task RunDiscoveryAsync(IReadOnlyCollection<string> knownIpAddresses)
    {
        resultsList.ItemsSource = null;
        items.Clear();
        statusText.Text = "Scanning local network...";

        var found = new List<DiscoveredItem>();
        await foreach (var device in client.DiscoverAsync())
        {
            if (!knownIpAddresses.Contains(device.IpAddress))
            {
                found.Add(new DiscoveredItem(device));
            }
        }

        items.AddRange(found);
        resultsList.ItemsSource = items;
        statusText.Text = items.Count == 0 ? "No new devices found." : $"Found {items.Count} new device(s).";
    }

    private void OnAddSelected()
    {
        var selected = resultsList.SelectedItems.Cast<DiscoveredItem>().ToList();
        if (selected.Count == 0)
        {
            statusText.Text = "Select at least one device first.";
            return;
        }

        var added = new List<WizDevice>();
        foreach (var item in selected)
        {
            var (color, colorTemp, dimming) = InferCapabilities(item.Device.ModuleName);
            added.Add(new WizDevice(
                Guid.NewGuid(),
                item.Device.ModuleName,
                item.Device.IpAddress,
                InferDeviceType(item.Device.ModuleName),
                color,
                colorTemp,
                dimming));
        }

        AddedDevices = added;
        DialogResult = true;
    }
}
