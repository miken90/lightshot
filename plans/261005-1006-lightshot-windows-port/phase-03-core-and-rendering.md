# Phase 3: Core domain port and deterministic rendering

Status: pending | Effort: 12d | Priority: P1 | Depends on: phase 1, phase 2 gate

## Overview

Two work packages with disjoint file ownership, runnable in parallel:

- **Package C**: port `LightshotKit` (63 source files, 41 test classes, ~556 tests) to `Lightshot.Core` as plain `net10.0` with zero packages.
- **Package R**: build `Lightshot.Rendering` (SkiaSharp CPU `render()`, `TextLayout`, redaction patch, own blur and scramble, PNG/JPEG codecs, thumbnails, bundled Inter) and re-baseline the pixel tests.

The slice shipped is a tested library pair: later phases code against stable interfaces and a pure core. Clone the upstream SHA into a scratch directory first and read `HotkeyBindings.defaults`, `RecordingDefaults.standard`, `QuickAccessSettings`, ADR 0001 and the real `LightshotKit/Sources`; the analysis reports describe but do not contain them (KIT §6, APP "Risks").

## Requirements

- Behaviour parity with the Swift core; same constants (KIT §7).
- `render()` is pure and deterministic; preview equals export through one shared redaction patch (SPECS invariants 3, 4).
- Own pixel code (blur, scramble, resample) is bit-exact on every CPU: scalar or fixed-point arithmetic only, no `Vector<T>`, no FMA, no `Math.Exp`-derived kernels.
- Text uses the bundled Inter loaded with `SKTypeface.FromStream`, never system fonts.
- Goldens: exact (byte hash) for own pixel code; max 2 per-channel LSB for Skia antialiased output. Re-baselining is deliberate, one test at a time, with a reviewed diff.
- Coordinates: geometry in image pixels (top-left origin); `CaptureRegion` is global virtual-screen physical pixels (plan.md decision). Every ported constant that is in "points" (for example `stillThreshold` 0.5, snap 8, card width 220) is recorded as DIPs, and the unit of each is stated in a comment when ported.

## Data flow

`AnnotationDocument` (value state, commands, undo snapshots) -> `IImageRenderer.Render(document)` (Rendering) -> base image + elements in z-order + focus dim -> redaction patches via `RedactionBackdrop.Patch(rect, style, strength, seed)` -> `RenderedImage` (PNG/JPEG via `IImageCodec`). The editor canvas (phase 4) calls the same renderer, caching the base image and patches and redrawing dirty elements.

## Files

### Package C: create under `src/Lightshot.Core/`

| Folder | Files (source name in KIT §2 -> `.cs`) |
|---|---|
| `Geometry/` | `Geometry.cs` (Point, Size, Rect as `readonly record struct` of double), `Transform.cs`, `CanvasProjection.cs`, `ArrowGeometry.cs`, `EditableSelection.cs` |
| `Annotation/` | `AnnotationDocument.cs`, `AnnotationElement.cs`, `Style.cs`, `StyleFields.cs`, `AutoRedactPlan.cs` |
| `Scanner/` | `SensitiveDataScanner.cs` (.NET `Regex` with `RegexOptions.CultureInvariant` and match timeouts, `IPAddress.TryParse` replaces `inet_pton`), `IWeakEntityDetector.cs`, `TextRecognition.cs`, `TextCapture.cs` |
| `Capture/` | `CaptureAction.cs`, `CaptureAuthorizationStatus.cs`, `CapturedImage.cs`, `CaptureError.cs`, `CaptureRegion.cs`, `ICaptureService.cs`, `FrozenScreen.cs` (raw `PixelSurface` per display), `PixelSurface.cs`, `ImageFormat.cs`, `ImageLoadError.cs`, `IImageSink.cs`, `IImageSource.cs`, `IOverlayController.cs`, `IImageRenderer.cs`, `IImageCodec.cs`, `IThumbnailer.cs`, `QuickAccess.cs`, `AppCoordinator.cs` (+ partial files per flow) |
| `Hotkeys/` | `HotkeyBinding.cs` (key code is a Windows virtual-key), `IHotkeyService.cs`, `KeyLabel.cs` (VK name table) |
| `Settings/` | `ISettingsStore.cs`, `FilenameFormatter.cs` (adds Windows-forbidden characters), `RecordingDefaults.cs` |
| `History/` | `HistoryStore.cs` (file I/O plus injected `IThumbnailer`) |
| `Appearance/` | `Appearance.cs`, `ThemePalette.cs` |
| `Permissions/` | `IPermissionAuthorizing.cs`, `PermissionGate.cs`, `PermissionKind.cs`, `PermissionOnboardingModel.cs` (`INotifyPropertyChanged`) |
| `Recording/` | `RecordingOptions.cs`, `IRecordingService.cs`, `RecordingSession.cs`, `RecordingError.cs`, `IAudioInputService.cs`, `ICameraService.cs`, `AudioMixer.cs`, `CameraBubble.cs`, `ClickHighlight.cs`, `KeystrokeOverlay.cs`, `IInputEventSource.cs`, `MutedMicrophoneDetector.cs`, `GifFramePlan.cs`, `IGifEncoding.cs`, `GifWriter.cs`, `GifQuantizer.cs`, `FrameCadencePlanner.cs`, `IMediaSink.cs`, `IMediaMetadataSource.cs` |
| `Studio/` | `StudioCapture.cs`, `StudioCaptions.cs`, `StudioDocument.cs`, `StudioEdits.cs`, `StudioInput.cs`, `StudioTimeline.cs`, `CanvasLayout.cs`, `CursorPath.cs`, `ZoomCamera.cs`, `TimelineSnap.cs`, `VideoEdit.cs` (with `VideoBitRate`, `SizeEstimator`), `VideoTimeline.cs`, `IStudioFlattening.cs`, `StudioProjectStore.cs` |
| `Threading/` | `IUiDispatcher.cs` (replaces `@MainActor`), `ImmediateDispatcher.cs` |
| `Lightshot.Core.csproj` | no packages |

