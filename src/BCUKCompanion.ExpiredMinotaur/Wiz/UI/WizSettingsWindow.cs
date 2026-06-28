using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using BCUKCompanion.Core.Models;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListBox = System.Windows.Controls.ListBox;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using TabControl = System.Windows.Controls.TabControl;

namespace BCUKCompanion.ExpiredMinotaur.Wiz.UI;

public sealed class WizSettingsWindow : Window
{
    private const string RedemptionEventName = "redemption.received";
    private const string RewardTitleMetadataKey = "rewardTitle";

    private readonly WizConfigStore configStore;
    private readonly WizClient client;
    private readonly EventActionDispatcher dispatcher;

    private readonly ObservableCollection<WizDevice> devices;
    private readonly ObservableCollection<EventActionMapping> mappings;

    private readonly ListBox devicesList = new() { Margin = new Thickness(0, 0, 0, 8), MinHeight = 200 };
    private readonly ListBox mappingsList = new() { Margin = new Thickness(0, 0, 0, 8), MinHeight = 200 };
    private readonly ListBox actionsList = new() { Margin = new Thickness(0, 0, 0, 8), MinHeight = 160 };
    private readonly ComboBox rewardTitleCombo = new() { IsEditable = true, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock statusText = new() { Margin = new Thickness(12, 0, 12, 12) };

    private sealed class ActionListItem(WizAction action, string display)
    {
        public WizAction Action { get; } = action;

        public override string ToString() => display;
    }

    public WizSettingsWindow(WizConfigStore configStore, WizClient client)
    {
        this.configStore = configStore;
        this.client = client;

        var config = configStore.Load();
        devices = new ObservableCollection<WizDevice>(config.Devices);
        mappings = new ObservableCollection<EventActionMapping>(config.Mappings);
        dispatcher = new EventActionDispatcher(BuildConfig, client);

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

        var saveButton = new Button { Content = "Save", Width = 90, HorizontalAlignment = HorizontalAlignment.Right };
        saveButton.Click += (_, _) => OnSave();

        var bottomPanel = new DockPanel { Margin = new Thickness(12, 8, 12, 12) };
        DockPanel.SetDock(saveButton, Dock.Right);
        bottomPanel.Children.Add(saveButton);
        bottomPanel.Children.Add(statusText);

        var root = new DockPanel();
        DockPanel.SetDock(bottomPanel, Dock.Bottom);
        root.Children.Add(bottomPanel);
        root.Children.Add(tabs);

        Content = root;

        RefreshRewardTitleSuggestions();
    }

    private WizConfig BuildConfig() => new() { Devices = devices.ToList(), Mappings = mappings.ToList() };

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

    private UIElement BuildMappingsTab()
    {
        mappingsList.ItemsSource = mappings;
        mappingsList.SelectionChanged += (_, _) => RefreshActionsList();

        var addMappingButton = new Button { Content = "Add Mapping", Width = 100, Margin = new Thickness(0, 0, 8, 0) };
        addMappingButton.Click += (_, _) => OnAddMapping();

        var removeMappingButton = new Button { Content = "Remove Mapping", Width = 110 };
        removeMappingButton.Click += (_, _) => OnRemoveMapping();

        var mappingButtonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { addMappingButton, removeMappingButton },
        };

        var leftPanel = new StackPanel
        {
            Margin = new Thickness(0, 12, 8, 12),
            Width = 220,
            Children =
            {
                new TextBlock { Text = "Reward title" },
                rewardTitleCombo,
                mappingButtonsPanel,
                new TextBlock { Text = "Mappings" },
                mappingsList,
            },
        };

        var addActionButton = new Button { Content = "Add Action", Width = 100, Margin = new Thickness(0, 0, 8, 0) };
        addActionButton.Click += (_, _) => OnAddAction();

        var editActionButton = new Button { Content = "Edit Action", Width = 100, Margin = new Thickness(0, 0, 8, 0) };
        editActionButton.Click += (_, _) => OnEditAction();

        var removeActionButton = new Button { Content = "Remove Action", Width = 110, Margin = new Thickness(0, 0, 8, 0) };
        removeActionButton.Click += (_, _) => OnRemoveAction();

        var testButton = new Button { Content = "Test", Width = 80 };
        testButton.Click += async (_, _) => await OnTestAsync().ConfigureAwait(true);

        var actionButtonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { addActionButton, editActionButton, removeActionButton, testButton },
        };

        var rightPanel = new StackPanel
        {
            Margin = new Thickness(8, 12, 0, 12),
            Children =
            {
                new TextBlock { Text = "Actions for selected mapping" },
                actionsList,
                actionButtonsPanel,
            },
        };

        var splitPanel = new DockPanel();
        DockPanel.SetDock(leftPanel, Dock.Left);
        splitPanel.Children.Add(leftPanel);
        splitPanel.Children.Add(rightPanel);

