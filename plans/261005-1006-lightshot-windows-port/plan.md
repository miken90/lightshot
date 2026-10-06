---
title: "Lightshot for Windows: full-parity native port"
description: "Ten-phase plan to port the macOS Lightshot app (screenshots, recording, Studio) to a native Windows 11 app on C#/.NET 10, WPF, Win32/DComp, D3D11 and SkiaSharp."
status: pending
priority: P1
effort: 92-104d (MVP; was 160-180d, 55-66d deferred post-MVP)
branch: main
tags: [windows, dotnet, wpf, port, capture, recording, studio, skia, direct3d]
blockedBy: []
blocks: []
created: 2026-10-05
---

# Lightshot for Windows: native port (MVP: screenshot + recording)

## Outcome

A Windows 11 (build 22621+) per-user app, installed by Velopack, focused on an MVP of high-performance screenshots with annotation and manual redaction, and screen recording with audio and input overlays. Advanced features (Auto Redact, OCR/QR, Studio compositor, Translation, camera bubble) are preserved in the roadmap as a post-MVP backlog. It is local-only except for one ECDSA-verified update check. The plan is planning only; no product code is written here.

## MVP scope (user, 2026-10-05)

Scope narrows from full parity to an MVP focused on screenshot and recording. Auxiliary features are noted as post-MVP and are not built now.

**IN the MVP:**
- **Phase 1** (scaffold and scripts, complete — done): buildable/testable repo driven from WSL, packaging and check scripts.
- **Phase 2** (spikes and go/no-go gate, complete — done): five measured verdicts and the go/no-go record (`reports/spike-gate.md`).
- **Phase 3** (core and rendering): tested domain library and deterministic `render()` only for what MVP features use (28 classes, 451 ported tests; deferred types excluded).
- **Phase 4** (capture & editor MVP, complete): hotkey, area/window/display capture, annotation editor, manual redaction (blackout, blur, pixelate), copy/save.
- **Phase 5** (pin, Quick Access, history, settings, complete): pin, Quick Access, history, settings, onboarding, light/dark, launch at login, hide desktop icons.
- **Phase 7** (recording, complete WITHOUT camera bubble): DDA/WGC capture to MP4 (H.264 + AAC, faststart), mic + system audio, pause, countdown, click and keystroke overlays (input hooks + burn-in), GIF conversion, video editor (trim / convert), crash recovery, hide notifications guidance, display kept awake.
- **Phase 9** (update and release, complete): ECDSA-verified auto-update, installer, release script.

**POST-MVP (deferred; files kept, marked deferred, content preserved):**
- **Phase 6** ([phase-06-redaction-ocr-codes.md](phase-06-redaction-ocr-codes.md)): Auto Redact, OCR Text capture, QR/barcode.
- **Phase 8** ([phase-08-studio.md](phase-08-studio.md)): Studio editor, auto zoom, cursor from data, backgrounds, captions, export.
- **Phase 10** ([phase-10-translation.md](phase-10-translation.md)): Translation window with bundled en<->vi and local language packs.
- **Camera bubble** (Phase 7 package R4, `ICameraService`/`CameraBubble` and associated criteria/tests).

## Reference inputs (all under `/home/canhnguyen/WORKSPACES/AI/boom/plans/reports/`)

| Short name | File | Used for |
|---|---|---|
| RULING | `xia-261005-0949-lightshot-windows-port-stack-ruling.md` | Accepted decisions, spikes, risks. Binding. |
| KIT | `xia-261005-0949-lightshot-windows-port-kit-anatomy.md` | Domain core modules, protocols, 41 test classes (595 tests across 41 classes; authoritative count from cloned manifest), constants. |
| APP | `xia-261005-0949-lightshot-windows-port-app-anatomy.md` | App shell files, macOS API matrix, settings keys, recording pipeline. |
| SPECS | `xia-261005-0949-lightshot-windows-port-specs-anatomy.md` | Feature areas, 19 invariants, 24 Windows risks. |

## Phases