`GifWriter` (LZW) and `GifQuantizer` (deterministic median-cut, per-frame local palette) are new pure Core code because the ruling places the GIF quantiser in Core. `FrameCadencePlanner` is new pure logic (duplicate or drop frames to hold constant fps from a variable-rate source) used by phase 7.

### Package R: create under `src/Lightshot.Rendering/`

`Rendering.cs` (the `render` entry), `DocumentRenderer.cs` (implements `IImageRenderer`), `ElementPainter.cs`, `FocusDim.cs`, `TextLayout.cs` (Skia metrics plus greedy word wrap), `FontProvider.cs` (embedded Inter), `RedactionBackdrop.cs`, `GaussianBlur.cs`, `Scramble.cs` (SplitMix64, area-average downscale, nearest upscale), `SkiaImageCodec.cs` (PNG, JPEG, decode, GIF frame count and delay via `SKCodec`), `SkiaThumbnailer.cs`, `PixelSurfaceConverter.cs`, embedded resource `assets/fonts/Inter-Regular.ttf`, `Inter-Bold.ttf`.

### Tests: create

- `tests/Lightshot.Core.Tests/**` mirroring Core folders, `FakeServices/` (fakes for every service interface), `TierTraitTests.cs`, `NoticeFileTests.cs` already from phase 1.
- `tests/Lightshot.Rendering.Tests/**`: `DocumentRenderTests`, `ExportTests`, `TextLayoutTests`, `GaussianBlurTests`, `ScrambleTests`, `ThumbnailTests`, `FrozenScreenCodecTests`, `CoordinatorRenderTests`, `Goldens/*.png`, `Goldens/hashes.json`, `PixelAssert.cs`.
- `docs/porting/test-manifest.json` and `scripts/check-test-parity.ps1`.

## Test porting ledger (41 classes; counts from KIT §5)

| Source class (count) | Target project and tier | Notes |
|---|---|---|
| AnnotationDocumentTests (58), ArrowGeometryTests (12), CanvasProjectionTests (6), EditableSelectionTests (21), StyleFieldsTests (1) | Core.Tests, Unit | Verbatim; method names preserved |
| AppCoordinatorTests (90) | Core.Tests, Unit (~80); Rendering.Tests, Render (~10 that call real render or history) | Fakes ported once; `ImmediateDispatcher` replaces `@MainActor` |
| RecordingCoordinatorTests (76) | Core.Tests, Unit (~70); Rendering.Tests, Render (~6) | Same split |
| AppearanceTests (8), AudioMixerTests (7), CameraBubbleTests (7), ClickHighlightTests (5), GIFFramePlanTests (5), HotkeyBindingTests (11), KeystrokeOverlayTests (12), MutedMicrophoneDetectorTests (3), PermissionGateTests (5), PermissionOnboardingModelTests (9), QuickAccessTests (13), RecordedAreaTests (3), RecordingOptionsTests (18), RecordingSessionTests (13), SeamTests (2), TextCaptureTests (14), VideoEditTests (5), VideoTimelineTests (4) | Core.Tests, Unit | HotkeyBinding and KeystrokeOverlay fixtures switch from macOS key codes to VKs; QuickAccessTests re-checked for the y-axis (below) |
| Studio*: AnnotationTests (5), CaptionTests (13), CaptureTests (10), DocumentTests (11), EngineTests (22), ExportBitRateTests (5), ExportFrameRateTests (5), FlattenTests (4), SnapTests (4), TrimSpeedTests (14) | Core.Tests, Unit | `StudioCaptureTests` use a temp directory |
| AutoRedactTests (30) | Core.Tests, Unit for scanner logic, plan and undo; detector cases move to phase 6 | Phone, link, address cases (those that used `NSDataDetector`) are re-pinned in phase 6 against `IWeakEntityDetector` |
| DocumentRenderTests (26), ExportTests (7), TextLayoutTests (4) | Rendering.Tests, Render | Pixel; re-baselined |
| FrozenScreenTests (13) | Core.Tests, Unit for crop maths on synthetic surfaces; Rendering.Tests, Render for PNG round trips | Adapted to `PixelSurface` |
| HistoryStoreTests (14) | Core.Tests, Unit (retention, trim, ordering, back-compat decode) with a fake thumbnailer; Rendering.Tests, Render (thumbnails, GIF metadata) | |

