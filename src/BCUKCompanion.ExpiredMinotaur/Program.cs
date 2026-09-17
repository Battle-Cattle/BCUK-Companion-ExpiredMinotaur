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
                var config = store.Load();
                var dispatcher = new EventActionDispatcher(
                    () => config.Mappings,
                    () => new CompositeEventActionContext(
                        new WizActionContext(client, config.Devices),
                        new TreadmillActionContext(treadmillClient)));
                dispatcher.DispatchAndReportAsync(e, showBalloon);
            },
            AdditionalMenuItems = new[]
            {
                SingletonWindowMenuItem.Create("Actions...",
                    () => new ActionsSettingsWindow(store, client, treadmillClient, () => companionClient)),
            },
        });

        treadmillClient.Dispose();
    }
}
