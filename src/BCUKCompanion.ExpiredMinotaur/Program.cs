using System.Diagnostics;
using System.Windows;
using BCUKCompanion.Core;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using BCUKCompanion.TrayApp;

namespace BCUKCompanion.ExpiredMinotaur;

internal static class Program
{
    internal const string DataFolderName = "BCUKCompanion.ExpiredMinotaur";

    [STAThread]
    private static void Main()
    {
        var registry = new EventActionTypeRegistry();
        registry.Register<WizTurnOnAction>(WizTurnOnAction.ActionKind);
        registry.Register<WizTurnOffAction>(WizTurnOffAction.ActionKind);
        registry.Register<WizToggleAction>(WizToggleAction.ActionKind);
        registry.Register<WizSetBrightnessAction>(WizSetBrightnessAction.ActionKind);
        registry.Register<WizSetColorAction>(WizSetColorAction.ActionKind);
        registry.Register<WizSetColorTemperatureAction>(WizSetColorTemperatureAction.ActionKind);
        registry.Register<TreadmillNudgeSpeedAction>(TreadmillNudgeSpeedAction.ActionKind);

        var store = new ActionsConfigStore(DataFolderName, registry);
        var client = new WizClient();
        var treadmillClient = new TreadmillClient();

        ActionsSettingsWindow? settingsWindow = null;

        // Set via OnClientReady once the tray shell creates (or recreates, on a bot-host
        // change) its CompanionClient. Settings windows read this through a Func so they
        // always see the current instance, even one created after the window itself.
        CompanionClient? companionClient = null;

        // Set via OnTrayIconReady once, at startup. Lets OnBotEvent surface dispatch
        // failures (crashes and completed-but-failed actions alike) as tray balloons,
        // since Debug.WriteLine alone is compiled out of a Release build.
        Action<string, string>? showBalloon = null;

        CompanionTrayApplication.Run(new CompanionTrayAppOptions
        {
            DataFolderName = DataFolderName,
            OnClientReady = client => companionClient = client,
            OnTrayIconReady = balloon => showBalloon = balloon,
            OnBotEvent = e =>
            {
                var dispatch = Task.Run(() =>
                {
                    var config = store.Load();
                    var dispatcher = new EventActionDispatcher(
                        () => config.Mappings,
                        () => new ActionsContext(
                            new WizActionContext(client, config.Devices),
                            new TreadmillActionContext(treadmillClient)));
                    return dispatcher.DispatchAsync(e);
                });
                dispatch.ContinueWith(
                    t => Debug.WriteLine($"Action dispatch failed: {t.Exception}"),
                    TaskContinuationOptions.OnlyOnFaulted);
                dispatch.ContinueWith(
                    t => showBalloon?.Invoke("Action dispatch crashed", DescribeFault(t.Exception)),
                    TaskContinuationOptions.OnlyOnFaulted);
                dispatch.ContinueWith(
                    t => ReportDispatchFailure(t.Result, "Action failed", showBalloon),
                    TaskContinuationOptions.OnlyOnRanToCompletion);
            },
            AdditionalMenuItems = new[]
            {
                CreateSettingsMenuItem("Actions...",
                    () => new ActionsSettingsWindow(store, client, treadmillClient, () => companionClient),
                    () => settingsWindow, w => settingsWindow = w),
            },
        });

        treadmillClient.Dispose();
    }

    /// <summary>
    /// Reports a completed (non-faulted) dispatch that did not fully succeed — a wrong Wiz
    /// IP, an offline light, a disconnected treadmill, etc. — via a tray balloon, so it is
    /// visible in a Release build and not just silently discarded. A null result (event
    /// wasn't a redemption / had no reward title) or a result with zero matching actions
    /// (nothing configured for that reward) is not a failure and is not reported.
    /// </summary>
    private static void ReportDispatchFailure(
        EventDispatchResult? result, string balloonTitle, Action<string, string>? showBalloon)
    {
        if (result is null || result.ActionResults.Count == 0 || result.AllSucceeded)
        {
            return;
        }

        var detail = string.Join(
            "; ",
            result.ActionResults.Where(r => !r.Success).Select(r => r.ErrorMessage ?? "Action failed."));
        showBalloon?.Invoke(balloonTitle, detail);
    }

    private static string DescribeFault(AggregateException? exception) =>
        exception?.Flatten().InnerException?.Message ?? "Unknown error.";

    private static TrayMenuItem CreateSettingsMenuItem<TWindow>(
        string label, Func<TWindow> createWindow, Func<TWindow?> getWindow, Action<TWindow?> setWindow)
        where TWindow : Window
    {
        return new TrayMenuItem(label, () =>
        {
            var window = getWindow();
            if (window is null)
            {
                window = createWindow();
                window.Closed += (_, _) => setWindow(null);
                setWindow(window);
                window.Show();
            }
            else
            {
                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }
                window.Activate();
            }
        });
    }
}
