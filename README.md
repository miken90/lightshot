# Lightshot Windows Port

A lightweight, modern Windows port of Lightshot screenshot utility built with .NET 10, SkiaSharp, and modern Windows 11 APIs.

## Host Prerequisites

| Prerequisite | Required | Verified on Host |
|---|---|---|
| Operating System | Windows 11 Build 22621+ | Windows 11 Build 26200.0 |
| Git for Windows | 2.40+ | git version 2.51.1.windows.1 |
| Windows Package Manager | winget | v1.29.380 |
| .NET 10 SDK | 10.0.100+ | 10.0.100 (installed per-user via `setup.ps1`) |
| Execution Policy | `RemoteSigned` (CurrentUser) | Configured via `setup.ps1` |

## Developing from WSL

Scripts target Windows PowerShell 5.1 and must be invoked on the NTFS path `D:\WORKSPACES\PERSONAL\lightshot` (never over `\\wsl$`).

When invoking from WSL bash:
- Always redirect stdin: `</dev/null`
- Strip Windows carriage returns: `2>&1 | tr -d '\r'`
- If wrapping in `bash -lc "..."`, escape `$LASTEXITCODE` as `\$LASTEXITCODE`

Example WSL invocation:
```bash
/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe -NoProfile -File 'D:\WORKSPACES\PERSONAL\lightshot\scripts\build.ps1' </dev/null 2>&1 | tr -d '\r'
```

## Setup & Scripts

All scripts reside in `scripts/` and return explicit exit codes.

### 1. Initial Setup (`scripts/setup.ps1`)
Idempotently configures `CurrentUser` execution policy to `RemoteSigned`, installs .NET 10 SDK per-user (without elevation) into `%LocalAppData%\Microsoft\dotnet` if missing, configures `DOTNET_ROOT` and `PATH`, restores local dotnet tools (`vpk`), and verifies model assets.

One-time bootstrap invocation:
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'D:\WORKSPACES\PERSONAL\lightshot\scripts\setup.ps1'
```

### 2. Build Pipeline (`scripts/build.ps1`)
Runs architectural purity checks (`check-core.ps1`), script encoding and syntax checks (`check-scripts.ps1`), builds `Lightshot.slnx` with warnings as errors, and optionally runs exit-code canary verification (`-SelfCheck`).

```powershell
powershell.exe -NoProfile -File 'D:\WORKSPACES\PERSONAL\lightshot\scripts\build.ps1' -SelfCheck
```

### 3. Test Runner (`scripts/test.ps1`)
Runs test suites filtered by trait tier. Results are exported to `artifacts/test-results/`.

```powershell
# Run default tiers (Unit, Render)
powershell.exe -NoProfile -File 'D:\WORKSPACES\PERSONAL\lightshot\scripts\test.ps1'

# Run Desktop tier tests (requires interactive console session)
powershell.exe -NoProfile -File 'D:\WORKSPACES\PERSONAL\lightshot\scripts\test.ps1' -Tier Desktop

# Run all tiers
powershell.exe -NoProfile -File 'D:\WORKSPACES\PERSONAL\lightshot\scripts\test.ps1' -Tier All
```

### 4. Running & Stopping (`scripts/run.ps1`)
Launches `Lightshot.App.exe` detached, writes the PID and path to `artifacts/run.pid`, and prevents duplicate instances. Stops gracefully via named event `Local\Lightshot.Quit` with fallback to `Stop-Process`.

```powershell
# Launch app detached
powershell.exe -NoProfile -File 'D:\WORKSPACES\PERSONAL\lightshot\scripts\run.ps1'

# Gracefully stop running app
powershell.exe -NoProfile -File 'D:\WORKSPACES\PERSONAL\lightshot\scripts\run.ps1' -Stop
```

### 5. Packaging (`scripts/package.ps1`)
Publishes a self-contained ReadyToRun `win-x64` application and bundles it into an unsigned Velopack installer package in `artifacts/release/`. Enforces package size limit (`-MaxSetupMB`).

```powershell
powershell.exe -NoProfile -File 'D:\WORKSPACES\PERSONAL\lightshot\scripts\package.ps1' -Version 0.1.0
```

### 6. Pinned Model Assets (`scripts/fetch-models.ps1`)
Downloads and validates AI/ML model assets against SHA-256 hashes defined in `assets/models.lock.json`.

```powershell
powershell.exe -NoProfile -File 'D:\WORKSPACES\PERSONAL\lightshot\scripts\fetch-models.ps1'
```

## Test Tiers

Tests are tagged with `[Tier("<Name>")]` trait attributes:

| Tier | Attribute | Description | Session Requirements |
|---|---|---|---|
| `Unit` | `[Unit]` | Fast, pure unit tests with zero system side effects. | Any |
| `Render` | `[Render]` | SkiaSharp rendering and rasterization golden tests. | Any |
| `Gpu` | `[Gpu]` | Direct3D 11 and DXGI hardware-accelerated tests. | Interactive |
| `Media` | `[Media]` | Audio and screen capture pipeline tests. Acquires display power request. | Interactive (non-Session 0) |
| `Desktop` | `[Desktop]` | FlaUI UI automation, system tray, and desktop session tests. | Interactive (non-Session 0, unlocked) |

To update golden test images when modifying rendering code:
```powershell
powershell.exe -NoProfile -File 'D:\WORKSPACES\PERSONAL\lightshot\scripts\test.ps1' -Tier Render -UpdateGoldens
```
*Always inspect git diffs of updated golden images before committing.*

## SmartScreen Notice for Unsigned Builds

Development and CI release builds produce unsigned Velopack installers (`artifacts/release/Lightshot-win-Setup.exe`). When launching on Windows 11:
1. Windows Defender SmartScreen may display: *"Windows protected your PC"*.
2. Click **More info**.
3. Click **Run anyway**.
