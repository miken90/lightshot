# Phase 8: Studio (timeline editor, auto zoom, cursor art, backgrounds, captions, export)

Status: pending | Effort: 25d | Priority: P1 | Depends on: phases 2, 3, 7

## Overview

The Studio editor opens a recording that carries side data (`input.json`, optional camera movie) and composes it live: background, rounded and shadowed screen layer, auto zoom, cursor with original art, click rings, keystroke pills, camera bubble, speed ramps, trims, and captions. Preview and export use the same compositor (invariant: preview equals export). Export writes MP4 (H.264 or HEVC) or GIF.

Packages: S1 compositor, S2 decode and seek, S3 timeline UI, S4 zoom, cursor and backgrounds, S5 audio (speed, mixing), S6 captions, S7 export and flattening.

## Requirements

- One `StudioCompositor.Render(StudioState, double t, RenderTarget target)` on Vortice D3D11, Direct2D and DirectWrite. Preview and export share it. Core keeps the pure geometry: `StudioProject`, `ZoomPlanner`, `CursorSmoother`, `StudioTimeline`, `SpeedRamp`, `CursorTheme`, `CaptionLayout`, `KeystrokeOverlay` (phase 3).
- Cursor art: ten original vector cursor themes drawn by us (no Apple assets, no system cursor copies); the cursor is drawn from `input.json`, never from the recorded pixels (DDA recordings exclude the cursor).
- Decoding: Media Foundation hardware decode to a D3D11 texture with a frame-accurate seek (seek to the previous key frame, decode forward discarding to the target). A small decoded-frame cache for scrubbing and a prefetch thread for play.
- Preview host: `HwndHost` hosting a flip-model swap chain; `D3DImage` fallback if the airspace spike result (spike D) says HwndHost fails with overlays. Aspect and canvas presets from Core.
- Timeline UI: tracks for video, zoom, speed, captions, keystrokes; split, delete, drag handles, undo/redo (Core `StudioHistory` if present, else ported equivalents), snap to the playhead, keyboard shortcuts.
- Auto zoom: Core `ZoomPlanner` from click and key events; manual zoom regions with strength and duration; smooth cursor path.
- Backgrounds: gradient, solid, wallpaper-style presets, image; padding, corner radius, shadow; screen layer crop.
- Speed: ramps and uniform speed with pitch-preserving audio via SoundTouch (LGPL; dynamic link, recorded in THIRD-PARTY-NOTICES).
- Captions: Whisper.net with `ggml-base-q5` bundled; word timestamps; silence gating; local larger-model import (file picker with SHA-256 display, never downloaded); language auto or manual; captions editable; style presets in Core `CaptionLayout`. DEGRADE: accuracy differs from Apple speech; first-run model load time shown.
- Export: MP4 H.264 or HEVC (hardware encoders via the phase 7 `EncoderSelector`, HEVC hidden when no decoder), audio AAC, fps and resolution choices, progress and cancel, cancel deletes the partial; GIF export via `MfGifEncoder` fed from compositor frames. Export runs the compositor offline at frame times, never from wall time.
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
| `CursorArt.cs`, `CursorThemes/*.cs` | Ten original vector cursor themes |
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
5. S4: wire `ZoomPlanner`, `CursorSmoother`, themes, backgrounds; the inspector edits `StudioState`.
6. S5: SoundTouch processing per speed segment; mixing with the camera audio tracks (mic, system) from the project.
7. S6: `WhisperModelStore` verifies SHA-256 of the bundled and imported models; decode audio to 16 kHz mono; `SilenceGate` skips silent spans to avoid hallucinated text; word timestamps flow into Core `CaptionLayout`; generation runs cancellable on a worker.
8. S7: exporter renders at frame times from the project timeline, writes video and audio through MF; GIF from the same frames; `StudioFlattener` produces a plain MP4 on demand.
9. Preview equals export: the preview renders through the same function; a test compares a preview frame and an export frame at the same time.
10. Performance pass: target 30 fps preview at 1080p on the RTX 4060 laptop, export at least 1x real time for 1080p30 H.264 hardware.

## Acceptance criteria

