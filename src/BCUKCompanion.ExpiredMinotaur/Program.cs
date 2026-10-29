using BCUKCompanion.Core;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using BCUKCompanion.TrayApp;
using BCUKCompanion.TrayApp.Updates;

namespace BCUKCompanion.ExpiredMinotaur;

/// <summary>The app's entry point.</summary>
internal static class Program
{
    /// <summary>
    /// The folder (under the user's profile) for this app's settings and tokens, also used for its
    /// single-instance lock and startup entry. Unique to this app, so it never collides with another
    /// companion app on the same Windows account.
    /// </summary>
    internal const string DataFolderName = "BCUKCompanion.ExpiredMinotaur";

    /// <summary>
    /// Registers this app's actions (WiZ lights, treadmill) and runs the shared companion tray app,
    /// dispatching bot events — reward redemptions and advanced commands — to the mapped actions.
    /// </summary>
    [STAThread]
    internal static void Main()
    {
        var store = new ActionsConfigStore(DataFolderName, CreateRegistry());
        var wizClient = new WizClient();
        using var treadmillClient = new TreadmillClient();

        RunTrayApp(CreateOptions(store, wizClient, treadmillClient));
    }

    /// <summary>Runs the shared tray app until the user exits it; tests replace it to see the options without starting WPF.</summary>
    internal static Action<CompanionTrayAppOptions> RunTrayApp { get; set; } = CompanionTrayApplication.Run;

    /// <summary>Registers every action kind this app's actions-config.json can hold.</summary>
    /// <returns>A registry with the WiZ and treadmill action kinds.</returns>
    internal static EventActionTypeRegistry CreateRegistry()
    {
        var registry = new EventActionTypeRegistry();
        registry.Register<WizTurnOnAction>(WizTurnOnAction.ActionKind);
        registry.Register<WizTurnOffAction>(WizTurnOffAction.ActionKind);
        registry.Register<WizToggleAction>(WizToggleAction.ActionKind);
        registry.Register<WizSetBrightnessAction>(WizSetBrightnessAction.ActionKind);
        registry.Register<WizSetColorAction>(WizSetColorAction.ActionKind);
        registry.Register<WizSetColorTemperatureAction>(WizSetColorTemperatureAction.ActionKind);
        registry.Register<TreadmillNudgeSpeedAction>(TreadmillNudgeSpeedAction.ActionKind);
        return registry;
    }

    /// <summary>The shared tray app's options for this app: its data folder, updates, action dispatch and "Actions..." menu item.</summary>
    /// <param name="store">Loads the saved devices and mappings for each event, and backs the Actions window.</param>
    /// <param name="wizClient">The client WiZ actions send their commands through.</param>
    /// <param name="treadmillClient">The treadmill connection treadmill actions nudge.</param>
    /// <returns>The options to pass to <see cref="CompanionTrayApplication.Run"/>.</returns>
    internal static CompanionTrayAppOptions CreateOptions(ActionsConfigStore store, WizClient wizClient, TreadmillClient treadmillClient)
    {
        // Set via OnClientReady once the tray shell creates (or recreates, on a bot-host
        // change) its CompanionClient. Settings windows read this through a Func so they
        // always see the current instance, even one created after the window itself.
        CompanionClient? companionClient = null;

        return new CompanionTrayAppOptions
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
        };
    }
}
