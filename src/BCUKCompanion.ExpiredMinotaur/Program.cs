using BCUKCompanion.ExpiredMinotaur.Wiz;
using BCUKCompanion.ExpiredMinotaur.Wiz.UI;
using BCUKCompanion.TrayApp;

namespace BCUKCompanion.ExpiredMinotaur;

internal static class Program
{
    internal const string DataFolderName = "BCUKCompanion.ExpiredMinotaur";

    [STAThread]
    private static void Main()
    {
        var store = new WizConfigStore(DataFolderName);
        var client = new WizClient();
        var dispatcher = new EventActionDispatcher(store.Load, client);

        CompanionTrayApplication.Run(new CompanionTrayAppOptions
        {
            DataFolderName = DataFolderName,
            OnBotEvent = e => Task.Run(() => dispatcher.DispatchAsync(e)),
            AdditionalMenuItems = new[]
            {
                new TrayMenuItem("Wiz Devices...", () => new WizSettingsWindow(store, client).Show()),
            },
        });
    }
}
