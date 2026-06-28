using BCUKCompanion.TrayApp;

namespace BCUKCompanion.ExpiredMinotaur;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        CompanionTrayApplication.Run(new CompanionTrayAppOptions
        {
            DataFolderName = "BCUKCompanion.ExpiredMinotaur"
        });
    }
}