| # | File | Usable slice shipped | Effort | Status | Depends on |
|---|---|---|---|---|---|
| 1 | [phase-01-scaffold-and-scripts.md](phase-01-scaffold-and-scripts.md) | Buildable, testable repo driven from WSL; empty tray app launches | 3d | done | none |
| 2 | [phase-02-spikes-and-go-no-go.md](phase-02-spikes-and-go-no-go.md) | Five measured verdicts and a go/no-go record (`reports/spike-gate.md`) | 13-15d | done | 1 |
| 3 | [phase-03-core-and-rendering.md](phase-03-core-and-rendering.md) | Tested domain library and deterministic `render()` (MVP subset: 28 classes, 451 tests) | 12d | done | 1, 2 (spikes A, D) |
| 4 | [phase-04-capture-editor-mvp.md](phase-04-capture-editor-mvp.md) | **First usable app**: hotkey, area/window/display capture, editor, copy/save | 18d | done | 3, 2 (spikes A, D) |
| 5 | [phase-05-pin-quickaccess-history-settings.md](phase-05-pin-quickaccess-history-settings.md) | Pin, Quick Access, history, settings, onboarding, light/dark, launch at login | 14d | done | 4 |
| 7 | [phase-07-recording.md](phase-07-recording.md) | Video/GIF recording, audio, input overlays, video editor (without camera bubble) | 26-35d | done | 2 (spike B), 4, 5 |
| 9 | [phase-09-update-and-release.md](phase-09-update-and-release.md) | ECDSA-verified update check, installer, release script (first public release has updater) | 6d | pending | 1, 4, 5 |
| 6 | [phase-06-redaction-ocr-codes.md](phase-06-redaction-ocr-codes.md) | Auto Redact, OCR Text, QR/barcode | 9d | deferred (post-MVP) | 4, 5, 2 (spike E) |
| 8 | [phase-08-studio.md](phase-08-studio.md) | Studio editor, auto zoom, cursor from data, backgrounds, captions, export | 35-45d | deferred (post-MVP) | 3, 2 (spikes C, E), 7 |
| 10 | [phase-10-translation.md](phase-10-translation.md) | OCR Translate window with bundled en<->vi and local language packs | 7d | deferred (post-MVP) | 2 (spike E), 6, 9 |

*Note on Phase 2: All five spikes are measured and documented in `reports/spike-*.md`. The Phase 2 go/no-go gate is recorded in [reports/spike-gate.md](reports/spike-gate.md) with an overall verdict of GO for the MVP.*

### MVP Effort Arithmetic

- **Phase 1 (Scaffold & Scripts)**: 3d (done)
- **Phase 2 (Spikes & Gate)**: 13–15d (done)
- **Phase 3 (Core & Rendering MVP)**: 12d
- **Phase 4 (Capture & Editor MVP)**: 18d
- **Phase 5 (Pin, Quick Access, History, Settings)**: 14d
- **Phase 7 (Recording without Camera)**: 26–35d (was 30–40d; camera package R4 deferred post-MVP, -4 to -5d)
- **Phase 9 (Update & Release)**: 6d
- **Total MVP Effort**: 3 + 13 + 12 + 18 + 14 + 26 + 6 = **92d** (min) to 3 + 15 + 12 + 18 + 14 + 35 + 6 = **104d** (max), or **92–104 working days** (~18–21 weeks for one engineer).

**Deferred Post-MVP Effort:**
- **Phase 6 (Auto Redact, OCR, QR/Barcode)**: 9d
- **Phase 8 (Studio Compositor & Editor)**: 35–45d
- **Phase 10 (Translation)**: 7d
- **Camera Bubble (Phase 7 Package R4)**: 4–5d
- **Total Deferred Effort**: 9 + 35 + 7 + 4 = **55d** (min) to 9 + 45 + 7 + 5 = **66d** (max).

Original 10-phase full scope was 160–180 working days. Rescoping to the user-decided MVP reduces effort by 55–66d (~40%), focusing delivery on standalone screenshots and screen recording.

### Order adjustments against the suggested order, and why

