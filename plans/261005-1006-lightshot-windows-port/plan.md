---
title: "Lightshot for Windows: full-parity native port"
description: "Ten-phase plan to port the macOS Lightshot app (screenshots, recording, Studio) to a native Windows 11 app on C#/.NET 10, WPF, Win32/DComp, D3D11 and SkiaSharp."
status: pending
priority: P1
effort: 128d
branch: main
tags: [windows, dotnet, wpf, port, capture, recording, studio, skia, direct3d]
blockedBy: []
blocks: []
created: 2026-10-05
---

# Lightshot for Windows: full-parity native port

## Outcome

A Windows 11 (build 22621+) per-user app, installed by Velopack, that matches the macOS Lightshot feature set: screenshots with annotation and redaction, OCR and QR, pin, Quick Access, history, recording with audio, camera, clicks and keystrokes, a video editor, and Studio with on-device captions. It is local-only except for one signed update check. The plan is planning only; no product code is written here.

## Reference inputs (all under `/home/canhnguyen/WORKSPACES/AI/boom/plans/reports/`)

| Short name | File | Used for |
|---|---|---|
| RULING | `xia-261005-0949-lightshot-windows-port-stack-ruling.md` | Accepted decisions, spikes, risks. Binding. |
| KIT | `xia-261005-0949-lightshot-windows-port-kit-anatomy.md` | Domain core modules, protocols, 41 test classes (~556 tests), constants. |
| APP | `xia-261005-0949-lightshot-windows-port-app-anatomy.md` | App shell files, macOS API matrix, settings keys, recording pipeline. |
| SPECS | `xia-261005-0949-lightshot-windows-port-specs-anatomy.md` | Feature areas, 19 invariants, 24 Windows risks. |

## Phases

| # | File | Usable slice shipped | Effort | Status | Depends on |
|---|---|---|---|---|---|
| 1 | [phase-01-scaffold-and-scripts.md](phase-01-scaffold-and-scripts.md) | Buildable, testable repo driven from WSL; empty tray app launches | 2d | pending | none |
| 2 | [phase-02-spikes-and-go-no-go.md](phase-02-spikes-and-go-no-go.md) | Five measured verdicts and a go/no-go record | 10d | pending | 1 |
| 3 | [phase-03-core-and-rendering.md](phase-03-core-and-rendering.md) | Tested domain library and deterministic `render()` | 12d | pending | 1, 2 (gate) |
| 4 | [phase-04-capture-editor-mvp.md](phase-04-capture-editor-mvp.md) | **First usable app**: hotkey, area/window/display capture, editor, copy/save | 18d | pending | 3 |
| 5 | [phase-05-pin-quickaccess-history-settings.md](phase-05-pin-quickaccess-history-settings.md) | Pin, Quick Access, history, settings, onboarding, light/dark, launch at login | 14d | pending | 4 |
| 6 | [phase-06-redaction-ocr-codes.md](phase-06-redaction-ocr-codes.md) | Auto Redact, OCR Text, QR/barcode | 9d | pending | 4 (5 for settings panes) |
| 7 | [phase-07-recording.md](phase-07-recording.md) | Video/GIF recording, audio, camera, input overlays, video editor | 25d | pending | 2, 4, 5 |
| 8 | [phase-08-studio.md](phase-08-studio.md) | Studio editor, auto zoom, cursor from data, backgrounds, captions, export | 25d | pending | 3, 7 |
| 9 | [phase-09-update-and-release.md](phase-09-update-and-release.md) | Signed update check, installer, release script | 6d | pending | 4 (5, 7, 8 for the full app) |
| 10 | [phase-10-translation.md](phase-10-translation.md) | OCR Translate window with local language packs | 7d | pending | 6, 9 |

Total: 128 working days for one engineer (about 26 weeks). Phases 5 and 6 may run in parallel after phase 4 only under the file-ownership table below.

### Order adjustments against the suggested order, and why

