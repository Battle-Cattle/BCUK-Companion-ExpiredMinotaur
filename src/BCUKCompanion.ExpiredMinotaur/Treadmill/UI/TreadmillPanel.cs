using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;

namespace BCUKCompanion.ExpiredMinotaur.Treadmill.UI;

/// <summary>
/// The treadmill connection panel at the top of the Actions window: Connect/Disconnect buttons,
/// the latest status message and a once-a-second speed line. Call <see cref="Detach"/> when the
/// window closes.
/// </summary>
public sealed class TreadmillPanel : StackPanel
{
    private readonly TreadmillClient treadmillClient;

    private readonly TextBlock statusText = new() { Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBlock speedText = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly Button connectButton = new() { Content = "Connect Treadmill", Width = 130, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button disconnectButton = new() { Content = "Disconnect Treadmill", Width = 130 };

    private readonly DispatcherTimer speedTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    // Tracks a Connect/Disconnect in flight so the 1-second speedTimer tick doesn't re-enable
    // the button mid-operation (TreadmillClient.IsConnected alone doesn't reflect "connecting"),
    // which would let a second click queue another request behind TreadmillClient's write lock.
    private bool operationInProgress;

    /// <summary>Builds the panel and starts following <paramref name="treadmillClient"/>.</summary>
    /// <param name="treadmillClient">The shared treadmill connection the panel controls.</param>
    public TreadmillPanel(TreadmillClient treadmillClient)
    {
        this.treadmillClient = treadmillClient;
        Margin = new Thickness(12, 12, 12, 0);

        // StatusChanged fires on whatever thread triggered it (a BLE callback thread, or the
        // follow timer's thread-pool thread) — marshal onto the UI thread ourselves.
        treadmillClient.StatusChanged += OnStatusChanged;

        connectButton.Click += async (_, _) => await RunConnectionOperationAsync(() => treadmillClient.ConnectAsync(), "Connect").ConfigureAwait(true);
        disconnectButton.Click += async (_, _) => await RunConnectionOperationAsync(treadmillClient.DisconnectAsync, "Disconnect").ConfigureAwait(true);

        speedTimer.Tick += (_, _) => RefreshSpeedText();
        RefreshSpeedText();
        speedTimer.Start();

        Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { connectButton, disconnectButton },
        });
        Children.Add(statusText);
        Children.Add(speedText);
    }

    /// <summary>Stops the speed timer and stops listening to the treadmill's status messages.</summary>
    public void Detach()
    {
        speedTimer.Stop();
        treadmillClient.StatusChanged -= OnStatusChanged;
    }

    /// <summary>Shows a treadmill status message, marshalled onto the UI thread.</summary>
    /// <remarks>
    /// InvokeAsync, not Invoke: TreadmillClient raises StatusChanged while holding its write lock,
    /// and blocking that thread on the UI thread would deadlock if the UI thread ever waited on
    /// the same lock.
    /// </remarks>
    /// <param name="sender">The <see cref="TreadmillClient"/> that raised the event.</param>
    /// <param name="message">The status text to show.</param>
    private void OnStatusChanged(object? sender, string message) =>
        _ = Dispatcher.InvokeAsync(() => statusText.Text = message);

    /// <summary>Updates the current/target speed line (every second) and the connection buttons.</summary>
    private void RefreshSpeedText()
    {
        speedText.Text = TreadmillPanelState.FormatSpeed(treadmillClient.CurrentSpeedKmh, treadmillClient.TargetSpeedKmh);
        RefreshConnectionButtons();
    }

    /// <summary>Enables Connect or Disconnect to match the connection, or neither while one is in flight.</summary>
    private void RefreshConnectionButtons()
    {
        (connectButton.IsEnabled, disconnectButton.IsEnabled) =
            TreadmillPanelState.ConnectionButtons(operationInProgress, treadmillClient.IsConnected);
    }

    /// <summary>Runs a connect or disconnect, keeping both buttons disabled until it finishes.</summary>
    /// <param name="operation">Starts the connect or disconnect.</param>
    /// <param name="label">"Connect" or "Disconnect", for the failure message.</param>
    /// <returns>A task that completes once the attempt has finished, successful or not.</returns>
    private async Task RunConnectionOperationAsync(Func<Task> operation, string label)
    {
        operationInProgress = true;
        RefreshConnectionButtons();
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            statusText.Text = $"{label} failed: {ex.Message}";
        }
        finally
        {
            operationInProgress = false;
            RefreshConnectionButtons();
        }
    }
}
