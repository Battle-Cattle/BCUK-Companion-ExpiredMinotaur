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

        // Set via OnTrayIconReady once, at startup. Lets OnBotEventAsync surface dispatch
        // failures (crashes and completed-but-failed actions alike) as tray balloons,
        // since Debug.WriteLine alone is compiled out of a Release build.
        Action<string, string>? showBalloon = null;

        CompanionTrayApplication.Run(new CompanionTrayAppOptions
        {
            DataFolderName = DataFolderName,
            OnClientReady = client => companionClient = client,
            OnTrayIconReady = balloon => showBalloon = balloon,
            // Awaited by the shell, which acknowledges advanced commands to the bot from the
            // result: anything short of every mapped action succeeding refunds the viewer.
            OnBotEventAsync = async e =>
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
            },
            AdditionalMenuItems = new[]
            {
                SingletonWindowMenuItem.Create("Actions...",
                    () => new ActionsSettingsWindow(store, client, treadmillClient, () => companionClient)),
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
