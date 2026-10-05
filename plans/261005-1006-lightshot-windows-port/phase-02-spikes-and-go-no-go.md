# Phase 2: Spikes and go/no-go gate

Status: pending | Effort: 10d | Priority: P1 | Depends on: phase 1

## Overview

Five time-boxed probes retire the five top risks from RULING §8 before any feature is committed. Each is a throwaway console or WPF probe under `spikes/` that prints measurements and a PASS or FAIL per criterion. The phase ends with a written gate decision. No probe code is reused unreviewed; useful pieces are rewritten into `src/` in later phases.

## Requirements

- Every probe has numeric pass criteria (below) and a named fallback.
- Probes run on the host laptop (RTX 4060 hybrid, Windows 11 26200) through `scripts/spike.ps1 -Name <probe>`.
- Results go to `plans/261005-1006-lightshot-windows-port/reports/spike-<name>.md`; the decision goes to `reports/spike-gate.md`.
- The gate is explicit: GO, GO with a named fallback, or STOP and re-plan.

## Files

Create:

| Path | Purpose |
|---|---|
| `scripts/spike.ps1` | Builds and runs one probe, collects its JSON result |
| `scripts/set-gpu-preference.ps1` | Sets `HKCU\Software\Microsoft\DirectX\UserGpuPreferences` for a probe exe to iGPU, dGPU or system default |
| `spikes/capture-probe/capture-probe.csproj`, `Program.cs`, `DdaCapture.cs`, `WgcCapture.cs`, `HdrProbe.cs` | Spike A |
| `spikes/recording-probe/recording-probe.csproj`, `Program.cs`, `DdaToNv12.cs`, `MfFragmentedWriter.cs`, `WasapiMic.cs`, `ProcessLoopback.cs`, `Recover.cs` | Spike B |
| `spikes/compositor-probe/compositor-probe.csproj`, `App.xaml`, `MainWindow.xaml(.cs)`, `D3dHost.cs`, `MfDecoder.cs`, `ProbeCompositor.cs`, `OffscreenExport.cs` | Spike C |
| `spikes/overlay-probe/overlay-probe.csproj`, `Program.cs`, `OverlayWindow.cs`, `HotkeyWindow.cs`, `HookThread.cs` | Spike D |
| `spikes/ml-probe/ml-probe.csproj`, `Program.cs`, `OcrProbe.cs`, `WhisperProbe.cs`, `YuNetProbe.cs`, `OpusMtProbe.cs`, `Fixtures/` | Spike E |
| `plans/261005-1006-lightshot-windows-port/reports/spike-*.md` | Results |

`spikes/` is excluded from `Lightshot.slnx` release builds and from `package.ps1`. It may reference packages not yet in `Directory.Packages.props`.

## Spikes

### Spike A: capture on the hybrid-GPU laptop (1.5d) - retires the capture risk

- Probe: capture each monitor by DXGI Desktop Duplication (D3D device created on the adapter that owns the output) and by Windows.Graphics.Capture with `IsBorderRequired = false`, unpackaged. Run with the exe forced to the iGPU, then the dGPU, then system default. Capture a window with WGC and with `PrintWindow(PW_RENDERFULLCONTENT)`. Probe HDR via `IDXGIOutput6.GetDesc1` and tone-map FP16 to SDR.
- Pass: no yellow border on WGC for the unpackaged exe; a still in under 100 ms per monitor; HDR frames tone-mapped to plausible SDR; correct pixels and origin on a mixed-DPI monitor pair; DDA does not fail with `DXGI_ERROR_UNSUPPORTED` in any GPU preference.
- Fallbacks: border shown -> register a sparse package for the exe (phase 4 and 9 conditional work); DDA unsupported on a path -> create the device on the output adapter or use WGC for displays; window-clean images wrong -> use `PrintWindow` for stills.
- Uncovered by hardware: no HDR panel or a single monitor on the host. Record which cases ran and mark the rest UNCOVERED in the report.

### Spike B: real-time recording pipeline (2.5d) - retires the recording risk

