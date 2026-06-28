# BCUK-Companion-ExpiredMinotaur

The companion app for the BCUK Bot, built for **ExpiredMinotaur**.

This repo is a thin Windows tray application that consumes the shared
[`BCUKCompanion.Core`](https://github.com/Battle-Cattle/BCUK-Companion-Core)
NuGet packages (`BCUKCompanion.Core` and `BCUKCompanion.TrayApp.Shell`).
It does not fork or vendor that repo's source — it depends on the packages
published to the org's GitHub Packages feed.

## Structure

- `src/BCUKCompanion.ExpiredMinotaur` — WinExe host project: `Program.cs`
  calls `CompanionTrayApplication.Run()` with a `DataFolderName` unique to
  this app, plus `appsettings.json` for the default bot host.
- `NuGet.config` — adds the `https://nuget.pkg.github.com/Battle-Cattle/index.json`
  feed alongside nuget.org.
- `.github/workflows/dotnet.yml` — restores and builds on `windows-latest`
  (the WPF/WinForms dependencies only build on Windows).

## Building

Requires the .NET 8 SDK with the Windows desktop workload (Windows only —
see `BCUK-Companion-Core`'s `CLAUDE.md` for why this can't build on Linux).

1. Configure GitHub Packages access: set `GITHUB_USERNAME` and `GITHUB_TOKEN`
   (a PAT with `read:packages`) in your environment — `NuGet.config`
   reads these for the `battle-cattle-github` feed.
2. Restore and build:

   ```powershell
   dotnet restore
   dotnet build
   ```

3. Run:

   ```powershell
   dotnet run --project src/BCUKCompanion.ExpiredMinotaur
   ```

## Configuration

Edit `src/BCUKCompanion.ExpiredMinotaur/appsettings.json` to set the bot
host this app talks to:

```json
{
  "botHost": "https://bot.example.com"
}
```

This is only the default — users can change the bot host from the tray
app's settings window, which persists to a per-app settings file under
`DataFolderName`.