1. Phases 1 to 3 are foundations, not end-user slices. Each still ships a gated artefact (working scripts, a go/no-go record, a tested library). The first end-user slice is phase 4. This cannot be earlier because nothing user-visible exists before capture, overlay and editor are wired.
2. Manual redaction (blackout, blur, pixelate) is part of the editor and `render()`, so it ships in phase 4. Phase 6 adds only Auto Redact, OCR and QR/barcode.
3. `DesktopCover` (hide desktop icons) is built in phase 5 beside its setting and reused by recording in phase 7. It is not in phase 7 because screenshots use it too (SPECS 1.13).
4. The sparse-package work is conditional. It runs inside phase 4 (capture border) and phase 9 (install hook) only if the capture spike verdict requires it.
5. A local, unsigned installer is produced from phase 1 onward by `package.ps1`, so every phase is installable. Phase 9 adds signing, the update path and the release script.

## Source manifest and attribution duty

| Item | Value |
|---|---|
| Upstream | https://github.com/lethanhvietctt5/lightshot, branch `main`, SHA `b54a970924e10d5321465ba6ff4816b919afcc61` |
| Licence | MIT, "Copyright (c) 2026 Viet Le" |
| Fetch (read-only, into a scratch directory outside the repo) | `LightshotKit/Sources`, `LightshotKit/Tests`, `App/Sources`, `specs/`, `docs/adr/`, `docs/releases/`, `scripts/README.md`, `project.yml`, `README.md` |
| Out of scope | `website/` and specs 0017, 0018 (landing site) |
| Not in the analysis packs | `LightshotKit/Sources` bodies (reports only describe them), `HotkeyBindings.defaults`, `RecordingDefaults.standard`, `QuickAccessSettings` defaults, ADR 0001 text, entitlements in `project.yml`. Phase 3 reads these from the clone before porting. |

Derived code includes the ported Core, ported tests, algorithms and constants, and any converted icon or asset. The attribution duty is:

1. `LICENSE` keeps the upstream MIT text and the line "Copyright (c) 2026 Viet Le", plus a second line for the port author. The port licence is MIT (recommended, see questions).
2. `NOTICE` records the upstream repo, SHA and the list of derived directories. A unit test (`NoticeFileTests.ContainsUpstreamShaAndCopyright`) fails if either is missing.
3. Each ported source file starts with one comment line naming its upstream path and "MIT, (c) 2026 Viet Le".
4. `THIRD-PARTY-NOTICES.md` lists every dependency, bundled font, model and its licence (Inter OFL, YuNet MIT, Whisper MIT, ONNX Runtime MIT, ZXing Apache-2.0, OPUS-MT CC-BY, SoundTouch LGPL with dynamic link). The About pane links to it and to `LICENSE`.
5. Apple-specific artwork (SF Symbols, macOS cursor art, system sounds) is never copied. Replacements are original or openly licensed.

## Key decisions (binding; each cites RULING)

| Decision | Source |
|---|---|
| C#/.NET 10 LTS, WPF shell, raw Win32/DComp overlays, D3D11/D2D/MF via Vortice and CsWin32 | RULING §2 |
| `render()` and editor preview on SkiaSharp CPU raster, bundled font, own scalar deterministic blur | RULING §3 |
| Studio compositor and burn-in on D3D11 + D2D + DirectWrite; one `Render(state, t, target)` for preview and export | RULING §3 |
| Windows 11 only, min build 22621, tested on 26100 and 26200 | RULING §4 |
| Unpackaged Velopack; ECDSA-signed manifest plus SHA-256; sparse package if borderless capture fails; no full MSIX | RULING §5 |
| DDA for display and area recording; WGC for windows | RULING §5 |
| ML: `Windows.Media.Ocr`, ZXing.Net, YuNet ONNX, Whisper.net base q5 bundled, OPUS-MT last | RULING §6 |
| Hide notifications is detection plus guidance only; hide desktop icons is an `IDesktopWallpaper` cover window | RULING §6 |
| Global virtual-screen physical-pixel `CaptureRegion`; the source's primary-display rect-recording bug is fixed | RULING §7 |
| Projects `Lightshot.Core`, `Lightshot.Rendering`, `Lightshot.Platform.Windows`, `Lightshot.App`; xUnit v3 plus FlaUI; traits Unit, Render, Gpu, Media, Desktop | RULING §7 |

Decisions added by this plan (all reversible inside the named phase):