- Probe: DDA -> GPU video processor (crop, scale, BGRA to NV12) -> Media Foundation sink writer with the hardware H.264 and HEVC MFTs into fragmented MP4. Audio: WASAPI microphone plus process-excluding loopback (try NAudio first, then direct CsWin32 `ActivateAudioInterfaceAsync`), both stamped on QPC, with a pause offset. Record 60 s at 1440p60 with both audio sources, kill the process at 40 s, then recover and remux to a normal MP4 using a passthrough sink writer.
- Pass: the file plays in the Windows media player and in `MediaElement`; A/V drift under 40 ms at the end; dropped frames under 1%; CPU under 15%; the killed file is playable and remuxes with video and both audio tracks intact; loopback excludes the probe's own audio.
- Fallback: if fragmented MP4 recovery or passthrough remux fails, evaluate FFmpeg (LGPL, dynamically linked) for muxing only. If NAudio lacks process loopback, use direct COM.

### Spike C: Studio compositor inside WPF (3d) - retires the Studio risk

- Probe: MF hardware decode to D3D11 textures -> video processor to BGRA -> D2D effects (blur, shadow, rounded mask, zoom transform, cursor sprite) -> flip-model swapchain in an `HwndHost` at 60 fps. Frame-accurate seek. The same `Render(state, t, target)` renders into an offscreen texture that feeds an MF sink writer. Also check SoundTouch (LGPL, dynamic) pitch-preserving speed on a 2x segment, and a WARP device render.
- Pass: 1440p preview at 60 fps with no dropped frames over 30 s; seek to a frame in under 150 ms; export pixels equal preview pixels on WARP (max channel delta <= 2); WPF resize and DPI change do not tear or leak the swapchain.
- Fallback: try a `D3DImage` host before changing the UI stack. If SoundTouch cannot be dynamically linked cleanly, ship speed changes without pitch preservation and flag the parity gap to the user.

### Spike D: overlay layer (1.5d) - retires the overlay risk

- Probe: global hotkey -> freeze all monitors -> one topmost DComp window per monitor showing the frozen still, in under 150 ms. Check: foreground and focus are obtained from `WM_HOTKEY`; Esc, Return and arrow keys reach the window; `WDA_EXCLUDEFROMCAPTURE` keeps a card and a pill out of DDA and WGC output; detect Windows 11's PrintScreen claim (`HKCU\Control Panel\Keyboard` `PrintScreenKeyForSnippingEnabled`) and whether `RegisterHotKey(VK_SNAPSHOT)` still fires; `WH_KEYBOARD_LL` and `WH_MOUSE_LL` on a dedicated thread never exceed `LowLevelHooksTimeout`; note whether Defender flags the hook; validate the shell-thread model from plan.md (overlay HWNDs on a separate thread from WPF).
- Pass: hotkey to first overlay pixel under 150 ms (median of 20); keys delivered 20 of 20; excluded windows absent from 20 of 20 frames; hook callback max under 5 ms; PrintScreen outcome recorded with the exact setting needed.
- Fallback: focus not granted -> `AttachThreadInput` plus a synthesised Alt key; hotkey claimed -> default chords avoid bare PrintScreen; overlay too slow -> pre-created hidden overlay windows warmed at startup.

### Spike E: on-device ML quality and cost (1.5d) - retires the ML risk

- Probe: (1) Whisper.net `ggml-base` q5 with word timestamps on a 5-minute narration, CPU and CUDA/Vulkan. (2) `Windows.Media.Ocr` on fixtures rendered from the Auto Redact categories (secrets, cards, IBANs, emails, SSN, IP, labelled values), with 2x upscale and tiling. (3) YuNet ONNX on a face set. (4) One OPUS-MT pair exported to ONNX, run on ONNX Runtime with a Marian-aware tokenizer.
- Pass: captions within 300 ms word-timing error and faster than real time on CPU; OCR recall at least 90% (measured against ground truth of the rendered fixtures because no macOS host exists for the Vision baseline; see plan.md question 8); YuNet recall at least 90% at default thresholds on the face set; one translation pair loads and decodes a sample correctly.
- Fallbacks: Whisper too slow on CPU -> ship `tiny` q5 as default and `base` as optional import; OCR recall low -> add per-line retry at 3x and a binarised variant; Marian tokenizer unusable -> keep translation scheduled but re-estimate phase 10.
- Outputs set the bundle budget used by `package.ps1 -MaxSetupMB`. Translation stays last whatever the result.

