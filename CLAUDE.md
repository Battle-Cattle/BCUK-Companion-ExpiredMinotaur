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
  `actions-config.json`, including migration from the older per-integration
  config files.
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
(without it, restore fails with `NETSDK1100`). GitHub's automatic dependency
submission relies on that too: it restores every project on Linux.

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
- Keep logic you can test free of BLE, UDP and WPF, so it can be tested without hardware. For example, `TreadmillSpeedFollower` takes only the speeds and times it's given, `ControlPointService` writes through `IControlPointWriter`, `WizClient`'s reply parsing is in static methods, and `WizModuleInference` sits outside its dialog. Code that only talks to the device or draws a window (`FtmsDevice`, the UDP socket loop, the dialogs) is the exception.
- Logic that ends up in Core goes under Core's test rules.

## ALWAYS: Before committing

Build, then run the tests (on Linux, see "Building and testing on Linux"):

```bash
dotnet build && dotnet test
```

The build fails on a missing doc comment as well as a compile error. Let CI's Windows job confirm the result before treating the PR as green. Bump `VERSION` when the change should ship (see the README's "Releasing").

## PR Reviews (Qlty)

Qlty's PR comments (code smells such as file/function complexity, too many returns, duplication, plus diff-coverage gaps) are this repo's standards, not optional suggestions. **Never dismiss, defer or resolve a Qlty finding without fixing it**, and never reply that it "stays as is" or belongs in a follow-up. Fix every finding in the same PR, whether it's an inline review comment or a row in the "new issues" or coverage summary:
- **Complexity, returns or structure:** refactor (extract helpers or a module) until the metric is under the threshold, keeping behaviour unchanged and tests green.
- **Uncovered new lines:** add tests that cover them.

After pushing, check that Qlty's next analysis no longer lists the finding.

## PR Reviews (CodeRabbit)

CodeRabbit auto-reviews pushes but is rate-limited per developer; a rate-limited push gets a "Review limit reached" comment with a wait time and no actual diff review. **It never auto-retries** once the cooldown passes — a manual trigger is required every time, even if you just wait it out. After the wait time shown in the rate-limit comment has elapsed, post a PR comment:

- **`@coderabbitai review`** — reviews only what changed since the last review. Default choice.
- **`@coderabbitai full review`** — re-reviews the entire PR from scratch. Use this instead when the last review's **Merge Risk** (see below) was 🟡 Moderate or higher, when it surfaced a lot of issues, or when the intervening changes are large.

**The manual comment is ONLY for the rate-limit case above.** Do not post it reactively for other "review skipped" reasons:
- **"Review skipped: Draft detected"** — expected while the PR is a draft. Marking the PR ready for review (`draft: false`) auto-triggers a review on its own; no comment needed. Only manually trigger if, after going ready, no review shows up within a few minutes.
- **"No new commits to review since the last review"** — CodeRabbit is up to date; nothing to do.

If unsure which case a "skipped"/"no review" comment falls into, check whether it names a wait time (rate-limit → wait then trigger) or a different reason (draft/no-new-commits → no action).

**Merge Risk field:** CodeRabbit's main "📝 Walkthrough" comment is a living comment — every review (including the automatic first one) rewrites it in place, and it carries a **Merge Risk** line (e.g. `**Merge Risk:** _🔵 Low_ · up to \`a5ac9\``). Treat this as a required read, not decoration:
- After every review finishes, re-read the walkthrough comment for the current Merge Risk line and any caveat attached to it — don't stop at the inline/actionable comments.
- A caveat under the Merge Risk line is a real concern even when the badge is "Low" — CodeRabbit is flagging it, not blocking on it. Either fix it and push, or post one PR comment explaining why it's acceptable/out of scope, before treating the PR as mergeable.
- When you push a fix for a Merge Risk caveat, say so in a PR comment (what the risk was, which commit fixes it) tagging `@coderabbitai` so it lands in CodeRabbit's own thread and context — e.g. "@coderabbitai — fixed the race described above in `<commit>`...". This gives the next re-review and human reviewers the context, and lets CodeRabbit re-assess the same concern.
- When the Merge Risk was 🟡 Moderate or higher, the re-review after your fix must be a **`@coderabbitai full review`**, not an incremental `@coderabbitai review` — CodeRabbit acknowledging the fix in its thread (or marking the inline comments "Addressed") is not a re-review. If the push was rate-limited, wait out the cooldown and trigger the full review then.
- This can take more than one round: a fix can surface a narrower version of the same risk on re-review. Keep fixing and re-commenting (tagging `@coderabbitai` each time) until a review comes back with the risk resolved or explicitly accepted, not just re-labeled "Low" without comment.
- See BCUK-Bot-4 PR #612 (https://github.com/Battle-Cattle/BCUK-Bot-4/pull/612, "Forward all streamer events to the companion app") for the full pattern: an initial Low-risk walkthrough flagged a token-revocation race → fixed and commented → re-review found a narrower race in the same area → fixed and commented again → final re-review confirmed Low risk with the residual tradeoff explicitly accepted → only then marked ready for merge.
- This applies to any PR you're driving to green, not just ones you opened — a rising or unresolved Merge Risk is a signal to keep working the PR, not to wait on reviewers.

**Avoid re-triggering thrash:**
- **Never push a new commit while a review is in progress** — the in-flight review fails outright ("head commit changed during the review") and wastes the attempt. Wait for the review to finish (or fail/rate-limit) before pushing again.
- **Never re-trigger `@coderabbitai review` while already rate-limited or still in progress.** Each rate-limit reply just reports the currently remaining cooldown (it doesn't reset or extend it) — retrying early is simply wasted, not counterproductive. Wait out the countdown, trigger exactly once, then leave it alone until it either posts real findings or rate-limits again.
