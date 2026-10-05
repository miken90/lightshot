# Phase 1: Repo scaffold and Windows scripts

Status: pending | Effort: 2d | Priority: P1 | Depends on: none

## Overview

Create the solution, the four product projects, the test projects, and the `.ps1` scripts that make the whole port buildable and testable from WSL on a host that has git but no .NET SDK. Everything later runs through these scripts, so this phase is early and small. It ships an empty tray app that launches, plus a local unsigned installer.

## Requirements

- Scripts are invoked as `powershell.exe -NoProfile -File D:\WORKSPACES\PERSONAL\lightshot\scripts\<x>.ps1` from WSL, without an execution-policy flag.
- Scripts target Windows PowerShell 5.1: ASCII only, no `&&`, no ternary, no PS7 cmdlets.
- Build on the NTFS path `D:\...` only, never `\\wsl$`.
- `Lightshot.Core` is `net10.0` with zero `PackageReference` and no Windows API. `Lightshot.Rendering` is plain `net10.0` plus SkiaSharp.
- Test tiers are selectable: `Unit`, `Render`, `Gpu`, `Media`, `Desktop`.
- Process hygiene: `run.ps1` records the PID of what it starts and can stop it (SIGTERM-equivalent first, hard kill after a timeout).

## Data flow

`setup.ps1` (check or install SDK, restore tools, fetch models) -> `build.ps1` (restore, build all, warnings as errors) -> `test.ps1` (build, run xUnit v3 projects filtered by trait, write results to `artifacts/test-results/`) -> `run.ps1` (build, start app, write `artifacts/run.pid`) -> `package.ps1` (publish self-contained x64, `vpk pack`, size report). `check-core.ps1` runs the litmus and is called by `build.ps1`.

## Files

Create at repo root `D:\WORKSPACES\PERSONAL\lightshot\`:

| Path | Purpose |
|---|---|
| `Lightshot.slnx` | Solution (SDK 10 default format) |
| `global.json` | Pins SDK 10.0.x with `rollForward: latestFeature`; selects the Microsoft.Testing.Platform runner for `dotnet test` [UNVERIFIED: confirm the exact `test.runner` key on the installed SDK] |
| `Directory.Build.props` | `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, `LangVersion`, deterministic build, `InvariantGlobalization` off |
| `Directory.Packages.props` | Central package versions (SkiaSharp 3.x, HarfBuzzSharp, Vortice.*, CsWin32, NAudio, ZXing.Net, Microsoft.ML.OnnxRuntime, Whisper.net, Velopack, xunit.v3, FlaUI.UIA3) |
| `nuget.config` | nuget.org only, package source mapping |
| `.config/dotnet-tools.json` | `vpk` (Velopack CLI) |
| `.editorconfig`, `.gitignore`, `.gitattributes` | Style; ignore `artifacts/`, `assets/models/*.onnx|*.bin`; `*.ps1` kept LF-safe ASCII |
| `README.md` | Build, test, run commands from WSL; tier table |
| `AGENTS.md` | Project rules: core purity, goldens policy, script commands |
| `LICENSE`, `NOTICE`, `THIRD-PARTY-NOTICES.md` | Attribution duty (see plan.md) |
| `src/Lightshot.Core/Lightshot.Core.csproj` | `net10.0`, no packages |
| `src/Lightshot.Rendering/Lightshot.Rendering.csproj` | `net10.0`, SkiaSharp, HarfBuzzSharp, ref Core |
| `src/Lightshot.Platform.Windows/Lightshot.Platform.Windows.csproj` | `net10.0-windows10.0.22621.0`, CsWin32; `NativeMethods.txt`, `NativeMethods.json` |
| `src/Lightshot.App/Lightshot.App.csproj` | WPF exe, `app.manifest` (PerMonitorV2, Windows 11 supportedOS, `longPathAware`), `Program.cs` (Velopack `Run()` first line added in phase 9), `App.xaml` |
| `src/Lightshot.App/Program.cs`, `App.xaml.cs` | Single-instance mutex, shell thread stub, tray stub |
| `tests/Lightshot.TestSupport/` | `UnitAttribute`, `RenderAttribute`, `GpuAttribute`, `MediaAttribute`, `DesktopAttribute` (xUnit v3 trait attributes setting trait `Tier`) |
| `tests/Lightshot.Architecture.Tests/` | Assembly-reference and file-policy tests |
| `tests/Lightshot.Core.Tests/`, `tests/Lightshot.Rendering.Tests/`, `tests/Lightshot.Platform.Windows.Tests/`, `tests/Lightshot.App.Tests/` | Empty projects with one smoke test each |
| `tests/Lightshot.App.UiTests/` | FlaUI (UIA3) project, `Desktop` tier |
| `scripts/_common.ps1` | Repo root, PATH refresh, logging, exit-code helper |
| `scripts/setup.ps1` | Prerequisite check and install |
| `scripts/build.ps1`, `scripts/test.ps1`, `scripts/run.ps1`, `scripts/package.ps1` | As above |
| `scripts/check-core.ps1` | Core purity litmus |
| `scripts/check-scripts.ps1` | ASCII and PS5.1 syntax check of every script |
| `scripts/fetch-models.ps1`, `assets/models.lock.json` | Pinned model download with SHA-256 (models added in later phases; file created empty-valid here) |
| `assets/fonts/` | Inter static TTFs and `OFL.txt` (used from phase 3) |
| `assets/icons/` | Placeholder app icon (replaced in phase 5) |

