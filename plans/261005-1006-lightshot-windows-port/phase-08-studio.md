# Phase 8: Studio (timeline editor, auto zoom, cursor art, backgrounds, captions, export)

Status: pending | Effort: 35–45d | Priority: P1 | Depends on: phases 2, 3, 7

## Overview

The Studio editor opens a recording that carries side data (`input.json`, optional camera movie) and composes it live: background, rounded and shadowed screen layer, auto zoom, cursor with original art, click rings, keystroke pills, camera bubble, speed ramps, trims, text annotations, and captions. Preview and export use the same compositor (invariant: preview equals export). Export writes MP4 (H.264 or HEVC) or GIF.

Packages: S1 compositor, S2 decode and seek, S3 timeline UI, S4 zoom, cursor and backgrounds, S5 audio (speed, mixing), S6 captions, S7 export and flattening.

## Requirements

- One `StudioCompositor.Render(StudioState, double t, RenderTarget target)` on Vortice D3D11, Direct2D and DirectWrite. Preview and export share it. Core keeps the pure geometry ported in phase 3: `StudioDocument`, `CursorPath`, `ZoomCamera`, `StudioCaptions`/`CaptionBuilder`, `StudioTimeline`, `KeystrokeOverlay` (phase 3).
- Cursor art: ten original vector cursor themes drawn by us (no Apple assets, no system cursor copies); the cursor is drawn from `input.json`, never from the recorded pixels (DDA recordings exclude the cursor). Art review gate per Boom rule 8 (`cc-art`, then Kongming frame review, never agy).
- Decoding: Media Foundation hardware decode to a D3D11 texture with a frame-accurate seek (seek to the previous key frame, decode forward discarding to the target). A small decoded-frame cache for scrubbing and a prefetch thread for play.
- Preview host: `HwndHost` hosting a flip-model swap chain; `D3DImage` fallback if the airspace spike result (Spike C) says HwndHost fails with overlays. Aspect and canvas presets from Core.
- Timeline UI: tracks for video, zoom, speed, captions, keystrokes, audio waveform; delete, drag handles, undo/redo from `StudioDocument` snapshots (limit 200, coalesced with `beginChange`/`endChange`), snap to the playhead, keyboard shortcuts. (Note: split and clip-edge trim removed per spec 0007 Round 3).
- Auto zoom: Core `ZoomCamera` from click and key events; manual zoom regions with strength and duration; smooth `CursorPath`.
- Spec features (SPECS 1.75–1.82 parity):
  - Audio waveform: timeline displays computed waveform peaks.
  - Idle hide: cursor hidden when idle for > 1 s per spec (`CursorPath`).
  - Motion blur: DEGRADE: omitted in initial Direct2D pass due to shader overhead; documented DEGRADE.
  - Ripple and pulse click effects: animated rings on left-click (ripple) and right-click (pulse) rendered by `StudioCompositor`.
  - Word-cut by transcript: selecting text in transcript allows cutting corresponding span in timeline (`CaptionBuilder`).
  - Remove Silences: timeline edit action that truncates spans identified by `SilenceGate` (distinct from silence gating).
  - Text annotations: text overlays with font/colour/size/backdrop placed on canvas (`StudioAnnotation`).
  - Plain MP4s: opening a plain MP4 without side data disables cursor, camera, and keys panels in inspector.