1. Phases 1 to 3 are foundations, not end-user slices. Each still ships a gated artefact (working scripts, a go/no-go record, a tested library). The first end-user slice is phase 4. This cannot be earlier because nothing user-visible exists before capture, overlay and editor are wired.
2. Manual redaction (blackout, blur, pixelate) is part of the editor and `render()`, so it ships in phase 4. Phase 6 adds only Auto Redact, OCR and QR/barcode, which are deferred to post-MVP.
3. `DesktopCover` (hide desktop icons) is built in phase 5 beside its setting and reused by recording in phase 7. It is not in phase 7 because screenshots use it too (SPECS 1.13).
4. Sparse-package fallback deleted (requires trusted signature, breaking Q4). Fallback is DDA for display capture (no border exists); for window stills PrintWindow(PW_RENDERFULLCONTENT); if WGC still shows a border unpackaged, accept it for window recording only and document a DEGRADE.
5. A local, unsigned installer is produced from phase 1 onward by `package.ps1`, so every phase is installable. Phase 9 provides the ECDSA-verified update path and release tooling for the MVP release. Its size gate is re-anchored to 113 MB (phase 1 measured 98 MB unsigned Setup.exe + 15% headroom) without waiting on Spike E or ML models.
6. Phase 7 (recording without camera) runs after Phase 5; no MVP phase depends on deferred phases 6, 8, or 10. Phase 9 can package and release either immediately after Phase 5 (screenshot-only release) or after Phase 7 (screenshot + recording release).

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
| Windows 11 only, min build 22621, tested on 26200 (26100 UNCOVERED or covered on windows-latest CI runner) | RULING §4 |
| Unpackaged Velopack; ECDSA-signed manifest plus SHA-256; no sparse package (requires signing); DDA for borderless display capture, PrintWindow/DEGRADE fallback for windows; no full MSIX | RULING §5 |
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
| `src/Lightshot.App/Views/Editor/**` | 4; phase 6 adds one file `AutoRedactController.cs` plus one registration line in `EditorWindow.xaml.cs`, merged by the phase-6 owner after phase 4 closes | Phases 5 and 6 run in sequence; never touch each other's Views folders |
| `src/Lightshot.App/Views/Settings/**` | 5; phase 7 adds `RecordingPane.*`, phase 8 `StudioPane` is not needed, phase 10 adds `TranslationPane.*` | New files only |

## Risk summary (full tables in each phase)

| Risk | Likelihood | Impact | Phase | Mitigation |
|---|---|---|---|---|
| Unpackaged borderless WGC or DDA fails on the hybrid-GPU laptop | Medium | High | 2, 4 | Capture spike; adapter-matched D3D device; DDA for displays, PrintWindow for stills, accept WGC window border as DEGRADE |
| Real-time recording misses A/V sync or drops frames | Medium | High | 2, 7 | Recording spike with numeric gates; loopback audio stamped against render endpoint clock; FFmpeg muxing-only fallback |
| Overlay latency, focus or `WH_KEYBOARD_LL` timeout | Medium | High | 2, 4, 7 | Overlay spike (74.8 ms achieved); hook thread only enqueues |
| Pixel goldens drift across CPUs | Medium | Medium | 3 | Own scalar blur and scramble, exact goldens; Skia 1-2 LSB tolerance; two-machine check or windows-latest CI runner |
| Scope size (92–104d MVP; was 160–180d) | Medium | Medium | all | Rescoped to user-decided MVP (screenshot + recording); 55–66d deferred post-MVP |
| Execution policy, session 0 or WSL path problems running scripts | Medium | Medium | 1 | Policy check in `setup.ps1`; NTFS paths only; session probe test |
| Antivirus flags the low-level hook | Medium | Medium | 7 | Hooks installed only while a recording runs; submit each release to the Microsoft Defender false-positive portal; scan with VirusTotal before publishing; open-source build instructions |

*(Note: Risks concerning deferred work — Studio compositor 60 fps / pixel export, and ML model quality/cost — are moved to the Post-MVP backlog below).*

## Rollback strategy

The repo is greenfield, so each phase is one reviewable commit series that can be reverted with its merge. Persisted data is additive-only (new JSON keys with defaults), so a revert never strands user data. Phase-specific notes are in each file.

## Success criteria (global MVP)

