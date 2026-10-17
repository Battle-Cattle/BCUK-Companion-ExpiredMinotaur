using BCUKCompanion.Core;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using BCUKCompanion.TrayApp;
using BCUKCompanion.TrayApp.Updates;

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
        var wizClient = new WizClient();
        var treadmillClient = new TreadmillClient();

        // Set via OnClientReady once the tray shell creates (or recreates, on a bot-host
        // change) its CompanionClient. Settings windows read this through a Func so they
        // always see the current instance, even one created after the window itself.
        CompanionClient? companionClient = null;

        CompanionTrayApplication.Run(new CompanionTrayAppOptions
        {
            DataFolderName = DataFolderName,
            OnClientReady = readyClient => companionClient = readyClient,
            // Installs any newer GitHub release in the background, restarting the app, and adds
            // a "Check for updates..." tray menu item.
            Updates = new CompanionUpdateOptions
            {
                RepoOwner = "Battle-Cattle",
                RepoName = "BCUK-Companion-ExpiredMinotaur",
            },
            // The shell runs every redemption's and advanced command's mapped actions with this,
            // reports failures as balloons, and acknowledges advanced commands from the result.
            // Built per event so it always sees the latest saved mappings and devices.
            CreateEventActionDispatcher = () =>
            {
                var config = store.Load();
                return new EventActionDispatcher(
                    () => config.Mappings,
                    () => new CompositeEventActionContext(
                        new WizActionContext(wizClient, config.Devices),
                        new TreadmillActionContext(treadmillClient)));
            },
            AdditionalMenuItems = new[]
            {
                SingletonWindowMenuItem.Create("Actions...",
                    () => new ActionsSettingsWindow(store, wizClient, treadmillClient, () => companionClient)),
            },
        });

        treadmillClient.Dispose();
    }
}