| Criterion | Test |
|---|---|
| Studio geometry (zoom plan, cursor smoothing, timeline, speed ramps, layout, themes) | `Lightshot.Core.Tests.StudioProjectTests`, `ZoomPlannerTests`, `CursorSmootherTests`, `StudioTimelineTests`, `SpeedRampTests`, `CaptionLayoutTests` (phase 3 ports) |
| Compositor output matches goldens (WARP) | `Lightshot.Platform.Windows.Tests.StudioCompositorGoldenTests.MatchesGoldenAtKeyTimes` (`Gpu`) |
| Cursor themes are original and render identically | `Lightshot.Platform.Windows.Tests.CursorThemeGoldenTests.AllThemesMatchGoldens` (`Gpu`) |
| Preview frame equals export frame | `Lightshot.Platform.Windows.Tests.PreviewEqualsExportTests.SameTimeSamePixels` (`Gpu`) |
| Frame-accurate seek | `Lightshot.Platform.Windows.Tests.FrameSeekerTests.SeekReturnsExactRequestedFrame` (`Media`) |
| Playback clock follows audio and handles speed | `Lightshot.Platform.Windows.Tests.PlaybackClockTests.StaysWithinOneFrameOfAudio` (`Unit`) |
| Speed preserves pitch | `Lightshot.Platform.Windows.Tests.AudioSpeedProcessorTests.PreservesPitchAtDoubleSpeed` (`Unit`, FFT peak check) |
| Export MP4 duration, streams, fps within tolerance | `Lightshot.Platform.Windows.Tests.StudioExporterTests.ExportsPlayableMp4WithExpectedDuration` (`Media`) |
| Export cancel deletes partial | `Lightshot.Platform.Windows.Tests.StudioExporterTests.CancelDeletesPartial` (`Media`) |
| Flattening yields a plain movie that the video editor opens | `Lightshot.Platform.Windows.Tests.StudioFlattenerTests.FlattenedMovieOpensInVideoEditor` (`Media`) |
| Model hash is verified; imported model is checked | `Lightshot.Platform.Windows.Tests.WhisperModelStoreTests.RejectsHashMismatch` (`Unit`) |
| Transcription finds words and timestamps | `Lightshot.Platform.Windows.Tests.WhisperTranscriberTests.TranscribesFixtureWithinWordErrorBudget` (`Media`) |
| Silence yields no captions | `Lightshot.Platform.Windows.Tests.SilenceGateTests.SilentAudioProducesNoSegments` (`Unit`) |
| Undo and redo of timeline edits | `Lightshot.App.Tests.TimelineViewModelTests.UndoRedoRestoresState` (`Unit`) |
| Studio opens from the post-recording overlay and exports | `Lightshot.App.UiTests.StudioFlowTests.OpenEditExportProducesFile` (`Desktop`) |
| Studio does not use the network | `Lightshot.Architecture.Tests.NetworkPolicyTests` (phase 9) |
| Hardware-encoder and HEVC outputs on other GPUs | UNCOVERED: only one GPU on the host; manual matrix |
| Aesthetic quality of cursor art, backgrounds, shadows | UNCOVERED: visual; manual review |
| Whisper accuracy relative to Apple speech | UNCOVERED: no macOS host; fixtures substitute (plan.md question 8) |

## Rollback

Studio is a separate window and project format with its own folder; remove the entry points and the recordings still open in the plain editor. Models are content assets. Projects carry `version`; additive changes only.

## Risks

| Risk | L | I | Mitigation |
|---|---|---|---|
| Frame-accurate seek slow on long GOPs | M | M | Phase 7 writes a 2 s GOP; cache; prefetch |
| HwndHost airspace clashes with overlays | M | M | Spike D; `D3DImage` fallback |
| GPU golden drift across drivers | H | M | Goldens rendered on WARP only; hardware path uses structural checks |
| Whisper base q5 slow on CPU for long takes | M | M | Cancellable, progress; optional larger model import |
| Whisper hallucination on silence | H | M | `SilenceGate`, VAD thresholds, caption review UI |
| SoundTouch LGPL compliance | L | M | Dynamic linking; notices; replaceable DLL |
| Export drift between preview and file | M | H | One render function; `PreviewEqualsExportTests` |
| Memory growth from frame cache | M | M | Byte-budget LRU |
| Scope size (25d) | H | M | Slices S1..S7 each testable alone; captions and GIF export last |

## Dependencies

Phase 7 (recordings, side data, `EncoderSelector`, `MfGifEncoder`), phase 3 (Core Studio models), phase 2 verdicts C and D, phase 1 model fetch script. Blocks phase 9 only through packaging of models and native DLLs.