- Backgrounds: gradient, solid, wallpaper-style presets, image; padding, corner radius, shadow; screen layer crop.
- Speed: ramps and uniform speed with pitch-preserving audio via SoundTouch (LGPL; dynamic link, recorded in THIRD-PARTY-NOTICES).
- Captions: Whisper.net with `ggml-base-q5` bundled; word timestamps; silence gating; local larger-model import (file picker with SHA-256 display, never downloaded); language auto or manual; captions editable; style presets in Core `CaptionBuilder`. DEGRADE: accuracy differs from Apple speech; first-run model load time shown.
- Export rules: MP4 H.264 or HEVC (hardware encoders via phase 7 `EncoderSelector`, HEVC hidden when no decoder), export at 24/30/60 fps (default: recorded rate; `StudioExportFrameRate`), HEVC at 70% of H.264 bits with key frame at least every 5 s and "≤ N MB" estimate (`StudioExportBitRate`), audio AAC, progress and cancel, cancel deletes partial; GIF export via `MfGifEncoder` fed from compositor frames. Export runs the compositor offline at frame times, never from wall time.
- `StudioFlattening`: flattening a Studio project to a plain movie so the plain video editor and sharing paths work; the original stays untouched.
- Camera movie: composited as a bubble per `CameraBubble` geometry, in sync via the shared QPC-derived source seconds.
- Local-only; no network access from Studio.

## Data flow

```
recording.mp4 + input.json + camera.mp4 + project.json
 -> StudioProject (Core) -> StudioState(t) -> StudioCompositor.Render
 -> swap chain (preview)   or   offscreen target -> NV12 -> MF sink writer (export)
audio: decode -> speed/pitch (SoundTouch) -> mix -> AAC
captions: audio -> 16 kHz mono PCM -> Whisper.net -> segments with word times -> CaptionTrack
```

## Files

Create under `src/Lightshot.Platform.Windows/Studio/`:

| Path | Purpose |
|---|---|
| `StudioCompositor.cs` | Single render function: background, screen layer, zoom, cursor, rings, keystrokes, bubble, captions |
| `CompositorResources.cs` | Device, brushes, text formats, per-size cache |
| `CursorArt.cs`, `CursorThemes/*.cs` | Ten original vector cursor themes. Art review gate per Boom rule 8 (`cc-art`, then Kongming frame review, never agy) |
| `BackgroundPainter.cs`, `ShadowPainter.cs` | Gradients, images, shadow |
| `MfVideoDecoder.cs`, `FrameCache.cs`, `FrameSeeker.cs`, `PrefetchWorker.cs` | Hardware decode, accurate seek |
| `StudioPlayer.cs`, `PlaybackClock.cs` | Play/pause/scrub, audio-led clock |
| `AudioSpeedProcessor.cs`, `SoundTouchInterop.cs`, `StudioAudioMixer.cs` | Speed with pitch preserved |
| `StudioExporter.cs`, `ExportAudioPipeline.cs`, `StudioFlattener.cs` | Offline render to MP4/GIF |
| `Captions/WhisperTranscriber.cs`, `Captions/WhisperModelStore.cs`, `Captions/PcmResampler.cs`, `Captions/SilenceGate.cs` | Transcription |

Create under `src/Lightshot.App/Views/Studio/`:

| Path | Purpose |
|---|---|
| `StudioWindow.xaml(.cs)`, `StudioViewModel.cs` | Shell, undo, shortcuts |
| `PreviewHost.cs` | `HwndHost` swap chain host, `D3DImageHost.cs` fallback |
| `TimelineControl.xaml(.cs)`, `TimelineViewModel.cs`, `ClipTrack.cs`, `ZoomTrack.cs`, `CaptionTrack.cs` | Timeline |
| `InspectorPanel.xaml(.cs)` | Background, layout, cursor, keystrokes, camera, speed, zoom |
| `CaptionsPanel.xaml(.cs)` | Generate, edit, style, import model |
| `ExportDialog.xaml(.cs)`, `ExportViewModel.cs` | Format, fps, size, progress, cancel |

Assets: `assets/models.lock.json` entry for `ggml-base-q5` (MIT, SHA-256 pinned, fetched by `fetch-models.ps1`); SoundTouch native DLL entry in `THIRD-PARTY-NOTICES`.

