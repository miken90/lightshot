# Phase 9: Update check, packaging and release

Status: done (2026-10-06) | Effort: 6d | Priority: P1 | Depends on: phases 1, 4, 5 (runs straight after phase 5)

## Overview

Ships the only network feature (update check), the installer and release tooling. Unpackaged Velopack (RULING) with an ECDSA-signed manifest verified before anything is applied. The update trust model is stricter than the Velopack default because releases are never Authenticode-signed (decision 2026-10-05).

## Requirements

- Update check: once per day plus at launch, anonymous HTTPS GET, no identifiers, honours a setting to disable. Source of truth is a `releases.json` manifest and its detached `.sig` hosted with the release assets. Manifest URL constant is `https://github.com/miken90/lightshot/releases/latest/download/releases.json` (resolved decision 2026-10-05).
- Verify-then-stage: download manifest and signature, verify ECDSA P-256 against a public key pinned in the app, download the package, verify its SHA-256 from the manifest, place both in a local staging folder, then hand the folder to Velopack as a local source to apply. This avoids a custom `IUpdateSource` and keeps the trust decision ours. The exact Velopack local-source API is verified in step 1 before any code is committed.
- Consent: the first launch never auto-updates; on the second and later launches an update is offered; apply on quit or on "Restart to update"; hold while `hasWorkInProgress` (editor with unsaved changes, recording, export). Tray shows a dot when an update is staged.
- Downgrade protection: manifest version must be greater than the running version; the manifest contains a monotonic `sequence`.
- Packaging: self-contained x64, ReadyToRun, no single-file trimming unless proven safe for WPF and Skia; `vpk pack` produces Setup.exe, nupkg and releases file; installer is per-user (no admin); shortcuts; uninstall removes the HKCU Run value and `%AppData%` only if the user chooses.
- `VelopackApp.Build().Run();` is the first line of `Main` in phase 1; phase 9 wires the hooks for install, update, uninstall (Run key cleanup).
- Size gate: `scripts/package.ps1 -MaxSetupMB` fails when exceeded. The MVP bundles no ML models (phases 6, 8, 10 deferred). Phase 1 measured an unsigned Setup.exe of 98 MB; keeping 15% headroom (98 * 1.15 = 112.7 MB), the `-MaxSetupMB` gate is set to **113 MB**. The gate no longer waits on Spike E.
- Builds are unsigned by decision. The README documents SmartScreen "More info -> Run anyway" and SHA256SUMS.
- Unpackaged execution: display capture uses DDA (borderless); window stills use `PrintWindow(PW_RENDERFULLCONTENT)`; unpackaged WGC window recording border is accepted as documented DEGRADE. Sparse package fallback deleted (requires trusted certificate, violating Q4).
- `NetworkPolicyTests`: the only code allowed to reference `HttpClient` or sockets is `Lightshot.Platform.Windows.Updates`.
- Release tooling `tools/Lightshot.ReleaseTool`: generates the manifest, signs it with the private key supplied by file path (never in the repo), verifies round trip. `scripts/release.ps1 -Publish` (refused without the flag) runs `gh release create v<ver> --repo miken90/lightshot` with assets: `LightshotApp-win-Setup.exe`, `LightshotApp-win-Portable.zip`, nupkg, `releases.json`, `releases.json.sig`, `SHA256SUMS.txt`. Manifest URL constant is `https://github.com/miken90/lightshot/releases/latest/download/releases.json`. The script checks the built artifact itself (version string in the exe, SHA-256, size against `-MaxSetupMB`, expected asset list); never re-runs tests (Boom rule 19). Publishing happens only after the user approves that release. `scripts/release.ps1 -DryRun` uses a throwaway key generated in the temp folder; the real private key is read only with `-Publish`.

## Data flow

```
daily timer -> UpdateChecker -> GET releases.json + .sig -> ManifestVerifier (ECDSA, sequence, version)
 -> GET package -> SHA-256 check -> StagingFolder -> UpdateState{staged}
 -> user restarts -> Velopack apply from staging -> relaunch
```

## Files

Create under `src/Lightshot.Platform.Windows/Updates/`: `UpdateChecker.cs`, `UpdateManifest.cs`, `ManifestVerifier.cs`, `PinnedKey.cs`, `UpdateStager.cs`, `VelopackApplier.cs`, `UpdateState.cs`, `UpdateScheduler.cs`.

Create under `src/Lightshot.App/`: `Startup/VelopackHooks.cs`, `Views/Settings/UpdatePane.xaml(.cs)`, `Views/Notices/UpdateNoticeController.cs`, tray dot change in `Tray/TrayMenu.cs`.

Create `tools/Lightshot.ReleaseTool/` (`Program.cs`, `ManifestWriter.cs`, `Signer.cs`, `Lightshot.ReleaseTool.csproj`), `scripts/release.ps1`, `scripts/keygen.ps1` (generates the signing keypair via the tool), `docs/release.md` (the why and where only).

Modify: `scripts/package.ps1` (size gate), `Directory.Packages.props` (Velopack), `THIRD-PARTY-NOTICES`, `README.md` (install, SmartScreen note).

Tests: `tests/Lightshot.Platform.Windows.Tests/{ManifestVerifierTests, UpdateCheckerTests, UpdateStagerTests, UpdateSchedulerTests}`, `tests/Lightshot.Architecture.Tests/NetworkPolicyTests.cs`, `tests/Lightshot.App.Tests/UpdatePolicyTests.cs`, `tests/Lightshot.App.UiTests/InstallerSmokeTests.cs`, `tests/Lightshot.ReleaseTool.Tests/RoundTripTests.cs`.

