using System.Diagnostics;
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
        registry.Register<DelayAction>(DelayAction.ActionKind);

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
                }).ContinueWith(
                    t => Debug.WriteLine($"Wiz dispatch failed: {t.Exception}"),
                    TaskContinuationOptions.OnlyOnFaulted);
                Task.Run(() => treadmillDispatcher.DispatchAsync(e)).ContinueWith(
                    t => Debug.WriteLine($"Treadmill dispatch failed: {t.Exception}"),
                    TaskContinuationOptions.OnlyOnFaulted);
            },
            AdditionalMenuItems = new[]
            {
                CreateSettingsMenuItem("Wiz Devices...",
                    () => new WizSettingsWindow(store, client),
                    () => settingsWindow, w => settingsWindow = w),
                CreateSettingsMenuItem("Treadmill...",
                    () => new TreadmillSettingsWindow(treadmillStore, treadmillClient),
                    () => treadmillSettingsWindow, w => treadmillSettingsWindow = w),
            },
        });
    }

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
