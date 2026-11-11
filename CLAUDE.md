# CLAUDE.md

Guidance for working in this repo.

## What this repo is

`BCUK-Companion-ExpiredMinotaur` is the per-user companion app for
**ExpiredMinotaur**, built on top of the shared packages published by
[`BCUK-Companion-Core`](https://github.com/Battle-Cattle/BCUK-Companion-Core)
(`BCUKCompanion.Core` and `BCUKCompanion.TrayApp.Shell`). This repo
consumes those packages via `PackageReference` — it does not fork or
vendor the Core source. `src/BCUKCompanion.ExpiredMinotaur/Program.cs`
calls `CompanionTrayApplication.Run()` with a `DataFolderName` of
`BCUKCompanion.ExpiredMinotaur` (kept unique so this app's settings,
token storage, single-instance lock, and startup registry entry never
collide with another companion app on the same Windows account), and
`appsettings.json` holds the default bot host.

The rest of the first-party code is this app's own integrations:

- `Wiz/` — WiZ lights over the LAN (UDP): `WizClient`, the `wiz.*` action
  kinds, device dialogs and `WizActionEditor`.
- `Treadmill/` — an FTMS treadmill over Bluetooth LE: `TreadmillClient`,
  `treadmill.nudgeSpeed`, and `TreadmillActionEditor`.
- `ActionsSettingsWindow` / `ActionsConfigStore` — the "Actions..." tray
  window (a subclass of the Shell's `EventActionMappingsWindow`) and its
  `actions-config.json`.
- `tests/BCUKCompanion.ExpiredMinotaur.Tests` — xUnit tests for the
  integrations' logic. CI runs them in the Windows job.

## What belongs in Core instead

Anything every companion app would need goes in `BCUK-Companion-Core`, not
here: self-updating (`CompanionTrayAppOptions.Updates`), the Add/Edit Action
dialog (apps only supply `EventActionEditor`s), config persistence hooks,
and the release workflow (`release.yml` calls Core's reusable
`companion-release.yml`). Device integrations specific to this streamer's
setup stay in this repo.

## Building

This is a Windows app (WPF + WinForms via `BCUKCompanion.TrayApp.Shell`). It
only *runs* on Windows, but it compiles on Linux: `Directory.Build.props` sets
`EnableWindowsTargeting`, which fetches the Windows reference packs from NuGet
(without it, restore fails with `NETSDK1100`).

GitHub's automatic dependency submission is turned off for this repo. Its job
restores on Linux without the `PACKAGES_READ_PAT` secret, so the private feed
below answers 401. Dependabot still raises NuGet updates with its own credentials.

Restoring requires read access to the org's GitHub Packages feed
(`https://nuget.pkg.github.com/Battle-Cattle/index.json`, configured in
`NuGet.config`) — set `GITHUB_USERNAME` and `GITHUB_TOKEN` (a PAT with
`read:packages`) in the environment before `dotnet restore`. The default
`GITHUB_TOKEN` GitHub Actions provides only grants read access to
packages owned by *this* repo, not the separate `BCUK-Companion-Core`
repo that publishes `BCUKCompanion.Core`/`BCUKCompanion.TrayApp.Shell` —
CI instead uses a `PACKAGES_READ_PAT` repo secret (a classic PAT with
`read:packages` from an account with access to the org's packages).

### Building and testing on Linux

A sandbox often can't reach the GitHub Packages feed. Build against a sibling
checkout of `BCUK-Companion-Core` instead (`UseLocalCore`, which expects it at
`../BCUK-Companion-Core`), restoring from nuget.org only:

```bash
cat > /tmp/nuget.org.config <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
XML
dotnet restore --configfile /tmp/nuget.org.config -p:UseLocalCore=true
dotnet build --no-restore -p:UseLocalCore=true
```

Check out the Core version this repo pins (see Versioning), or the build tests
something other than what ships.

The tests target `net10.0-windows`, and Linux has no `Microsoft.WindowsDesktop.App`
runtime to run them on. None of them touch WPF types, though, so after the build
you can drop that framework from the test host's runtimeconfig and run them on the
base runtime:

```bash
f=$(find tests/BCUKCompanion.ExpiredMinotaur.Tests/bin -name '*.Tests.runtimeconfig.json')
python3 - "$f" <<'PY'
import json, sys
p = sys.argv[1]; c = json.load(open(p)); o = c["runtimeOptions"]
o["frameworks"] = [x for x in o.get("frameworks", []) if x["name"] != "Microsoft.WindowsDesktop.App"]
json.dump(c, open(p, "w"), indent=2)
PY
dotnet test tests/BCUKCompanion.ExpiredMinotaur.Tests --no-build
```

That's a local shortcut. CI's Windows job runs the tests for real.

## Versioning

`PackageReference` versions for `BCUKCompanion.Core` and
`BCUKCompanion.TrayApp.Shell` in
`src/BCUKCompanion.ExpiredMinotaur/BCUKCompanion.ExpiredMinotaur.csproj`
are pinned, not floating — bump them deliberately when picking up a new
Core release rather than letting NuGet auto-resolve newer versions.

---

## Code standards

These match `BCUK-Bot-4`'s `CLAUDE.md`, so every BCUK repo follows the same rules.

### Nullable and warnings

`<Nullable>enable</Nullable>` is on in every project. Don't silence a nullable warning with `!` unless a nearby check guarantees the value. When you do, add a comment that names the check. Don't add `#pragma warning disable` or `[SuppressMessage]` to make a build pass. Fix the cause.

### Complexity

Qlty reports function complexity, too many returns, deep nesting, long parameter lists and duplication (see PR Reviews (Qlty) below). Write code that stays under the limits from the start: cognitive complexity ≤ 15, cyclomatic complexity ≤ 12, at most 5 parameters, nesting depth ≤ 3. These are the same limits `BCUK-Bot-4` enforces with ESLint. When a method grows past them, extract helpers or a type.

### Docstrings

- Every type and member must have a `///` XML doc comment (`<summary>`) saying what it does. This covers public APIs, `internal`/`private` helpers, properties, constants and enum values. One line is enough for simple cases.
- Each parameter gets a `<param>` and a member that returns a value gets a `<returns>`, each with text. Say what the value is, not just its name: "Target belt speed in km/h", not "The speed." For a `Task`-returning method, say what the task completes with. A record's positional parameters are documented with `<param>` on the record.
- When you change a member's signature or behaviour, update its doc comment to match. Stale docs are worse than no docs.
- **Test projects are exempt.** A descriptive test method name documents the behaviour instead. Test-local fakes and helpers don't need doc comments either. Source projects still follow the rule above.
- **Concise lambdas** (LINQ selectors, event handlers wired inline, `Task.Run` bodies) don't need a doc comment. Add a short `//` comment only when the behaviour isn't obvious from the code.
- The build enforces this. `Directory.Build.props` and `.editorconfig` turn StyleCop's SA1600/SA1601/SA1602 (missing docs), SA1611/SA1615 (missing `<param>`/`<returns>`) and SA1614/SA1616 (empty ones) into errors (`stylecop.json` extends them to internal and private members), and a `<param>`, `<paramref>` or `<see cref>` that no longer matches the code fails the build too. Private fields are exempt. Use `/// <inheritdoc/>` on an override that adds nothing to its base's docs.

### Tests

- Every new function or behaviour change **must** include or update an xUnit test in `tests/BCUKCompanion.ExpiredMinotaur.Tests`, mirroring the source folder (e.g. `Treadmill/` → `Treadmill/`).
- When modifying existing behaviour, update affected tests before committing. Never leave a passing-but-wrong test.
- A fix for a bug or a review finding gets a test that fails without the fix.
- Keep logic you can test free of BLE, UDP and WPF, so it can be tested without hardware. For example, `TreadmillSpeedFollower` takes only the speeds and times it's given, `ControlPointService` writes through `IControlPointWriter`, `WizClient`'s reply parsing is in static methods, and `WizModuleInference` sits outside its dialog. `WizClient`'s socket code is tested against a fake device on a loopback port (`FakeWizDevice`).
- Windows and dialogs are tested too, on an STA thread through `WpfTest` (it shows dialogs modally and drives them once loaded). WPF only exists on Windows, so those tests are `[SkippableFact]`s that call `WindowsOnly.Skip()` first and keep every WPF type inside the lambda they pass to `WpfTest.Run`; CI's Windows job runs them. A window that opens a dialog takes a seam for showing it (e.g. `WizDevicesTab.ShowDialogWindow`) so a test can drive the dialog.
- The Bluetooth radio sits behind `ITreadmillDevice`, `IBleConnector`/`IBleLink`/`IGattCharacteristicPort` and `IAdvertisementWatcher`. Only their Windows implementations in `Treadmill/Ble/WinRtBle.cs`, which just forward to WinRT and need real hardware, are `[ExcludeFromCodeCoverage]`; keep every decision out of them.
- Logic that ends up in Core goes under Core's test rules.

## ALWAYS: Before committing

Build, then run only the tests for what you changed (on Linux, see "Building and testing on Linux"). CI's Windows job (`.github/workflows/dotnet.yml`) runs every test on each push, so don't repeat its full run locally:

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~BCUKCompanion.ExpiredMinotaur.Tests.Treadmill"   # the test namespace or class mirroring what changed
```

The build fails on a missing doc comment as well as a compile error, so always run it. **Run the full `dotnet test` instead** when the change touches something both integrations use (`ActionsConfigStore`, `ActionsSettingsWindow`, `Program.cs`, `Directory.Build.props`, the pinned Core version). The WPF tests skip on Linux, so CI's Windows job is the only real run for them.

**CI is the gate, not the local run.** A PR isn't done until CI's Windows job is green. If it fails, reproduce the failing test locally where the platform allows, fix it, and show it passing before pushing again. Bump `VERSION` when the change should ship (see the README's "Releasing").

## PR Reviews (Qlty)

Qlty's PR comments (code smells such as file/function complexity, too many returns, duplication, plus diff-coverage gaps) are this repo's standards, not optional suggestions. **Never dismiss, defer or resolve a Qlty finding without fixing it**, and never reply that it "stays as is" or belongs in a follow-up. Fix every finding in the same PR, whether it's an inline review comment or a row in the "new issues" or coverage summary:
- **Complexity, returns or structure:** refactor (extract helpers or a module) until the metric is under the threshold, keeping behaviour unchanged and tests green.
- **Uncovered new lines:** add tests that cover them.

After pushing, check that Qlty's next analysis no longer lists the finding.

## PR Reviews (CodeRabbit)

CodeRabbit reviews each push to a non-draft PR (marking a draft ready triggers one by itself). It's rate-limited per developer and **never retries on its own**. Only when a comment says "Review limit reached" with a wait time: wait that long, then post exactly one `@coderabbitai review` (only what changed since the last review), or `@coderabbitai full review` when the last Merge Risk was 🟡 Moderate or higher or the changes since are large. Any other "skipped" reason (draft, no new commits) needs no action.

**Merge Risk:** after every review, read the **Merge Risk** line in CodeRabbit's "📝 Walkthrough" comment, which is rewritten in place each time, not just the inline comments. A caveat under it is a real concern even at 🔵 Low: fix it, or post one PR comment saying why it's acceptable, before treating the PR as mergeable. After pushing a fix, say what the risk was and which commit fixes it in a comment tagging `@coderabbitai`; if the risk was 🟡 Moderate or higher, the re-review must be a `@coderabbitai full review`. Repeat until a review comes back with the risk resolved or explicitly accepted. [BCUK-Bot-4 PR #612](https://github.com/Battle-Cattle/BCUK-Bot-4/pull/612) shows the full pattern. This applies to any PR you're driving to green.

**Don't thrash:** never push while a review is in progress (it fails with "head commit changed"), and never re-trigger while rate-limited or in progress. A rate-limit reply only reports the remaining cooldown.