- All 451 ported MVP tests (28 classes) pass on plain `net10.0` with zero Windows or package references in Core (`CoreAssemblyTests.HasNoPackageOrWindowsReferences`; 144 tests across 13 classes deferred post-MVP).
- Render goldens pass on two different machines.
- Hotkey-to-overlay under 150 ms (Spike D proved 74.8 ms median); recording meets the recording spike targets on the host laptop (audio loopback stamped against render device clock, acoustic validation, stripping MF `mfra` per Spike B gate decision).
- No network traffic except the ECDSA-verified update check (`NetworkPolicyTests`, plus a firewall-log check in phase 9; note: `releases/latest/download` redirects to `objects.githubusercontent.com`, so network check must allow both hosts).

## Resolved decisions (2026-10-05)

- direct download, GitHub Releases on `miken90/lightshot`;
- Whisper base q5 bundled (deferred to post-MVP Phase 8);
- en<->vi translation (deferred to post-MVP Phase 10);
- never sign;
- Windows 11 22621+ only;
- PrintScreen = area, Ctrl+PrintScreen = fullscreen, with Snipping Tool claim detection;
- fix the primary-display rect-recording bug;
- bundled Inter font;
- MIT licence keeping "(c) 2026 Viet Le";
- OCR recall measured against rendered-fixture ground truth.

## Post-MVP backlog

Deferred features retained in the project roadmap, with their phase files, required spike results, deferred risk rows, and deferred success criteria:

| Feature | Phase File | Effort | Spike Prerequisite & Status | Core / Ported Tests Deferred |
|---|---|---|---|---|
| **Auto Redact, OCR Text, QR/Barcode** | [phase-06-redaction-ocr-codes.md](phase-06-redaction-ocr-codes.md) | 9d | Spike E: OCR strict recall failed (67.4% vs 90% bar; needs per-line 3x retry + binarization or alternative OCR engine); YuNet passed on prominent faces (91.5%). | `AutoRedactTests` (30), `TextCaptureTests` (14), `SensitiveDataScanner`, `IWeakEntityDetector`, `TextRecognition` |
| **Studio Editor & Captions** | [phase-08-studio.md](phase-08-studio.md) | 35–45d | Spike C: HwndHost selected; seek max <=150 ms and 60 fps source-skip mitigation become phase 8 acceptance items.<br>Spike E: Whisper captions passed (median 145 ms, RTF 0.058). | 10 Studio test classes (93 tests: Annotation, Caption, Capture, Document, Engine, ExportBitRate, ExportFrameRate, Flatten, Snap, TrimSpeed); `StudioDocument`, `StudioCaptions`, `CursorPath`, `ZoomCamera`, etc. |
| **Translation** | [phase-10-translation.md](phase-10-translation.md) | 7d | Spike E: OPUS-MT passed (en->vi chrF 57.1, vi->en chrF 65.8, p50 < 65 ms). | Depends on Phase 6 OCR Text capture and Phase 9 packaging. |
| **Camera Bubble & Camera Movie** | [phase-07-recording.md](phase-07-recording.md) (Post-MVP section) | 4–5d | Spike B passed capture/encode; camera hardware feed and MF source reader deferred. | `CameraBubbleTests` (7), `CameraBubble.cs`, `ICameraService.cs` |

### Deferred Success Criteria
- Studio preview pixels equal export pixels on WARP (`Lightshot.Platform.Windows.Tests.StudioCompositorTests.PreviewAndExportPixelsMatch`).
- OCR recall >= 90% across 200 entities and 3 fonts (`OcrProbe` / Phase 6).
- Camera bubble sizing, shape, mirror, and burn-in rendering (`Lightshot.Core.Tests.CameraBubbleTests`).
- Offline translation chrF >= 45 and latency <= 300 ms per sentence (`OpusMtProbe` / Phase 10).

### Deferred Risk Summary

| Risk | Likelihood | Impact | Phase | Mitigation |
|---|---|---|---|---|
| Studio compositor in WPF cannot hold 60 fps or pixel-equals-export | Medium | High | 2, 8 | Compositor spike selected `HwndHost`; `D3DImage` fallback tested; phase 8 acceptance items address seek and source-skips |
| OCR, Whisper, YuNet quality under bar | Medium | Medium | 2, 6, 8 | ML spike recorded OCR strict fail (67.4%); phase 6 to evaluate enhanced preprocessing, binarization retry, or alternative OCR engine; Whisper and YuNet verified |