## Implementation steps

1. Verify host prerequisites by hand once: `Get-ExecutionPolicy -Scope CurrentUser`, `winget --version`, Windows build >= 22621, `git --version`. Record in `README.md`.
2. Write `_common.ps1`: resolve repo root from `$PSScriptRoot`, refresh `$env:Path` from the registry after installs, `Invoke-Checked` that throws on non-zero exit.
3. Write `setup.ps1`:
   - If `Get-ExecutionPolicy -Scope CurrentUser` is `Restricted` or `Undefined`, set it to `RemoteSigned` (CurrentUser, no admin) and tell the user. Document the one-time bootstrap `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ...\setup.ps1`.
   - If `dotnet --list-sdks` has no `10.` line, run `winget install --id Microsoft.DotNet.SDK.10 -e --silent --accept-package-agreements --accept-source-agreements`. If winget fails or needs elevation, fall back to the official `dotnet-install.ps1` into `%LocalAppData%\Microsoft\dotnet` (per-user, no admin) and add it to the user PATH.
   - Run `dotnet tool restore`. Print a table of found versions. Exit non-zero if anything is missing. The script is idempotent.
4. Create the solution and projects. Wire references: Rendering -> Core; Platform.Windows -> Core; App -> Core, Rendering, Platform.Windows. Add `InternalsVisibleTo` for the matching test project only.
5. Write `check-core.ps1`: parse `Lightshot.Core.csproj` and fail on any `PackageReference`, `ProjectReference`, or a `TargetFramework` other than exactly `net10.0`; fail on source hits for `System.Windows`, `Windows.`, `Microsoft.Win32`, `System.Drawing`, `DllImport`, `LibraryImport`. Apply the same TFM and `Windows.` rules to `Lightshot.Rendering`, and its package list must be exactly SkiaSharp and HarfBuzzSharp. Also check that `NOTICE` exists.
6. Write `build.ps1` (`-Configuration`, `-NoRestore`): runs `check-core.ps1` and `check-scripts.ps1` first, then `dotnet build Lightshot.slnx`.
7. Write `test.ps1` with parameters `-Tier Unit|Render|Gpu|Media|Desktop|All` (default `Unit,Render`), `-Filter <wildcard on test name>`, `-NoBuild`, `-Configuration`. It maps the tier to the xUnit v3 trait filter `Tier=<name>`, forwards `-Filter`, and writes TRX or JSON to `artifacts/test-results/`. `Media` and `Desktop` first print the Windows session id and refuse to run in session 0. Add `-UpdateGoldens` (sets `LIGHTSHOT_UPDATE_GOLDENS=1`, only valid with `-Tier Render`, prints a reminder to review diffs). The exact xUnit v3 filter switches are [UNVERIFIED]; confirm against the installed `xunit.v3` and fall back to running each test project's exe directly if `dotnet test` cannot pass them.
8. Write `run.ps1` (`-Configuration`, `-Stop`): build, start `Lightshot.App.exe` detached, write the PID and command line to `artifacts/run.pid`; `-Stop` calls `CloseMainWindow`, waits 5 s, then `Stop-Process`. It refuses to start a second instance and reports the existing PID.
9. Write `package.ps1` (`-Version`, `-SkipVpk`, `-MaxSetupMB`): `dotnet publish` self-contained `win-x64` with ReadyToRun, then `dotnet vpk pack` unsigned into `artifacts/release/`; print file sizes and fail above `-MaxSetupMB`.
10. App stub: single-instance mutex `Local\Lightshot.SingleInstance`, a tray icon on the shell thread with Quit, and a "second launch activates the first" named event.
11. Write the architecture tests and smoke tests (below). Run `setup.ps1` twice, then `build.ps1`, `test.ps1`, `run.ps1`, `run.ps1 -Stop`, `package.ps1 -SkipVpk` from WSL.