Tests under `tests/Lightshot.Platform.Windows.Tests/`: `StudioCompositorGoldenTests` (`Gpu`, WARP), `CursorThemeGoldenTests`, `FrameSeekerTests`, `PlaybackClockTests`, `AudioSpeedProcessorTests`, `StudioExporterTests`, `StudioFlattenerTests`, `WhisperTranscriberTests`, `WhisperModelStoreTests`, `SilenceGateTests`, `PreviewEqualsExportTests`; `tests/Lightshot.App.Tests/{StudioViewModelTests, TimelineViewModelTests, ExportViewModelTests}`; `tests/Lightshot.App.UiTests/StudioFlowTests`. Golden images under `tests/.../Goldens/Studio/` (WARP-rendered, tolerance configured per file, GPU hardware outputs compared only by a looser structural check).

Modify: `AppController.cs` (open Studio from the post-recording overlay and History), `PostRecordingOverlayViewModel.cs` (Studio button), `HistoryViewModel` (Studio badge).

## Implementation steps

1. Read spike C and D verdicts; fix the compositor approach (D2D on the D3D11 device through DXGI surface vs a pure D3D11 shader path) accordingly.
2. S1: implement render passes in fixed order: background, screen layer with zoom transform and rounded mask, shadow, cursor trail and ring effects, cursor art, keystroke pills, camera bubble, captions. Every pass takes only `StudioState` and `t`.
3. S2: decoder to texture, key-frame seek then forward decode, cache keyed by presentation time; scrubbing uses the nearest cached frame while the exact frame decodes.
4. S3: timeline with virtualised rendering; edits produce Core commands pushed to undo.
5. S4: wire `ZoomCamera`, `CursorPath`, themes, backgrounds; the inspector edits `StudioState`.
6. S5: SoundTouch processing per speed segment; mixing with the camera audio tracks (mic, system) from the project.
7. S6: `WhisperModelStore` verifies SHA-256 of the bundled and imported models; decode audio to 16 kHz mono; `SilenceGate` skips silent spans to avoid hallucinated text; word timestamps flow into Core `CaptionBuilder`; generation runs cancellable on a worker.
8. S7: exporter renders at frame times from the project timeline, writes video and audio through MF; GIF from the same frames; `StudioFlattener` produces a plain MP4 on demand.
9. Preview equals export: the preview renders through the same function; a test compares a preview frame and an export frame at the same time.
10. Performance pass: target 60 fps preview at 1440p on the host with no more than 1% presented-frame misses (measured by DXGI frame statistics), export at least 1x real time for 1080p30 H.264 hardware.

## Acceptance criteria

