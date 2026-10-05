# Phase 9: Update check, packaging and release

Status: pending | Effort: 6d | Priority: P1 | Depends on: phases 1, 4, 5 (feature phases for the final size check)

## Overview

Ships the only network feature (update check), the installer and release tooling. Unpackaged Velopack (RULING) with a signed manifest verified before anything is applied. The update trust model is stricter than the Velopack default because the author may not have an Authenticode certificate.

## Requirements

- Update check: once per day plus at launch, anonymous HTTPS GET, no identifiers, honours a setting to disable. Source of truth is a `releases.json` manifest and its detached `.sig` hosted with the release assets (GitHub Releases URL is a settings constant; question in plan.md).
- Verify-then-stage: download manifest and signature, verify ECDSA P-256 against a public key pinned in the app, download the package, verify its SHA-256 from the manifest, place both in a local staging folder, then hand the folder to Velopack as a local source to apply. This avoids a custom `IUpdateSource` and keeps the trust decision ours. The exact Velopack local-source API is verified in step 1 before any code is committed.
- Consent: the first launch never auto-updates; on the second and later launches an update is offered; apply on quit or on "Restart to update"; hold while `hasWorkInProgress` (editor with unsaved changes, recording, export). Tray shows a dot when an update is staged.
- Downgrade protection: manifest version must be greater than the running version; the manifest contains a monotonic `sequence`.
- Packaging: self-contained x64, ReadyToRun, no single-file trimming unless proven safe for WPF and Skia; `vpk pack` produces Setup.exe, nupkg and releases file; installer is per-user (no admin); shortcuts; uninstall removes the HKCU Run value and `%AppData%` only if the user chooses.
- `VelopackApp.Run()` first in `Main` with hooks for install, update, uninstall (Run key cleanup).
- Size gate: `scripts/package.ps1 -MaxSetupMB` fails when exceeded; models and native runtimes counted (plan.md question on installer size).
- Authenticode is optional: `scripts/package.ps1 -SignParams` forwards to `vpk` when a certificate exists; without it SmartScreen warns, documented in the README.
- Sparse package (conditional): only if phase 4 concluded package identity is needed (for example for notification or tray behaviour); otherwise not built.
- `NetworkPolicyTests`: the only code allowed to reference `HttpClient` or sockets is `Lightshot.Platform.Windows.Updates`.
- Release tooling `tools/Lightshot.ReleaseTool`: generates the manifest, signs it with the private key supplied by file path (never in the repo), verifies round trip. `scripts/release.ps1 -DryRun` builds, packs, signs into a temp folder and verifies without publishing.

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

Modify: `scripts/package.ps1` (size gate, sign forwarding), `Directory.Packages.props` (Velopack), `THIRD-PARTY-NOTICES`, `README.md` (install, SmartScreen note).

Tests: `tests/Lightshot.Platform.Windows.Tests/{ManifestVerifierTests, UpdateCheckerTests, UpdateStagerTests, UpdateSchedulerTests}`, `tests/Lightshot.Architecture.Tests/NetworkPolicyTests.cs`, `tests/Lightshot.App.Tests/UpdatePolicyTests.cs`, `tests/Lightshot.App.UiTests/InstallerSmokeTests.cs`, `tests/Lightshot.ReleaseTool.Tests/RoundTripTests.cs`.

## Implementation steps

1. Read the installed Velopack version's API (local source, `UpdateManager`, hooks); record the exact call chain in the file header comment of `VelopackApplier.cs`. If a local-directory source does not exist, implement `IFileDownloader` with the staged files.
2. Define the manifest schema (`version`, `sequence`, `package`, `sha256`, `size`, `notesUrl`) with `version` field for schema.
3. Implement `ManifestVerifier` (pure, fully unit-testable with generated keys); reject any failure silently with a logged reason.
4. Implement checker, stager, scheduler with an injected clock and HTTP handler; no retry storms (backoff, once per day).
5. Update policy (second launch, work-in-progress hold) in a pure class.
6. Release tool and scripts; generate the keypair once, store the public key as a constant, document where the private key must live.
7. Packaging, size gate, install on a clean user profile in a Windows Sandbox or fresh VM when available, upgrade from the previous build.
8. Add `NetworkPolicyTests`, replacing the interim reference test from phase 6.
9. Documentation: `docs/release.md` covers key custody, rollback of a bad release, and SmartScreen.

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
| Installer size within the gate | `scripts/package.ps1 -MaxSetupMB` (script check; the gate is a number from the answer to the size question) |
| Installed app starts, hotkey works, uninstall removes the Run value | `Lightshot.App.UiTests.InstallerSmokeTests.InstallLaunchUninstall` (`Desktop`, needs a clean user profile; UNCOVERED on hosts without a sandbox) |
| Real upgrade across two versions | UNCOVERED: needs two published builds; manual release rehearsal |
| SmartScreen behaviour | UNCOVERED: depends on reputation and certificate; documented |

## Rollback

Updating can be disabled by a setting and by removing the scheduler registration. A bad release is withdrawn by publishing a manifest with a higher `sequence` pointing to the previous good package (documented in `docs/release.md`); users who already applied a bad build can reinstall from the Setup file. The private key custody plan limits damage if the key leaks: ship a new app version with a new pinned key via an installer download.

## Risks

| Risk | L | I | Mitigation |
|---|---|---|---|
| Velopack local-source API differs from plan | M | M | Step 1 verification; custom downloader fallback |
| Private signing key lost or leaked | L | H | Offline custody, documented rotation by installer |
| SmartScreen warnings reduce installs | H | M | Optional Authenticode; README note |
| Installer exceeds size budget | M | M | ReadyToRun without trimming; models dropped from the installer into optional pack (needs answer) |
| Update applies while recording | L | H | Work-in-progress hold test |
| Antivirus flags hooks plus updater | M | M | No network outside Updates; open-source build instructions |

## Dependencies

Phase 1 (scripts, package gate), phases 4 and 5 (settings, tray), final size check after phases 6 to 8. Blocks release only; phase 10 is optional and can ship after.