- **Thread model.** WPF runs on the main thread. One dedicated STA "shell thread" with its own Win32 message loop owns the tray icon, the hotkey window, the raw overlay windows and (separately, per use) the low-level hooks. Coordinators are `async Task` code over an `IUiDispatcher`; results cross threads through `TaskCompletionSource`. Phase 2's overlay spike validates this shape.
- **Frozen stills are raw pixels, not PNG.** `FrozenScreen` holds a BGRA pixel surface per display, so the overlay needs no decode and the 150 ms hotkey budget holds (the source avoided PNG on 5K for the same reason: APP §3). `CapturedImage` stays "PNG bytes plus pixel size" at the sink boundary for parity. The affected ported `FrozenScreenTests` are adapted deliberately.
- **Core cannot see Skia.** Core defines `IImageRenderer`, `IImageCodec` and `IThumbnailer`; Rendering implements them. The ~16 coordinator and history tests that call the real `render` or thumbnails move to `Lightshot.Rendering.Tests`.
- **Weak-entity detection is an interface.** Core keeps the scanner and an `IWeakEntityDetector` seam for phone, link and address. The implementation (libphonenumber-csharp plus regex) lives in Platform.Windows, so Core keeps zero packages.
- **JSON schemas are ours.** Settings, history index and Studio project use `System.Text.Json` with a `version` field and additive-only evolution. Byte-compatibility with Swift `Codable` output (and therefore opening a macOS `.lightshotstudio`) is not promised.
- **Settings in a JSON file** at `%AppData%\Lightshot\settings.json`, not the registry, so tests and backups are trivial. Launch at login is the only registry write (HKCU Run).
- **x64 only.** ARM64 was not requested and several native ML runtimes lack ARM64 builds.
- **No `AllowsTransparency` anywhere.** Full-screen chrome uses raw HWND plus DComp. Small windows (cards, pill, bubble) use WPF with DWM rounded corners.
- **Scripts target Windows PowerShell 5.1 and stay ASCII.** `powershell.exe` is 5.1; it reads BOM-less UTF-8 as ANSI. `scripts/check-scripts.ps1` enforces ASCII and no PS7-only syntax.
- **Build-time downloads only.** `scripts/fetch-models.ps1` downloads pinned models (SHA-256 in `assets/models.lock.json`) at developer setup. The shipped app downloads nothing.

## Process, data flow and tier summary

```
hotkey (WM_HOTKEY, shell thread)
  -> AppCoordinator (Core) -> ICaptureService.FreezeScreen (all monitors, BGRA, before any overlay)
  -> IOverlayController.SelectRegion (raw HWND + DComp per monitor, frozen backdrop)
  -> FrozenScreen.ImageOf(region)  -> CapturedImage
  -> editor (WPF, SKElement) -> AnnotationDocument commands -> Rendering.Render() -> ImageSink (clipboard/file/drag)
recording: DDA frames (GPU crop/scale/NV12) + WASAPI mic/loopback + hooks
  -> MF sink writer (fragmented MP4) -> remux -> Studio project (+ input.json, camera movie)
Studio: MF decode -> D3D11 -> StudioCompositor.Render(state, t, target) -> swapchain (preview) | MF sink writer (export)
```

| Tier trait | Runs headless | Notes |
|---|---|---|
| `Unit` | Yes, any OS for Core | ~450 ported cases plus new platform-logic tests |
| `Render` | Yes | ~105 re-baselined; exact for own pixel code, 1-2 LSB for Skia |
| `Gpu` | Yes (WARP) | Studio compositor goldens |
| `Media` | No (interactive desktop, real MF/hardware) | Encode, decode, recovery, ML quality |
| `Desktop` | No (interactive desktop) | Capture, overlays, hooks, hotkeys, FlaUI flows |

## File ownership across parallel work

