using BCUKCompanion.Core;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.Core.Models;
using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Actions;
using BCUKCompanion.ExpiredMinotaur.Updates;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using BCUKCompanion.TrayApp;

namespace BCUKCompanion.ExpiredMinotaur;

internal static class Program
{
    internal const string DataFolderName = "BCUKCompanion.ExpiredMinotaur";

    /// <summary>
    /// Registers this app's actions (WiZ lights, treadmill) and runs the shared companion tray app,
    /// dispatching bot events — reward redemptions and advanced commands — to the mapped actions.
    /// </summary>
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

        // Set via OnClientReady once the tray shell creates (or recreates, on a bot-host
        // change) its CompanionClient. Settings windows read this through a Func so they
        // always see the current instance, even one created after the window itself.
        CompanionClient? companionClient = null;

        // Set via OnTrayIconReady once, at startup. Lets dispatchAsync surface dispatch
        // failures (crashes and completed-but-failed actions alike) as tray balloons,
        // since Debug.WriteLine alone is compiled out of a Release build.
        Action<string, string>? showBalloon = null;

        var updater = new AppUpdater();

        // Runs the actions mapped to a redemption or advanced command, reporting failures as
        // balloons. Returns the result so the shell can acknowledge advanced commands from it.
        Func<BotEventArgs, Task<EventDispatchResult?>> dispatchAsync = async e =>
        {
            var config = store.Load();
            var dispatcher = new EventActionDispatcher(
                () => config.Mappings,
                () => new CompositeEventActionContext(
                    new WizActionContext(client, config.Devices),
                    new TreadmillActionContext(treadmillClient)));
            try
            {
                var result = await Task.Run(() => dispatcher.DispatchAsync(e)).ConfigureAwait(true);
                ReportFailedActions(result, showBalloon);
                return result;
            }
            catch (Exception ex)
            {
                showBalloon?.Invoke("Action dispatch crashed", ex.Message);
                throw;
            }
        };

        CompanionTrayApplication.Run(new CompanionTrayAppOptions
        {
            DataFolderName = DataFolderName,
            OnClientReady = client => companionClient = client,
            OnTrayIconReady = balloon =>
            {
                showBalloon = balloon;
                // Installs any newer GitHub release in the background, restarting the app.
                updater.CheckOnLaunch(balloon);
            },
            // Shell 0.7.0 only awaits OnBotEventAsync for advanced commands; channel-point
            // redemptions reach the host through OnBotEvent alone, so they're dispatched here
            // (fire-and-forget, failures still balloon). Commands also pass through OnBotEvent,
            // so filter to redemptions to avoid running a command's actions twice. If a later
            // Shell starts awaiting OnBotEventAsync for redemptions too, drop this.
            OnBotEvent = e =>
            {
                if (e.EventName == EventActionDispatcher.RedemptionEventName)
                {
                    _ = dispatchAsync(e);
                }
            },
            // Awaited by the shell, which acknowledges advanced commands to the bot from the
            // result: anything short of every mapped action succeeding refunds the viewer.
            OnBotEventAsync = dispatchAsync,
            AdditionalMenuItems = new[]
            {
                SingletonWindowMenuItem.Create("Actions...",
                    () => new ActionsSettingsWindow(store, client, treadmillClient, () => companionClient)),
                updater.CreateMenuItem(),
            },
        });

        treadmillClient.Dispose();
    }

    /// <summary>
    /// Surfaces actions that ran but failed (a wrong device IP, an offline light, a disconnected
    /// treadmill, ...) as a tray balloon, since Debug output is compiled out of a Release build.
    /// </summary>
    private static void ReportFailedActions(EventDispatchResult? result, Action<string, string>? showBalloon)
    {
        if (result is null || result.ActionResults.Count == 0 || result.AllSucceeded)
        {
            return;
        }

        var detail = string.Join("; ", result.ActionResults.Where(r => !r.Success).Select(r => r.ErrorMessage ?? "Action failed."));
        showBalloon?.Invoke("Action failed", detail);
    }
}
