using System.Diagnostics;
using System.Windows;
using BCUKCompanion.TrayApp;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace BCUKCompanion.ExpiredMinotaur.Updates;

/// <summary>
/// The user-facing update flow on top of <see cref="UpdateClient"/>: a silent check at launch
/// that installs any newer release automatically, and a "Check for updates..." tray menu item
/// that reports the result and asks before installing. Either way, installing shuts the app
/// down so the update script can swap the files and relaunch it.
/// </summary>
public sealed class AppUpdater
{
    private const string Caption = "BCUK Companion updates";

    private readonly UpdateClient client = new();
    private Action<string, string>? showBalloon;

    // Interlocked-guarded: 1 while a check/install is in flight, so a menu click during the
    // launch check (or a double click) doesn't start a second download.
    private int busy;

    public TrayMenuItem CreateMenuItem() => new("Check for updates...", () => _ = RunAsync(interactive: true));

    /// <summary>
    /// Starts the background launch check. Call once, on the UI thread, once the tray icon exists
    /// (i.e. from <c>OnTrayIconReady</c>) so progress and failures can be shown as balloons.
    /// Local/dev builds skip it so they never replace themselves with the published release.
    /// </summary>
    public void CheckOnLaunch(Action<string, string> showBalloon)
    {
        this.showBalloon = showBalloon;
        if (UpdateClient.IsReleaseBuild)
        {
            _ = RunAsync(interactive: false);
        }
    }

    // Awaits resume on the UI thread (no ConfigureAwait(false)), so message boxes and Shutdown
    // run where WPF expects them.
    private async Task RunAsync(bool interactive)
    {
        if (Interlocked.Exchange(ref busy, 1) == 1)
        {
            if (interactive)
            {
                MessageBox.Show("An update check is already in progress.", Caption);
            }
            return;
        }

        try
        {
            if (!UpdateClient.IsReleaseBuild)
            {
                if (interactive)
                {
                    MessageBox.Show("This is a development build, so it doesn't update itself. Download releases from GitHub instead.", Caption);
                }
                return;
            }

            AvailableUpdate? update;
            try
            {
                update = await client.CheckForUpdateAsync();
            }
            catch (Exception ex)
            {
                // Offline at launch is normal — only report failures the user asked about.
                if (interactive)
                {
                    MessageBox.Show($"Couldn't check for updates:\n{ex.Message}", Caption, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            if (update is null)
            {
                if (interactive)
                {
                    MessageBox.Show($"You're on the latest version (v{UpdateClient.CurrentVersion}).", Caption);
                }
                return;
            }

            if (interactive && MessageBox.Show(
                    $"Version {update.Version} is available (you have {UpdateClient.CurrentVersion}).\n\nInstall it now? The app will restart.",
                    Caption, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            showBalloon?.Invoke("Updating BCUK Companion", $"Downloading v{update.Version}. The app will restart when it's ready.");
            try
            {
                await client.PrepareAndLaunchInstallerAsync(update);
            }
            catch (Exception ex)
            {
                ReportInstallFailure(update, ex, interactive);
                return;
            }

            Application.Current.Shutdown();
        }
        finally
        {
            Interlocked.Exchange(ref busy, 0);
        }
    }

    private void ReportInstallFailure(AvailableUpdate update, Exception ex, bool interactive)
    {
        string reason = ex is UnauthorizedAccessException
            ? $"{ex.Message} Move the app to a folder you can write to, or download v{update.Version} manually."
            : ex.Message;

        if (!interactive)
        {
            showBalloon?.Invoke("Update failed", $"Couldn't install v{update.Version}: {reason}");
            return;
        }

        if (MessageBox.Show($"Couldn't install v{update.Version}:\n{reason}\n\nOpen the download page?",
                Caption, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo(update.ReleasePageUrl.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }
}