## Implementation steps

1. Read the installed Velopack version's API (local source, `UpdateManager`, hooks); record the exact call chain in the file header comment of `VelopackApplier.cs`. If a local-directory source does not exist, implement `IFileDownloader` with the staged files.
2. Define the manifest schema (`version`, `sequence`, `package`, `sha256`, `size`, `notesUrl`) with `version` field for schema.
3. Implement `ManifestVerifier` (pure, fully unit-testable with generated keys); reject any failure silently with a logged reason.
4. Implement checker, stager, scheduler with an injected clock and HTTP handler; no retry storms (backoff, once per day).
5. Update policy (second launch, work-in-progress hold) in a pure class.
6. Release tool and scripts; generate the keypair once, store the public key as a constant, document where the private key must live.
7. Packaging, size gate, install on a clean user profile in a Windows Sandbox or fresh VM when available, upgrade from the previous build.
8. Add `NetworkPolicyTests` (verifies only the Updates namespace references network types).
9. Documentation: `docs/release.md` covers key custody, rollback of a bad release, SmartScreen, and Smart App Control.

## Acceptance criteria

| Criterion | Test |
|---|---|
| Valid signature accepted, tampered manifest, wrong key, truncated signature rejected | `Lightshot.Platform.Windows.Tests.ManifestVerifierTests` (`Unit`) |
| SHA-256 mismatch rejects the package | `Lightshot.Platform.Windows.Tests.UpdateStagerTests.RejectsHashMismatch` (`Unit`) |
| Downgrade or equal sequence rejected | `Lightshot.Platform.Windows.Tests.ManifestVerifierTests.RejectsDowngradeAndReplay` (`Unit`) |
| Daily schedule, backoff, disabled setting makes zero requests | `Lightshot.Platform.Windows.Tests.UpdateSchedulerTests` and `UpdateCheckerTests.DisabledSettingSendsNothing` (`Unit`) |
| No identifiers sent in the request | `Lightshot.Platform.Windows.Tests.UpdateCheckerTests.RequestCarriesNoIdentifyingHeaders` (`Unit`) |
| Second-launch consent and work-in-progress hold | `Lightshot.App.Tests.UpdatePolicyTests` (`Unit`) |
| Only the Updates namespace references network types | `Lightshot.Architecture.Tests.NetworkPolicyTests.OnlyUpdatesNamespaceUsesNetwork` (`Unit`) |
| Release tool round trip signs and verifies | `Lightshot.ReleaseTool.Tests.RoundTripTests.SignedManifestVerifiesWithAppVerifier` (`Unit`) |
| Dry-run release builds, packs, verifies | `scripts/release.ps1 -DryRun` exit code, run by `Lightshot.App.UiTests.InstallerSmokeTests.DryRunSucceeds` (`Desktop`) |
| Installer size within the gate | `scripts/package.ps1 -MaxSetupMB` (script check; gate is 113 MB based on phase 1 measured 98 MB unsigned Setup.exe + 15% headroom; MVP bundles no ML models, no longer waits on Spike E) |
| Installed app starts, hotkey works, uninstall removes the Run value | `Lightshot.App.UiTests.InstallerSmokeTests.InstallLaunchUninstall` (`Desktop`, needs a clean user profile; UNCOVERED on hosts without a sandbox) |
| Real upgrade across two versions | UNCOVERED: needs two published builds; manual release rehearsal |
| SmartScreen behaviour | UNCOVERED: SmartScreen warns on every new release (expected; reputation is per file hash; unsigned by decision). README steps verified by manual release check |

## Rollback

Updating can be disabled by a setting and by removing the scheduler registration. Rollback: republish the last good code as a new, higher version (documented in `docs/release.md`; `UpdateManager` rejects downgrades and replays). Users who already applied a bad build can reinstall from the Setup file. The private key custody plan limits damage if the key leaks: ship a new app version with a new pinned key via an installer download.

## Risks

| Risk | L | I | Mitigation |
|---|---|---|---|
| Velopack local-source API differs from plan | M | M | Step 1 verification; custom downloader fallback |
| Private signing key lost or leaked | L | H | Offline custody, documented rotation by installer |
| SmartScreen warnings reduce installs | H | M | README install steps with screenshots of the SmartScreen dialog; portable zip as second asset; SHA256SUMS in release notes; in-app updates (no MOTW) |
| Smart App Control (enforcement mode) blocks unsigned apps with no reputation; there is no 'run anyway' | M | M | Accepted by Q4. README states that SAC users cannot run the app, and that turning SAC off is their own choice. Never advise it in-app |
| Installer exceeds size budget | M | M | ReadyToRun without trimming; installer stays under `-MaxSetupMB` (113 MB: 98 MB baseline + 15% headroom; ML models deferred post-MVP) |
| Update applies while recording | L | H | Work-in-progress hold test |
| Antivirus flags hooks plus updater | M | M | No network outside Updates; open-source build instructions |

## Dependencies

Runs after phase 4 and 5 (and can package phase 7): depends on phase 1 (scripts, package gate), phase 4 (tray, settings store), and phase 5 (Settings/About pane). Provides the update path for MVP public releases. Re-run size gate if/when post-MVP phases (6, 8, 10) are integrated.