| Criterion | Test |
|---|---|
| Studio geometry and ledger rules | `Lightshot.Core.Tests.StudioAnnotationTests`, `CaptionTests`, `CaptureTests`, `DocumentTests`, `EngineTests`, `ExportBitRateTests`, `ExportFrameRateTests`, `FlattenTests`, `SnapTests`, `TrimSpeedTests` (phase 3 ledger ports) |
| Compositor output matches goldens (WARP) | `Lightshot.Platform.Windows.Tests.StudioCompositorGoldenTests.MatchesGoldenAtKeyTimes` (`Gpu`) |
| Cursor themes are original and render identically | `Lightshot.Platform.Windows.Tests.CursorThemeGoldenTests.AllThemesMatchGoldens` (`Gpu`) |
| Preview frame equals export frame | `Lightshot.Platform.Windows.Tests.PreviewEqualsExportTests.SameTimeSamePixels` (`Gpu`) |
| Preview performance: 60 fps at 1440p, <=1% presented-frame misses | `Lightshot.Platform.Windows.Tests.StudioCompositorPerformanceTests.PresentedFrameMissesUnderOnePercentAt1440p60` (`Gpu`/`Media`) or UNCOVERED on non-RTX hardware |
| Frame-accurate seek | `Lightshot.Platform.Windows.Tests.FrameSeekerTests.SeekReturnsExactRequestedFrame` (`Media`) |
| Playback clock follows audio and handles speed | `Lightshot.Platform.Windows.Tests.PlaybackClockTests.StaysWithinOneFrameOfAudio` (`Unit`) |
| Speed preserves pitch | `Lightshot.Platform.Windows.Tests.AudioSpeedProcessorTests.PreservesPitchAtDoubleSpeed` (`Unit`, FFT peak check) |
| Export MP4 duration, streams, fps within tolerance | `Lightshot.Platform.Windows.Tests.StudioExporterTests.ExportsPlayableMp4WithExpectedDuration` (`Media`) |
| Export cancel deletes partial | `Lightshot.Platform.Windows.Tests.StudioExporterTests.CancelDeletesPartial` (`Media`) |
| Flattening yields a plain movie that the video editor opens | `Lightshot.Platform.Windows.Tests.StudioFlattenerTests.FlattenedMovieOpensInVideoEditor` (`Media`) |
| Model hash is verified; imported model is checked | `Lightshot.Platform.Windows.Tests.WhisperModelStoreTests.RejectsHashMismatch` (`Unit`) |
| Transcription finds words and timestamps | `Lightshot.Platform.Windows.Tests.WhisperTranscriberTests.TranscribesFixtureWithinWordErrorBudget` (`Media`) |
| Silence yields no captions | `Lightshot.Platform.Windows.Tests.SilenceGateTests.SilentAudioProducesNoSegments` (`Unit`) |
| Undo and redo of timeline edits; drag coalescing | `Lightshot.App.Tests.TimelineViewModelTests.UndoRedoRestoresState` and `Lightshot.App.Tests.TimelineViewModelTests.CoalescesDragIntoOneUndoStep` (`Unit`) |
| Studio opens from the post-recording overlay and exports | `Lightshot.App.UiTests.StudioFlowTests.OpenEditExportProducesFile` (`Desktop`) |
| Studio does not use the network | `Lightshot.Architecture.Tests.NetworkPolicyTests` (phase 9) |
| Hardware-encoder and HEVC outputs on other GPUs | UNCOVERED: only one GPU on the host; manual matrix |
| Aesthetic quality of cursor art, backgrounds, shadows | UNCOVERED: visual; manual review. Cursor themes: art review gate per Boom rule 8 (`cc-art`, then Kongming frame review, never agy) |
| Whisper accuracy relative to Apple speech | UNCOVERED: no macOS host; fixtures substitute (resolved decision 2026-10-05) |

## Rollback

Studio is a separate window and project format with its own folder; remove the entry points and the recordings still open in the plain editor. Models are content assets. Projects carry `version`; additive changes only.

## Risks

| Risk | L | I | Mitigation |
|---|---|---|---|
| Frame-accurate seek slow on long GOPs | M | M | Phase 7 writes a 2 s GOP; cache; prefetch |
| HwndHost airspace clashes with overlays | M | M | Spike C; `D3DImage` fallback |
| GPU golden drift across drivers and OS builds | H | M | Goldens rendered on WARP only; WARP output can change between OS builds, so Gpu goldens need a 2-LSB tolerance or a per-build baseline; hardware path uses structural checks |
| Whisper base q5 slow on CPU for long takes | M | M | Cancellable, progress; optional larger model import |
| Whisper hallucination on silence | H | M | `SilenceGate`, VAD thresholds, caption review UI |
| SoundTouch LGPL compliance | L | M | Dynamic linking; notices; replaceable DLL |
| Export drift between preview and file | M | H | One render function; `PreviewEqualsExportTests` |
| Memory growth from frame cache | M | M | Byte-budget LRU |
| Scope size (35–45d) | H | M | Slices S1..S7 each testable alone; captions and GIF export last |

## Dependencies

Phase 7 (recordings, side data, `EncoderSelector`, `MfGifEncoder`), phase 3 (Core Studio models), phase 2 verdicts C and D, phase 1 model fetch script. Blocks phase 9 only through packaging of models and native DLLs.
