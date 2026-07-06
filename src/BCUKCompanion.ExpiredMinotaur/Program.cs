using System.Windows;
using BCUKCompanion.Core.Actions;
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

        var store = new WizConfigStore(DataFolderName, registry);
        var client = new WizClient();

        WizSettingsWindow? settingsWindow = null;

        CompanionTrayApplication.Run(new CompanionTrayAppOptions
        {
            DataFolderName = DataFolderName,
            OnBotEvent = e => Task.Run(() =>
            {
                var config = store.Load();
                var dispatcher = new EventActionDispatcher(
                    () => config.Mappings,
                    () => new WizActionContext(client, config.Devices));
                return dispatcher.DispatchAsync(e);
            }),
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
            },
        });
    }
}
