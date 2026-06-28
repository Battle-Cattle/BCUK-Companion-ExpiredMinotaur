# CLAUDE.md

Guidance for working in this repo.

## What this repo is

`BCUK-Companion-ExpiredMinotaur` is the per-user companion app for
**ExpiredMinotaur**, built on top of the shared packages published by
[`BCUK-Companion-Core`](https://github.com/Battle-Cattle/BCUK-Companion-Core)
(`BCUKCompanion.Core` and `BCUKCompanion.TrayApp.Shell`). This repo
consumes those packages via `PackageReference` — it does not fork or
vendor the Core source. The only first-party code here is
`src/BCUKCompanion.ExpiredMinotaur/Program.cs`, which calls
`CompanionTrayApplication.Run()` with a `DataFolderName` of
`BCUKCompanion.ExpiredMinotaur` (kept unique so this app's settings,
token storage, single-instance lock, and startup registry entry never
collide with another companion app on the same Windows account), plus
`appsettings.json` for the default bot host.

## Building

This is a Windows-only app (WPF + WinForms via `BCUKCompanion.TrayApp.Shell`).
The `Microsoft.NET.Sdk.WindowsDesktop` MSBuild SDK is not available on
Linux/macOS — `dotnet build`/`dotnet restore` here will fail with
`MSB4236` outside Windows. Build/run on a real Windows machine or the
`windows-latest` GitHub Actions runner (`.github/workflows/dotnet.yml`).
Do not spend time trying to work around this on Linux.

Restoring requires read access to the org's GitHub Packages feed
(`https://nuget.pkg.github.com/Battle-Cattle/index.json`, configured in
`NuGet.config`) — set `GITHUB_USERNAME` and `GITHUB_TOKEN` (a PAT with
`read:packages`) in the environment before `dotnet restore`. The default
`GITHUB_TOKEN` GitHub Actions provides only grants read access to
packages owned by *this* repo, not the separate `BCUK-Companion-Core`
repo that publishes `BCUKCompanion.Core`/`BCUKCompanion.TrayApp.Shell` —
CI instead uses a `PACKAGES_READ_PAT` repo secret (a classic PAT with
`read:packages` from an account with access to the org's packages).

## Versioning

`PackageReference` versions for `BCUKCompanion.Core` and
`BCUKCompanion.TrayApp.Shell` in
`src/BCUKCompanion.ExpiredMinotaur/BCUKCompanion.ExpiredMinotaur.csproj`
are pinned, not floating — bump them deliberately when picking up a new
Core release rather than letting NuGet auto-resolve newer versions.