## Acceptance criteria

| Criterion | Test or evidence |
|---|---|
| Core has no package, project or Windows reference | `Lightshot.Architecture.Tests.CoreAssemblyTests.HasNoPackageOrWindowsReferences` |
| Rendering has no Windows reference | `Lightshot.Architecture.Tests.RenderingAssemblyTests.HasNoWindowsReferences` |
| Every script is ASCII and parses on PS 5.1 | `Lightshot.Architecture.Tests.ScriptPolicyTests.AllScriptsAreAsciiAndParse` |
| NOTICE holds the upstream SHA and copyright | `Lightshot.Architecture.Tests.NoticeFileTests.ContainsUpstreamShaAndCopyright` |
| Tier filter selects only tagged tests | `Lightshot.TestSupport.Tests.TierTraitTests.UnitAttributeSetsTierTrait` (in `Lightshot.Core.Tests`, `Unit`) |
| Desktop tier runs in an interactive session | `Lightshot.Platform.Windows.Tests.DesktopSessionTests.RunsInInteractiveSession` (`Desktop`) |
| `build.ps1` and `test.ps1 -Tier Unit` succeed from WSL with exit code 0, and non-zero on a failing test | UNCOVERED by xUnit; verified by running the scripts once and once with a deliberately failing test (manual host check) |
| `setup.ps1` installs the SDK on a clean host and is idempotent on the second run | UNCOVERED: needs a machine without the SDK; manual host check on this laptop |
| `run.ps1` starts the app and `-Stop` ends it with no orphan | UNCOVERED: process lifecycle, manual host check; FlaUI smoke `Lightshot.App.UiTests.TrayStubTests.AppStartsAndQuitsFromTray` (`Desktop`) covers launch and quit |
| `package.ps1` produces a Setup.exe that installs per-user | UNCOVERED: installer UI, manual host check (automated in phase 9) |

## Rollback

Delete the scaffold commit series; nothing outside the repo is changed except the SDK, the user PATH entry and the CurrentUser execution policy, which `setup.ps1` logs so they can be reverted by hand (`winget uninstall Microsoft.DotNet.SDK.10`, `Set-ExecutionPolicy -Scope CurrentUser Undefined`).

## Risks

| Risk | L | I | Mitigation |
|---|---|---|---|
| Restricted execution policy blocks `-File` scripts | M | H | Policy check and one-time bootstrap documented in `setup.ps1` and README |
| winget needs elevation or a Store login | M | M | `dotnet-install.ps1` per-user fallback |
| xUnit v3 trait filter switches differ from assumption | M | L | Verified in step 7; direct exe fallback |
| WSL-started `powershell.exe` runs outside the logon session | L | H | Session probe test; Desktop tier refuses session 0 |
| Non-ASCII in a script corrupts under PS 5.1 | M | M | `check-scripts.ps1` in `build.ps1` |
| Antivirus quarantines unsigned `vpk` output | L | M | Output goes to `artifacts/`; document exclusion |

## Dependencies

None. Blocks every later phase. Spike probes (phase 2) live in `spikes/` and reuse `build.ps1`.
