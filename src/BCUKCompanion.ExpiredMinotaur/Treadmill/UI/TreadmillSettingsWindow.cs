using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BCUKCompanion.Core.Actions;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TabControl = System.Windows.Controls.TabControl;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill.UI;

public sealed class TreadmillSettingsWindow : EventActionMappingsWindow<TreadmillConfig>
{
    private readonly TreadmillClient client;

    private readonly TextBlock connectionStatusText = new() { Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock speedText = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly Button connectButton = new() { Content = "Connect", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button disconnectButton = new() { Content = "Disconnect", Width = 90 };

    private readonly DispatcherTimer speedTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public TreadmillSettingsWindow(TreadmillConfigStore configStore, TreadmillClient client)
        : base(configStore, configStore.Load())
    {
        this.client = client;

        Title = "Treadmill";
        Width = 640;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // StatusChanged fires on whatever thread triggered it (a BLE callback thread, or the
        // keep-alive timer's thread-pool thread) — marshal onto the UI thread ourselves.
        client.StatusChanged += OnStatusChanged;

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
            client.StatusChanged -= OnStatusChanged;
        };
    }

    private void OnStatusChanged(object? sender, string message) => Dispatcher.Invoke(() => connectionStatusText.Text = message);

    protected override TreadmillConfig BuildConfig() => new() { Mappings = Mappings.ToList() };

    protected override IEventActionContext BuildContext() => new TreadmillActionContext(client);

    protected override IEventAction? ShowAddActionDialog()
    {
        var dialog = new TreadmillActionEditDialog(client) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    protected override IEventAction? ShowEditActionDialog(IEventAction existing)
    {
        var dialog = new TreadmillActionEditDialog(client, existing) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

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
        try
        {
            await client.ConnectAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            connectionStatusText.Text = $"Connect failed: {ex.Message}";
        }
        finally
        {
            RefreshConnectionButtons();
        }
    }

    private void OnDisconnect()
    {
        try
        {
            client.Disconnect();
        }
        catch (Exception ex)
        {
            connectionStatusText.Text = $"Disconnect failed: {ex.Message}";
        }
        finally
        {
            RefreshConnectionButtons();
        }
    }
}
