# BCUK-Companion-ExpiredMinotaur

The companion app for the BCUK Bot, built for **ExpiredMinotaur**.

This repo is a Windows tray application that consumes the shared
[`BCUKCompanion.Core`](https://github.com/Battle-Cattle/BCUK-Companion-Core)
NuGet packages (`BCUKCompanion.Core` and `BCUKCompanion.TrayApp.Shell`).
It does not fork or vendor that repo's source — it depends on the packages
published to the org's GitHub Packages feed.

## Structure

- `src/BCUKCompanion.ExpiredMinotaur` — WinExe host project: `Program.cs`
  calls `CompanionTrayApplication.Run()` with a `DataFolderName` unique to
  this app, plus `appsettings.json` for the default bot host. The rest is
  this app's own integrations: `Wiz/` (WiZ lights over the LAN) and
  `Treadmill/` (an FTMS treadmill over Bluetooth LE), each with its action
  kinds and an action editor for the shared Add/Edit Action dialog, and
  `ActionsSettingsWindow`/`ActionsConfigStore` for the "Actions..." window
  and its config file.
- `NuGet.config` — adds the `https://nuget.pkg.github.com/Battle-Cattle/index.json`
  feed alongside nuget.org.
- `.github/workflows/dotnet.yml` — restores and builds on `windows-latest`
  (the WPF/WinForms dependencies only build on Windows).
- `.github/workflows/release.yml` — publishes a downloadable Windows build
  to a GitHub Release when `VERSION` changes on `main` (or a `v*` tag is
  pushed), by calling `BCUK-Companion-Core`'s shared `companion-release.yml`.
- `VERSION` — the app version the next release on `main` publishes.

## Download

Grab the latest `BCUKCompanion.ExpiredMinotaur-vX.Y.Z-win-x64.zip` from the
[Releases page](https://github.com/Battle-Cattle/BCUK-Companion-ExpiredMinotaur/releases/latest),
extract it anywhere, and run `BCUKCompanion.ExpiredMinotaur.exe`. The zip holds
just that `.exe` and `appsettings.json`: it's a self-contained single-file build,
so no .NET runtime install is needed (Windows 10 1809 or later, x64). Installs
updated from an older multi-file release delete that release's leftover files
on their next launch.

### Updates

The app updates itself (via the shared tray shell's updater): on launch it
checks the latest GitHub release and, if it's newer, downloads it, verifies
it against the release's checksum, restarts, and carries on. A tray balloon shows
while this happens. You can also pick **Check for updates...** from the tray
menu. Settings and login are stored under your user profile, not the app
folder, so they survive updates. The app folder must be writable, so don't
extract it under `Program Files`. Local builds (version `0.0.0`) never
update themselves.

## Releasing

Bump the version in `VERSION` (e.g. `1.0.0` → `1.1.0`) and merge to `main`.
The `Release` workflow then tags the merge commit `v1.1.0`, builds the app,
zips it and attaches it (with a `.sha256` checksum) to a new GitHub Release
with auto-generated notes.
Installed copies pick it up on their next launch. Merging a `VERSION` that
already has a release does nothing.

You can also push a tag yourself (`git tag v1.1.0 && git push origin v1.1.0`),
or run the workflow manually from the Actions tab with an already-pushed tag
(e.g. to retry a failed run). A manual run builds that tag, not the branch
the run was started from. Versions with a suffix (e.g. `1.1.0-beta.1`) are
marked as pre-releases, and the in-app updater skips them. The version is
stamped into the app's assembly version.

## Building

Requires the .NET 10 SDK with the Windows desktop workload (Windows only —
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
