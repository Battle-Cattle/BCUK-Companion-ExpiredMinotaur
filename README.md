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
- `.github/workflows/release.yml` — publishes a downloadable Windows build
  to a GitHub Release when a `v*` tag is pushed.

## Download

Grab the latest `BCUKCompanion.ExpiredMinotaur-vX.Y.Z-win-x64.zip` from the
[Releases page](https://github.com/Battle-Cattle/BCUK-Companion-ExpiredMinotaur/releases/latest),
extract it anywhere, and run `BCUKCompanion.ExpiredMinotaur.exe`. The build
is self-contained, so no .NET runtime install is needed (Windows 10 1809 or
later, x64).

## Releasing

Push a version tag and the `Release` workflow builds, zips, and attaches
the app to a new GitHub Release (with auto-generated notes):

```powershell
git tag v1.0.0
git push origin v1.0.0
```

You can also run the workflow manually from the Actions tab with an
already-pushed tag (e.g. to retry a failed run) — it builds that tag, not
the branch the run was started from. Tags with a suffix (e.g. `v1.1.0-beta.1`) are marked as pre-releases.
The tag's number is stamped into the app's assembly version.

## Building

Requires the .NET 8 SDK with the Windows desktop workload (Windows only —
see `BCUK-Companion-Core`'s `CLAUDE.md` for why this can't build on Linux).

1. Configure GitHub Packages access: set `GITHUB_USERNAME` and `GITHUB_TOKEN`
   (a PAT with `read:packages`, from an account with read access to the
   org's packages) in your environment — `NuGet.config` reads these for
   the `battle-cattle-github` feed. The default Actions `GITHUB_TOKEN`
   cannot read packages published by the separate `BCUK-Companion-Core`
   repo, so CI sources this from a `PACKAGES_READ_PAT` repo secret instead.
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
