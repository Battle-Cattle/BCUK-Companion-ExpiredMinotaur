using System.Windows;
using BCUKCompanion.Core.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill;
using BCUKCompanion.ExpiredMinotaur.Treadmill.Actions;
using BCUKCompanion.ExpiredMinotaur.Treadmill.UI;
using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.Actions;
using BCUKCompanion.ExpiredMinotaur.Wiz.UI;
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

        var store = new WizConfigStore(DataFolderName, registry);
        var client = new WizClient();

        var treadmillStore = new TreadmillConfigStore(DataFolderName, registry);
        var treadmillClient = new TreadmillClient();
        var treadmillDispatcher = new EventActionDispatcher(
            () => treadmillStore.Load().Mappings,
            () => new TreadmillActionContext(treadmillClient));

        WizSettingsWindow? settingsWindow = null;
        TreadmillSettingsWindow? treadmillSettingsWindow = null;

        CompanionTrayApplication.Run(new CompanionTrayAppOptions
        {
            DataFolderName = DataFolderName,
            OnBotEvent = e =>
            {
                Task.Run(() =>
                {
                    var config = store.Load();
                    var dispatcher = new EventActionDispatcher(
                        () => config.Mappings,
                        () => new WizActionContext(client, config.Devices));
                    return dispatcher.DispatchAsync(e);
                });
                Task.Run(() => treadmillDispatcher.DispatchAsync(e));
            },
            AdditionalMenuItems = new[]
            {
                new TrayMenuItem("Wiz Devices...", () =>
                {
                    if (settingsWindow is null)
                    {
                        settingsWindow = new WizSettingsWindow(store, client);
                        settingsWindow.Closed += (_, _) => settingsWindow = null;
                        settingsWindow.Show();
                    }
                    else
                    {
                        if (settingsWindow.WindowState == WindowState.Minimized)
                        {
                            settingsWindow.WindowState = WindowState.Normal;
                        }
                        settingsWindow.Activate();
                    }
                }),
                new TrayMenuItem("Treadmill...", () =>
                {
                    if (treadmillSettingsWindow is null)
                    {
                        treadmillSettingsWindow = new TreadmillSettingsWindow(treadmillStore, treadmillClient);
                        treadmillSettingsWindow.Closed += (_, _) => treadmillSettingsWindow = null;
                        treadmillSettingsWindow.Show();
                    }
                    else
                    {
                        if (treadmillSettingsWindow.WindowState == WindowState.Minimized)
                        {
                            treadmillSettingsWindow.WindowState = WindowState.Normal;
                        }
                        treadmillSettingsWindow.Activate();
                    }
                }),
            },
        });
    }
}
