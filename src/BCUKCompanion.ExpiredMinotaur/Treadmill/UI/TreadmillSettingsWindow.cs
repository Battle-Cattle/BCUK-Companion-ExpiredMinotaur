using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.Core.Models;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListBox = System.Windows.Controls.ListBox;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using TabControl = System.Windows.Controls.TabControl;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill.UI;

public sealed class TreadmillSettingsWindow : Window
{
    private const string RedemptionEventName = "redemption.received";
    private const string RewardTitleMetadataKey = "rewardTitle";

    private readonly TreadmillConfigStore configStore;
    private readonly TreadmillClient client;
    private readonly EventActionDispatcher dispatcher;

    private readonly ObservableCollection<EventActionMapping> mappings;

    private readonly ListBox mappingsList = new() { Margin = new Thickness(0, 0, 0, 8), MinHeight = 200 };
    private readonly ListBox actionsList = new() { Margin = new Thickness(0, 0, 0, 8), MinHeight = 160 };
    private readonly ComboBox rewardTitleCombo = new() { IsEditable = true, Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock statusText = new() { Margin = new Thickness(12, 0, 12, 12) };
    private readonly TextBlock connectionStatusText = new() { Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock speedText = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly Button connectButton = new() { Content = "Connect", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button disconnectButton = new() { Content = "Disconnect", Width = 90 };

    private readonly DispatcherTimer speedTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private sealed class ActionListItem(IEventAction action, string display)
    {
        public IEventAction Action { get; } = action;

        public override string ToString() => display;
    }

    public TreadmillSettingsWindow(TreadmillConfigStore configStore, TreadmillClient client)
    {
        this.configStore = configStore;
        this.client = client;

        var config = configStore.Load();
        mappings = new ObservableCollection<EventActionMapping>(config.Mappings);
        dispatcher = new EventActionDispatcher(() => BuildConfig().Mappings, BuildContext);

        Title = "Treadmill";
        Width = 640;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // StatusChanged fires on whatever thread triggered it (a BLE callback thread, or the
        // keep-alive timer's thread-pool thread) — marshal onto the UI thread ourselves.
        client.StatusChanged += (_, message) => Dispatcher.Invoke(() => connectionStatusText.Text = message);

        connectButton.Click += async (_, _) => await OnConnectAsync().ConfigureAwait(true);
        disconnectButton.Click += (_, _) => OnDisconnect();

        speedTimer.Tick += (_, _) => RefreshSpeedText();
        RefreshSpeedText();
        RefreshConnectionButtons();
        speedTimer.Start();

        var connectionButtonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { connectButton, disconnectButton },
        };

        var topPanel = new StackPanel
        {
            Margin = new Thickness(12, 12, 12, 0),
            Children = { connectionButtonsPanel, connectionStatusText, speedText },
        };

        var tabs = new TabControl
        {
            Margin = new Thickness(12, 12, 12, 0),
        };
        tabs.Items.Add(new TabItem { Header = "Event Mappings", Content = BuildMappingsTab() });

        var saveButton = new Button { Content = "Save", Width = 90, HorizontalAlignment = HorizontalAlignment.Right };
        saveButton.Click += (_, _) => OnSave();

        var bottomPanel = new DockPanel { Margin = new Thickness(12, 8, 12, 12) };
        DockPanel.SetDock(saveButton, Dock.Right);
        bottomPanel.Children.Add(saveButton);
        bottomPanel.Children.Add(statusText);

        var root = new DockPanel();
        DockPanel.SetDock(topPanel, Dock.Top);
        DockPanel.SetDock(bottomPanel, Dock.Bottom);
        root.Children.Add(topPanel);
        root.Children.Add(bottomPanel);
        root.Children.Add(tabs);

        Content = root;

        RefreshRewardTitleSuggestions();

        Closed += (_, _) => speedTimer.Stop();
    }

    private TreadmillConfig BuildConfig() => new() { Mappings = mappings.ToList() };

    private TreadmillActionContext BuildContext() => new(client);

    private void RefreshSpeedText()
    {
        speedText.Text = $"Current: {client.CurrentSpeedKmh:0.0} km/h   Target: {client.TargetSpeedKmh:0.0} km/h";
        RefreshConnectionButtons();
    }

    private void RefreshConnectionButtons()
    {
        connectButton.IsEnabled = !client.IsConnected;
        disconnectButton.IsEnabled = client.IsConnected;
    }

    private async Task OnConnectAsync()
    {
        connectButton.IsEnabled = false;
        await client.ConnectAsync().ConfigureAwait(true);
        RefreshConnectionButtons();
    }

    private void OnDisconnect()
    {
        client.Disconnect();
        RefreshConnectionButtons();
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

        var context = BuildContext();
        actionsList.ItemsSource = mapping.Actions
            .Select(a => new ActionListItem(a, a.Describe(context)))
            .ToList();
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

        var dialog = new TreadmillActionEditDialog(client) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } action)
        {
            ReplaceMappingActions(mapping, [.. mapping.Actions, action]);
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

        var index = actionsList.SelectedIndex;
        var dialog = new TreadmillActionEditDialog(client, selected.Action) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } updated)
        {
            var newActions = mapping.Actions.ToList();
            newActions[index] = updated;
            ReplaceMappingActions(mapping, newActions);
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

        var newActions = mapping.Actions.ToList();
        newActions.RemoveAt(actionsList.SelectedIndex);
        ReplaceMappingActions(mapping, newActions);
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

        var context = BuildContext();
        var lines = result.ActionResults.Select(r =>
            $"{r.Action.Describe(context)}: {(r.Success ? "OK" : r.ErrorMessage ?? "Failed")}");
        MessageBox.Show(this, string.Join("\n", lines), $"Test results: {result.RewardTitle}");
        statusText.Text = result.AllSucceeded ? "Test succeeded." : "Test completed with errors.";
    }

    private void ReplaceMappingActions(EventActionMapping mapping, List<IEventAction> newActions)
    {
        var idx = mappings.IndexOf(mapping);
        if (idx < 0) return;
        var updated = mapping with { Actions = newActions };
        mappings[idx] = updated;
        if (ReferenceEquals(mappingsList.SelectedItem, mapping))
            mappingsList.SelectedItem = updated;
    }

    private void OnSave()
    {
        try
        {
            configStore.Save(BuildConfig());
            statusText.Text = $"Saved to {configStore.ConfigFilePath}";
        }
        catch (Exception ex)
        {
            statusText.Text = $"Save failed: {ex.Message}";
        }
    }
}