| Area | Owner phase | Parallel rule |
|---|---|---|
| `src/Lightshot.Core/**` | 3 (port), later phases add only new files under their own folder | Never edit another phase's folder |
| `src/Lightshot.Rendering/**` | 3 | Later phases add files only |
| `src/Lightshot.Platform.Windows/{Capture,Overlay,Hotkeys,Tray,Clipboard}/` | 4 | |
| `src/Lightshot.Platform.Windows/{Settings,Startup,DesktopCover}/` | 5 | |
| `src/Lightshot.Platform.Windows/Ml/` | 6 (10 adds `Ml/Translation/`) | |
| `src/Lightshot.Platform.Windows/{Recording,Audio,Camera,Input,Media}/` | 7 | |
| `src/Lightshot.Platform.Windows/Studio/` | 8 | |
| `src/Lightshot.Platform.Windows/Updates/`, `tools/Lightshot.ReleaseTool/` | 9 | |
| `src/Lightshot.App/Views/Editor/**` | 4; phase 6 adds one file `AutoRedactController.cs` plus one registration line in `EditorWindow.xaml.cs`, merged by the phase-6 owner after phase 4 closes | Phases 5 and 6 never touch each other's Views folders |
| `src/Lightshot.App/Views/Settings/**` | 5; phase 7 adds `RecordingPane.*`, phase 8 `StudioPane` is not needed, phase 10 adds `TranslationPane.*` | New files only |

## Risk summary (full tables in each phase)

| Risk | Likelihood | Impact | Phase | Mitigation |
|---|---|---|---|---|
| Unpackaged borderless WGC or DDA fails on the hybrid-GPU laptop | Medium | High | 2, 4 | Capture spike; adapter-matched D3D device; sparse-package fallback |
| Real-time recording misses A/V sync or drops frames | Medium | High | 2, 7 | Recording spike with numeric gates; FFmpeg muxing-only fallback |
| Studio compositor in WPF cannot hold 60 fps or pixel-equals-export | Medium | High | 2, 8 | Compositor spike; `D3DImage` host before changing UI stack |
| Overlay latency, focus or `WH_KEYBOARD_LL` timeout | Medium | High | 2, 4, 7 | Overlay spike; hook thread only enqueues |
| OCR, Whisper, YuNet quality under bar | Medium | Medium | 2, 6, 8 | ML spike sets bundle budget; manual larger-model import |
| Pixel goldens drift across CPUs | Medium | Medium | 3 | Own scalar blur and scramble, exact goldens; Skia 1-2 LSB tolerance; two-machine check |
| Scope size (128d) | High | Medium | all | Each phase is releasable; Studio and Translation last |
| Execution policy, session 0 or WSL path problems running scripts | Medium | Medium | 1 | Policy check in `setup.ps1`; NTFS paths only; session probe test |
| Antivirus flags the low-level hook | Medium | Medium | 7 | Dedicated thread, no network, document; Authenticode if bought |

## Rollback strategy

The repo is greenfield, so each phase is one reviewable commit series that can be reverted with its merge. Persisted data is additive-only (new JSON keys with defaults), so a revert never strands user data. Phase-specific notes are in each file. A feature that is only partly proven (Studio, Translation) ships behind a settings flag defaulting off until its acceptance tests pass.

## Success criteria (global)

- All ~450 pure tests pass on plain `net10.0` with zero Windows or package references in Core (`CoreAssemblyTests.HasNoPackageOrWindowsReferences`).
- Render goldens pass on two different machines.
- Hotkey-to-overlay under 150 ms; recording meets the recording spike targets on the host laptop; Studio preview pixels equal export pixels on WARP.
- No network traffic except the signed update check (`NetworkPolicyTests`, plus a firewall-log check in phase 9).

## Unresolved questions

1. Store channel wanted in addition to direct download? (RULING recommends direct only; a Store build would add an MSIX build.)
2. Installer size budget? The Whisper base model alone is about 60 MB; `package.ps1` fails above `-MaxSetupMB` (default 300) until answered.
3. Which translation language pairs to bundle at launch (one or two)?
4. Will Authenticode signing be purchased (for example Azure Artifact Signing), or are SmartScreen warnings accepted?
5. Default hotkeys replacing Cmd+Ctrl+3 and Cmd+Ctrl+4. Recommend PrintScreen variants (PrintScreen = area, Ctrl+PrintScreen = fullscreen), after the overlay spike confirms the OS PrintScreen claim can be taken over.
6. Font replacing Helvetica. Recommend bundled Inter (OFL).
7. Port licence. Recommend MIT with the original attribution kept.
8. OCR recall gate: no macOS host exists to measure the Vision baseline. Recommend measuring recall against ground truth of rendered fixtures instead. Acceptable?
