using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;
using SelectionMode = System.Windows.Controls.SelectionMode;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.UI;

/// <summary>
/// Scans the LAN for WiZ devices not already in the list and lets the user add the ones they pick,
/// with type and capabilities inferred from each module name.
/// </summary>
public sealed class WizDiscoverDevicesDialog : Window
{
    /// <summary>One row of the results list: a discovered device, shown with its inferred type.</summary>
    private sealed class DiscoveredItem(WizDiscoveredDevice device)
    {
        /// <summary>The device that answered discovery.</summary>
        public WizDiscoveredDevice Device { get; } = device;

        /// <summary>The row's text: module name, address and inferred type.</summary>
        public override string ToString() =>
            $"{Device.ModuleName} - {Device.IpAddress} (inferred: {WizModuleInference.InferDeviceType(Device.ModuleName)})";
    }

    private readonly WizClient client;
    private readonly ListBox resultsList = new() { Margin = new Thickness(0, 0, 0, 8), MinHeight = 160 };
    private readonly TextBlock statusText = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly List<DiscoveredItem> items = [];
    private bool isScanning;

    /// <summary>The devices the user chose to add, once the dialog closes with OK.</summary>
    public IReadOnlyList<WizDevice> AddedDevices { get; private set; } = [];

    /// <summary>Creates the dialog; discovery starts as soon as it loads.</summary>
    /// <param name="client">The client to discover devices with.</param>
    /// <param name="knownIpAddresses">Addresses already in the device list, left out of the results.</param>
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

    /// <summary>Runs one discovery scan and lists the new devices it finds (ignored if a scan is already running).</summary>
    private async Task RunDiscoveryAsync(IReadOnlyCollection<string> knownIpAddresses)
    {
        if (isScanning)
        {
            return;
        }

        isScanning = true;
        resultsList.ItemsSource = null;
        items.Clear();
        statusText.Text = "Scanning local network...";

        try
        {
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
        catch (Exception ex)
        {
            statusText.Text = $"Discovery failed: {ex.Message}";
        }
        finally
        {
            isScanning = false;
        }
    }

    /// <summary>Closes the dialog with the selected devices in <see cref="AddedDevices"/>, or asks for a selection.</summary>
    private void OnAddSelected()
    {
        var selected = resultsList.SelectedItems.Cast<DiscoveredItem>().ToList();
        if (selected.Count == 0)
        {
            statusText.Text = "Select at least one device first.";
            return;
        }

        AddedDevices = selected.Select(item => WizModuleInference.ToDevice(item.Device, Guid.NewGuid())).ToList();
        DialogResult = true;
    }
}