        return splitPanel;
    }

    private void RefreshRewardTitleSuggestions()
    {
        rewardTitleCombo.ItemsSource = mappings.Select(m => m.RewardTitle).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void RefreshActionsList()
    {
        var mapping = mappingsList.SelectedItem as EventActionMapping;
        if (mapping is null)
        {
            actionsList.ItemsSource = null;
            return;
        }

        actionsList.ItemsSource = mapping.Actions
            .Select(a => new ActionListItem(a, FormatAction(a)))
            .ToList();
    }

    private string FormatAction(WizAction action)
    {
        var device = devices.FirstOrDefault(d => d.Id == action.DeviceId);
        var deviceName = device?.Name ?? "(unknown device)";

        var detail = action.ActionKind switch
        {
            WizActionKind.SetBrightness => $"SetBrightness {action.Brightness}%",
            WizActionKind.SetColor => $"SetColor ({action.R},{action.G},{action.B})",
            WizActionKind.SetColorTemperature => $"SetColorTemperature {action.ColorTemperatureKelvin}K",
            _ => action.ActionKind.ToString(),
        };

        return $"{deviceName}: {detail}";
    }

    private void OnAddDevice()
    {
        var dialog = new WizDeviceEditDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } device)
        {
            devices.Add(device);
        }
    }

    private void OnEditDevice()
    {
        if (devicesList.SelectedItem is not WizDevice selected)
        {
            statusText.Text = "Select a device to edit.";
            return;
        }

        var index = devices.IndexOf(selected);
        var dialog = new WizDeviceEditDialog(selected) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } updated)
        {
            devices[index] = updated;

            var removedActionCount = 0;
            foreach (var mapping in mappings)
            {
                removedActionCount += mapping.Actions.RemoveAll(
                    a => a.DeviceId == updated.Id && WizAction.Validate(a, updated).Count > 0);
            }

            RefreshActionsList();
            statusText.Text = removedActionCount > 0
                ? $"Updated device and removed {removedActionCount} action(s) no longer valid for it."
                : "Updated device.";
        }
    }

    private void OnRemoveDevice()
    {
        if (devicesList.SelectedItem is not WizDevice selected)
        {
            statusText.Text = "Select a device to remove.";
            return;
        }

        devices.Remove(selected);

        var removedActionCount = 0;
        foreach (var mapping in mappings)
        {
            removedActionCount += mapping.Actions.RemoveAll(a => a.DeviceId == selected.Id);
        }

        RefreshActionsList();
        statusText.Text = removedActionCount > 0
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

    private void OnAddMapping()
    {
        var title = rewardTitleCombo.Text.Trim();
        if (string.IsNullOrEmpty(title))
        {
            statusText.Text = "Enter a reward title first.";
            return;
        }

        var existing = mappings.FirstOrDefault(m => string.Equals(m.RewardTitle, title, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            mappingsList.SelectedItem = existing;
            return;
        }

        var mapping = new EventActionMapping(title, []);
        mappings.Add(mapping);
        mappingsList.SelectedItem = mapping;
        RefreshRewardTitleSuggestions();
    }

    private void OnRemoveMapping()
    {
        if (mappingsList.SelectedItem is not EventActionMapping selected)
        {
            statusText.Text = "Select a mapping to remove.";
            return;
        }

        mappings.Remove(selected);
        RefreshRewardTitleSuggestions();
        RefreshActionsList();
    }

    private void OnAddAction()
    {
        if (mappingsList.SelectedItem is not EventActionMapping mapping)
        {
            statusText.Text = "Select a mapping first.";
            return;
        }

        if (devices.Count == 0)
        {
            statusText.Text = "Add a device first.";
            return;
        }

        var dialog = new WizActionEditDialog(devices) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } action)
        {
            mapping.Actions.Add(action);
            RefreshActionsList();
        }
    }

    private void OnEditAction()
    {
        if (mappingsList.SelectedItem is not EventActionMapping mapping)
        {
            statusText.Text = "Select a mapping first.";
            return;
        }

        if (actionsList.SelectedItem is not ActionListItem selected)
        {
            statusText.Text = "Select an action to edit.";
            return;
        }

        var index = mapping.Actions.IndexOf(selected.Action);
        var dialog = new WizActionEditDialog(devices, selected.Action) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } updated)
        {
            mapping.Actions[index] = updated;
            RefreshActionsList();
        }
    }

    private void OnRemoveAction()
    {
        if (mappingsList.SelectedItem is not EventActionMapping mapping)
        {
            statusText.Text = "Select a mapping first.";
            return;
        }

        if (actionsList.SelectedItem is not ActionListItem selected)
        {
            statusText.Text = "Select an action to remove.";
            return;
        }

        mapping.Actions.Remove(selected.Action);
        RefreshActionsList();
    }

    private async Task OnTestAsync()
    {
        if (mappingsList.SelectedItem is not EventActionMapping mapping)
        {
            statusText.Text = "Select a mapping to test.";
            return;
        }

        statusText.Text = "Testing...";

        var botEvent = new BotEventArgs(RedemptionEventName, new Dictionary<string, string?> { [RewardTitleMetadataKey] = mapping.RewardTitle });
        var result = await dispatcher.DispatchAsync(botEvent).ConfigureAwait(true);

        if (result is null)
        {
            statusText.Text = "Test did not dispatch any actions.";
            return;
        }

        var lines = result.ActionResults.Select(r =>
            $"{FormatAction(r.Action)}: {(r.Success ? "OK" : r.ErrorMessage ?? "Failed")}");
        MessageBox.Show(this, string.Join("\n", lines), $"Test results: {result.RewardTitle}");
        statusText.Text = result.AllSucceeded ? "Test succeeded." : "Test completed with errors.";
    }

    private void OnSave()
    {
        configStore.Save(BuildConfig());
        statusText.Text = $"Saved to {configStore.ConfigFilePath}";
    }
}