`docs/porting/test-manifest.json` lists each source class, its count and its target; `check-test-parity.ps1` fails if a target class has fewer tests than the manifest. Method names are taken from the cloned source at port time; new tests are named below.

## Implementation steps

### Package C

1. Clone upstream at the SHA into a scratch directory. Fill `docs/porting/test-manifest.json` from the clone. Read the real defaults and ADR 0001; record them in `docs/porting/defaults.md` (WHY and WHERE only).
2. Port Geometry and Annotation, then their tests. Use `System.Text.Json` for `Codable` types with explicit converters for element unions.
3. Port Capture models, `FrozenScreen` on `PixelSurface`, `CaptureRegion` as physical pixels, coordinators, settings seam, hotkeys (VK), permissions, history, appearance, quick access. Quick Access layout: the source stacks from a bottom-up visible frame; port with an explicit `ScreenAnchor` (top-left origin plus work area) and test both corners.
4. Port the scanner. Phone, link and address detection go through `IWeakEntityDetector`; the Core test fake returns pinned fixtures.
5. Port Recording and Studio models, `AudioMixer`, `CursorPath` (120 Hz critically damped spring, `omega = 40 - 35*smoothing`, 4 sub-steps), `ZoomCamera`, `CaptionBuilder`, `StudioEdits` (version 2, with v1 migration), `VideoBitRate`.
6. Add `GifWriter`, `GifQuantizer`, `FrameCadencePlanner` with new tests.
7. Run `check-core.ps1` after each folder; it must stay green.

### Package R

1. `FontProvider` loads Inter from embedded resources once; Skia paints use grayscale antialiasing, no hinting, subpixel positioning on.
2. `TextLayout`: measure with `SKFont`, greedy word wrap at the box width, long-word character break, same padding (`max(4, round(0.3 * fontSize))`) and minimum width (`1.5 * fontSize + 2 * pad`) as the source.
3. `GaussianBlur`: three-pass integer box blur sized from sigma using only `Math.Sqrt` and integer arithmetic (IEEE-exact), edge-clamped to the extent. Document the approximation versus a true Gaussian.
4. `Scramble`: SplitMix64 exactly as in KIT §4; area-average downscale (integer), neighbour offset `rng % 3 - 1` in x then y with two draws per cell in x-then-y order, nearest upscale.
5. `RedactionBackdrop.Patch` and `DocumentRenderer`: highlight alpha capped at 0.35, blackout opaque, blur and pixelate snapshot the surface so far and copy the patch with replace blending, focus dim at alpha 0.55 with rounded clears `min(12, w/4, h/4)`, step marker disc plus number at `radius * 1.2`, round caps and joins.
6. `SkiaImageCodec`: PNG and JPEG encode (quality clamped 0..1), decode, and original bytes returned if decode fails (matches the source).
7. Create goldens with `test.ps1 -Tier Render -UpdateGoldens`, review every PNG, then lock. Run the Render tier on a second machine or a different CPU feature set (`DOTNET_EnableAVX2=0`) to prove the own-code goldens are identical.

## Acceptance criteria