## Implementation steps

1. Build `scripts/spike.ps1` and the GPU preference script.
2. Run spikes A and D first (cheapest, they gate phases 4 and 7), then B, then C (needs B's decode knowledge), then E in parallel with C when a second engineer exists.
3. Write each result report with raw numbers, the hardware matrix actually covered, and the fallback triggered.
4. Write `reports/spike-gate.md` with this table filled in:

| Verdict | Effect on later phases |
|---|---|
| A fails border check | Add sparse-package work package to phase 4 (identity registration for dev) and phase 9 (Velopack install hook) |
| A fails DDA on some GPU path | Phase 4 and 7 capture sources use WGC for displays on that path |
| B fails recovery | Phase 7 adds FFmpeg muxing-only dependency (+LGPL notice) |
| C fails 60 fps | Phase 8 uses `D3DImage` host; re-estimate +3d |
| D fails 150 ms | Phase 4 pre-warms hidden overlays at startup |
| E Whisper too slow | Phase 8 bundles `tiny` q5; `base` becomes optional import |
| E OCR below bar | Phase 6 adds retry strategy, +2d |

5. Update `plan.md` effort and the affected phase files with the verdicts. Stop and re-plan if two or more High risks fail.

## Acceptance criteria

All criteria are probe-measured and recorded in the reports; none is an automated xUnit test because each needs hardware, an interactive session or a large media sample and the code is throwaway.

| Criterion | Evidence | Mapping |
|---|---|---|
| A: borderless unpackaged WGC, <100 ms stills, DDA on all GPU preferences, HDR, mixed DPI | `reports/spike-capture.md` with `capture-probe --assert` exit code | UNCOVERED by xUnit: needs displays and GPUs; re-tested in phase 4 by `Lightshot.Platform.Windows.Tests.DdaCaptureTests.CapturesEachMonitorAtNativeSize` (`Desktop`) |
| B: playable, drift <40 ms, drops <1%, CPU <15%, recovery works | `reports/spike-recording.md` with `recording-probe --assert` | UNCOVERED by xUnit; re-tested in phase 7 by `Lightshot.Platform.Windows.Tests.RecordingRecoveryTests.RemuxesKilledTake` (`Media`) |
| C: 60 fps, seek <150 ms, preview equals export on WARP | `reports/spike-compositor.md` | UNCOVERED by xUnit; re-tested in phase 8 by `Lightshot.Platform.Windows.Tests.StudioCompositorTests.PreviewAndExportPixelsMatch` (`Gpu`) |
| D: overlay <150 ms, keys 20/20, exclusion 20/20, hook <5 ms | `reports/spike-overlay.md` | UNCOVERED by xUnit; re-tested in phase 4 by `Lightshot.Platform.Windows.Tests.OverlayLatencyTests.HotkeyToFirstPixelUnder150Ms` (`Desktop`) |
| E: caption timing, OCR recall, YuNet recall, one translation pair | `reports/spike-ml.md` | UNCOVERED by xUnit; re-tested in phases 6, 8, 10 |
| Gate decision recorded and effects applied to later phase files | `reports/spike-gate.md` | UNCOVERED: a human decision record |

## Rollback

Delete `spikes/` and the reports. Only registry values written by `set-gpu-preference.ps1` persist; the script has `-Reset` and logs what it set.

## Risks

| Risk | L | I | Mitigation |
|---|---|---|---|
| Host lacks an HDR panel, a second monitor or mixed DPI | H | M | Report exactly what was covered; mark rest UNCOVERED; re-test when hardware is available |
| Spike estimates overrun | M | M | Time-box each; stop at pass or fallback decision, do not polish |
| Probe code leaks into product | M | M | `spikes/` excluded from solution; rewrite rule in README |
| Registry GPU preference left behind | L | L | `-Reset` and log |
| A passing spike on this laptop does not generalise | M | M | Gate records hardware matrix; phase 4 adds a MUX/iGPU-only note to the manual checklist |

## Dependencies

Phase 1 scripts and project scaffold. Blocks phase 3 (gate) and informs phases 4, 6, 7, 8, 9.