| Criterion | Test |
|---|---|
| Undo restores state and no-op leaves no undo step | `Lightshot.Core.Tests.AnnotationDocumentTests` (58, verbatim) incl. `UndoRestoresSnapshot` (new, explicit) |
| Step numbers stay stable after deletion; `add(contentsOf:)` is one undo step | `AnnotationDocumentTests` and `AutoRedactTests` plan/undo cases |
| Arrow constants (head, shaft, 16-sample centreline) | `Lightshot.Core.Tests.ArrowGeometryTests` (12) |
| Coordinator routing, onboarding gate, self-timer, repeat last | `Lightshot.Core.Tests.AppCoordinatorTests` (~80) |
| Recording coordination and GIF fallback | `Lightshot.Core.Tests.RecordingCoordinatorTests` (~70), `RecordingSessionTests` (13) |
| Mixed-scale crop picks the display with largest overlap, in physical pixels | `Lightshot.Core.Tests.FrozenScreenTests.CropPicksLargestOverlapAcrossMixedScale` (new, adapted) |
| Quick Access stacks correctly from a top-left-origin work area | `Lightshot.Core.Tests.QuickAccessTests.StacksFromBottomRightOfWorkArea` (new) |
| Filename sanitiser rejects Windows-forbidden characters | `Lightshot.Core.Tests.FilenameFormatterTests.StripsWindowsForbiddenCharacters` (new) |
| Hotkey conflicts by VK plus modifiers | `Lightshot.Core.Tests.HotkeyBindingTests` (11) |
| GIF writer round-trips frames, delays, loop count | `Lightshot.Rendering.Tests.GifWriterTests.RoundTripsFramesAndDelays` (new; decodes with `SKCodec`) |
| Frame cadence holds constant fps from a bursty source | `Lightshot.Core.Tests.FrameCadencePlannerTests.RepeatsLastFrameWhenSourceIdle` (new) |
| Studio v1 project migrates to v2; JSON round trip | `Lightshot.Core.Tests.StudioDocumentTests` (11) and `StudioEditsTests.MigratesVersionOneToTwo` (new) |
| Blur is bit-exact and CPU-independent | `Lightshot.Rendering.Tests.GaussianBlurTests.OutputHashMatchesGolden` (new, exact) |
| Scramble matches the SplitMix64 sequence and is deterministic per seed | `Lightshot.Rendering.Tests.ScrambleTests.SeedFixesOutput` (new, exact) |
| Render output matches re-baselined goldens (arrow styles, focus dim, text, redaction) | `Lightshot.Rendering.Tests.DocumentRenderTests` (26; 1-2 LSB for Skia paths) |
| Redaction preview patch equals export patch | `Lightshot.Rendering.Tests.DocumentRenderTests.PreviewPatchEqualsExportPatch` (new) |
| Blur smears across the seam widely enough | `DocumentRenderTests.BlurSmearsTheSeamWidely` (ported) |
| Text metrics stable with bundled font | `Lightshot.Rendering.Tests.TextLayoutTests` (4, re-baselined) |
| Copy-and-close writes no file (sink records a copy, zero writes) | `Lightshot.Core.Tests.AppCoordinatorTests` ported guard |
| Core free of Windows and package references | `Lightshot.Architecture.Tests.CoreAssemblyTests.HasNoPackageOrWindowsReferences` |
| Ported test count equals manifest | `scripts/check-test-parity.ps1` (script check; UNCOVERED by xUnit because it counts tests) |
| Render goldens pass on a second CPU | UNCOVERED by automation: second-machine run is a manual release check; mitigated by `DOTNET_EnableAVX2=0` run in `test.ps1 -Tier Render` |

## Rollback

Core and Rendering have no consumers yet. Revert the commit series; nothing persisted. If the box-blur approximation is rejected visually in phase 4, replace only `GaussianBlur.cs` and re-baseline its goldens; interfaces do not change.

## Risks

| Risk | L | I | Mitigation |
|---|---|---|---|
| Swift-to-C# semantic drift (value semantics, integer division, rounding `floor` vs `round`) | M | H | Verbatim test port first; property-style checks on geometry; read rounding rules from KIT §7 |
| Constants in points vs pixels misread | M | M | Unit stated per constant in a comment; review checklist |
| `Regex` behaviour differs from `NSRegularExpression` | M | M | Port the 21 scanner tests verbatim; add timeouts; `CultureInvariant` |
| Skia SIMD changes antialiasing across CPUs | M | M | 2 LSB tolerance; run with `DOTNET_EnableAVX2=0`; own pixel code exact |
| Box-blur approximation looks different from Core Image | M | L | Visual review in phase 4; sigma range 3..30 kept; swap file if needed |
| `FrozenScreen` raw-surface change invalidates ported tests | M | M | Adapt deliberately, document in `docs/porting/defaults.md` |
| ADR 0001 / defaults not in analysis packs | H | M | Step 1 reads the clone before porting |
| Package size of Core creep (somebody adds a package) | L | H | `check-core.ps1` in `build.ps1` |

## Dependencies

Phase 1 (projects, tiers, scripts). Phase 2 gate (verdicts may change capture interfaces). Blocks phases 4 to 10; every later phase consumes the interfaces defined here.
