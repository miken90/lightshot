# Phase 7: Recording (video, GIF, audio, camera, input overlays, recovery, video editor)

Status: pending | Effort: 30–40d | Priority: P1 | Depends on: phases 2, 4, 5

## Overview

Full screen recording on Windows: selection overlay with the recording toolbar, countdown, DDA capture to a hardware encoder, microphone and system audio, camera bubble, click and keystroke capture, controls pill, red pulsing frame, crash recovery, GIF conversion, post-recording overlay, hide-desktop-icons and hide-notifications guidance, the Screen Recording settings pane, and the plain-movie video editor (trim and convert). Every video take also writes Studio side data (`input.json`, camera movie) so phase 8 can open it; the Studio editor itself is phase 8.

Internal packages (sequential, one owner each folder): R1 selection and chrome windows, R2 capture and encode core, R3 audio, R4 camera, R5 input hooks, R6 burn-in, R7 recovery and GIF, R8 post-recording overlay and video editor, R9 settings pane and wiring.

## Requirements

- Fix the source bug: the source records rect and window areas on the primary display only (`content.displays.first`, APP §3). Here the selection is confined to one display (the one with largest overlap, clamped to it), the overlay covers every display, and capture uses that display's output.
- Pipeline (RULING §8): DDA -> GPU video processor (crop, scale, BGRA to NV12) -> MF sink writer, hardware H.264/HEVC, fragmented MP4. Window pick records its area (spec 0006). DDA delivers frames only on change: `FrameCadencePlanner` repeats the last frame to hold constant fps and the session end is stamped from the QPC clock before stopping capture.
- Audio: WASAPI microphone and process-loopback excluding our process, timestamps on QPC, pause offsets; one mixed AAC track or separate tracks; AAC 48 kHz, 96 kbps mono or 160 kbps stereo per track; muted/disconnected warnings; remembered device falls back to default.
- Video bit rate from Core `VideoBitRate` (about 0.1 bits per pixel per frame); max key-frame interval 2 s; output size = region pixels, optionally capped by max resolution, rounded down to even, minimum 2.
- Pause and resume are a presentation-time offset; frames and audio while paused are dropped.
- Own chrome never in the recording: pill, frame, dim, countdown, camera bubble preview (the bubble is burned in when burn-in applies) use `WDA_EXCLUDEFROMCAPTURE`.
- Hooks: `WH_MOUSE_LL` and `WH_KEYBOARD_LL` on a dedicated thread whose callback only enqueues; keystrokes use `ToUnicodeEx` with the no-state-change flag; unmodified characters are used so Shift+1 reads "Shift 1". Secure-input replacement: a UIA focus-change heuristic using `IsPassword`; keystrokes typed into elevated windows are invisible to the hook (documented).
- Hide desktop icons: reuse `DesktopCover` (phase 5); shown before capture starts and removed on every end path.
- Hide notifications (DEGRADE/CONFLICT): no public toggle exists. Detect with `SHQueryUserNotificationState` and, when the setting is on and notifications would show, display a guidance banner with a button opening `ms-settings:notifications`; never write undocumented registry state. If the empirical check in step 1 shows the API does not reflect Do Not Disturb on this Windows build, the banner is shown once per session without detection.
- Display kept awake during a take (`PowerCreateRequest` with display-required).
- Crash recovery at launch: `.mp4`/`.gif` in scratch offered as undelivered; `.partial` deleted; a playable fragmented file of at least 0.5 s remuxed to a normal MP4; failed streams delete their own partial so recovery does not mistake them for a crash.
- GIF: record video, then convert (`GifFramePlan`, minimum delay 2 cs, posterise depth `4 + round(quality * 4)`, transparency optimise within 8/255, `.partial` renamed on success, cancel removes the partial, cancel-to-video choice).
- HEVC availability varies (decoder extensions): detect encoder and decoder MFTs; hide HEVC when either is missing and default to H.264.
- Video editor: `TrimRange` minimum 0.5 s, `VideoDimensions` presets, bit rate and size estimate; Trim Only (compressed-sample passthrough) or Trim and Convert (H.264 High, AAC 48 kHz); save as new, replace, revert. DEGRADE: Trim Only starts at the key frame at or before the chosen point (<= 2 s early); the dialog shows the snapped time.
- Sounds for countdown, start, stop and pause use `System.Media.SystemSounds` (honours the user's sound scheme; no Apple assets).

## Data flow

```
selection (overlay) -> RecordingChoice -> RecordingOptions.Resolve -> scratch .partial.mp4 (fragmented)
DDA frame -> crop/scale/NV12 (video processor) -> [burn-in pass if studio==false] -> MF sink writer video input
mic / loopback (QPC) -> AudioMixer or separate -> AAC inputs
hooks -> event channel -> StudioInput recorder (source seconds) -> input.json
camera frames -> bubble preview + camera movie (H.264 30 fps) when studio take
stop -> finalize -> remux to progressive MP4 -> delete partial -> History / overlay / editor / Studio
```

## Files

Create under `src/Lightshot.Platform.Windows/`:

| Folder | Files |
|---|---|
| `Recording/` | `WindowsRecordingService.cs` (`IRecordingService`), `RecordingEngine.cs`, `DdaFrameSource.cs`, `VideoProcessorPipeline.cs`, `CadenceDriver.cs`, `MfFragmentedWriter.cs`, `EncoderSelector.cs`, `PauseClock.cs`, `QpcClock.cs`, `BurnInCompositor.cs`, `StudioTakeRecorder.cs`, `CameraMovieWriter.cs`, `ScratchStore.cs`, `Mp4Remuxer.cs`, `RecordingRecovery.cs`, `PowerRequest.cs`, `RecordingSounds.cs` |
| `Audio/` | `WasapiMicrophone.cs`, `ProcessLoopbackCapture.cs`, `AudioDeviceService.cs` (`IAudioInputService`), `PcmFrames.cs`, `AudioLevelMeter.cs` |
| `Camera/` | `MfCameraService.cs` (`ICameraService`), `CameraCapture.cs`, `CameraFeed.cs` |
| `Input/` | `HookThread.cs`, `MouseEventSource.cs`, `KeyEventSource.cs` (`IInputEventSource`), `KeyTranslator.cs`, `SecureInputProbe.cs` (UIA) |
| `Media/` | `MfMediaMetadata.cs` (`IMediaMetadataSource`), `MfGifEncoder.cs`, `MfVideoTrimmer.cs`, `MfTranscoder.cs`, `SystemMediaSink.cs` (`IMediaSink`: clipboard file, save, move, copy, Recycle Bin, delete) |
| `Overlay/` (extend, new files only) | `RecordingSelectionPainter.cs`, `CountdownWindow.cs`, `RecordingFrameWindow.cs` |
| `Notifications/` | `NotificationStateProbe.cs` |

Create under `src/Lightshot.App/`:

| Path | Purpose |
|---|---|
| `Views/Recording/RecordingToolbar.xaml(.cs)`, `RecordingToolbarViewModel.cs` | Mode, region size entry, toggles (mic, computer audio, camera, clicks, keystrokes), Studio Mode row, countdown, Start Video (Return), Start GIF (Alt+Return), device menus, warning badges |
| `Views/Recording/ControlsPill.xaml(.cs)` | Pause, resume, stop, restart, discard, meters; no-activate; excluded from capture |
| `Views/Recording/CameraBubbleWindow.xaml(.cs)`, `CameraBubbleController.cs` | Draggable bubble, size/shape/mirror, click for fullscreen |
| `Views/Recording/PostRecordingOverlay.xaml(.cs)` | Looping muted preview, rename, copy, save, drag-out, trash, auto-save after 20 s |
| `Views/Recording/MediaViewerWindow.xaml(.cs)` | In-app viewer replacing Quick Look (Space opens it) |
| `Views/Recording/ProgressPopup.xaml(.cs)` | GIF conversion and "preparing recording" |
| `Views/VideoEditor/VideoEditorWindow.xaml(.cs)`, `TrimBar.xaml`, `VideoEditorViewModel.cs` | Plain-movie editor |
| `Views/Settings/RecordingPane.xaml(.cs)`, `ClickHighlightPreview.xaml` | Screen Recording settings pane |
| `Views/Notices/NotificationGuidanceBanner.xaml(.cs)` | Hide-notifications guidance |

Create tests under `tests/Lightshot.Platform.Windows.Tests/`: `RecordingDisplayResolverTests`, `QpcClockTests`, `PauseClockTests`, `CadenceDriverTests`, `KeyTranslatorTests`, `HookQueueTests`, `EncoderSelectorTests`, `ScratchStoreTests`, `RecordingRecoveryTests`, `MfFragmentedWriterTests`, `AudioSyncTests`, `ProcessLoopbackTests`, `MfGifEncoderTests`, `MfVideoTrimmerTests`, `RecordingOwnChromeTests`, `NotificationStateProbeTests`, `SecureInputProbeTests`, `BurnInCompositorTests` (if reachable per phase 3 decision); `tests/Lightshot.App.Tests/{RecordingToolbarViewModelTests, VideoEditorViewModelTests, PostRecordingOverlayViewModelTests}`; `tests/Lightshot.App.UiTests/RecordingFlowTests`.

Modify: `AppController.cs` (recording `CaptureUI` members), `Tray/TrayMenu.cs` (Record Screen, recording timer row, Stop), `SettingsViewModel.cs`. (Hotkeys: record, pause/resume, restart have no default chords, nothing to change in Core).

## Implementation steps

1. Empirical check first: log `SHQueryUserNotificationState` while toggling Do Not Disturb on this build; decide detection vs once-per-session banner.
2. R2 core: `ScratchStore` (`%LocalAppData%\Lightshot\Recordings`), `QpcClock`, `PauseClock`, `FrameCadencePlanner` driver. DDA source on the output's adapter with `AcquireNextFrame` timeouts as "no change"; recreate duplication on `DXGI_ERROR_ACCESS_LOST` (UAC secure desktop, mode change); abort cleanly with a message on resolution change mid-take.
3. Encoder: `EncoderSelector` probes hardware H.264/HEVC MFTs, falls back to the software H.264 encoder; sink writer configured with D3D manager, average bit rate from `VideoBitRate`, GOP 2 s, fragmented MP4 container; per-stream AAC; write progress to the session; disk-full maps to `RecordingError.diskFull`.
4. Finalise: end the session at the stop timestamp taken before stopping capture (static tail kept), then `Mp4Remuxer` (passthrough source reader to sink writer) producing a faststart MP4, delete the partial; on remux failure deliver the fragmented file as is.
5. R3 audio: mic via WASAPI (shared mode, 48 kHz float), loopback via process-exclude activation; both stamped on QPC; loopback sends no packets when silent, so `AudioMixer` fills gaps by timestamp; disconnect notification raises `audioSourceLost` while video continues.
6. R4 camera: MF source reader on the selected device; frames feed the bubble and, for Studio takes, a 30 fps camera movie sampled by a timer so static scenes do not truncate it; open failure costs only the bubble and shows a Privacy-settings link.
7. R5 input: hook thread with its own message loop; callbacks write to a bounded lock-free queue and return; a consumer thread builds events with source-second stamps; mouse position seeded at start; keys require the toggle only (no permission on Windows); `SecureInputProbe` subscribes to UIA focus events.
8. R1 chrome: recording selection with 8 handles, 1 px and 10 px nudge, aspect lock with the ratios in Core `EditableSelection`, typed size, window pick records its area; default rect is a centred 720p area; `RecordingFrameWindow` draws the 3 px red border pulsing 1..0.3 and dims outside (raw HWND + DComp, click-through, excluded from capture); `CountdownWindow` is click-through so the user can interact with the target app.
9. R6 burn-in: reachability decided in phase 3 step 5 from cloned source and recorded in `docs/porting/defaults.md`. If reachable, implement R6 (draw order: camera bubble, click highlights, keystroke pills over blurred backdrop with D2D/DirectWrite into BGRA render target before video processor) and include `BurnInCompositor.cs` and `BurnInCompositorTests.cs`; if unreachable from any setting, drop R6 (or implement for GIF path only per `docs/porting/defaults.md`).
10. R7 recovery and GIF: `RecordingRecovery` at launch per the rules above; `MfGifEncoder` reads the finished MP4 (or Studio render in phase 8) frame by frame via source reader and video processor, reuses a RGBA canvas, posterises, optimises transparency, quantises with Core `GifQuantizer`, writes via Core `GifWriter` to `.partial`.
11. R8 post-recording overlay and video editor as listed; HEVC visibility rule applied to codec pickers.
12. R9 settings pane: every `RecordingDefaults` field (codec, fps, max resolution, scale, GIF fps/width/quality/optimise, audio options and volumes, mono, separate tracks, cursor, click highlight style/colour/size/animate, keystroke mode/position/size/appearance/blur, camera shape/size/mirror/anchor, controls position, show controls, show time, dim screen, confirm discard, hide notifications, hide desktop icons, countdown, sounds). Recording timer shows in the tray tooltip and the menu row (a tray icon cannot show text), with a red-dot icon swap.
13. Run the spike B scenario again on the product code and compare numbers.

## Acceptance criteria

| Criterion | Test |
|---|---|
| Session state machine, illegal transitions, pause excluded from elapsed | `Lightshot.Core.Tests.RecordingSessionTests` (13) |
| Options resolution and defaults | `Lightshot.Core.Tests.RecordingOptionsTests` (18) |
| Constant-fps cadence from idle source | `Lightshot.Core.Tests.FrameCadencePlannerTests.RepeatsLastFrameWhenSourceIdle` (phase 3) |
| Pause offset removes the gap on resume | `Lightshot.Platform.Windows.Tests.PauseClockTests.ShiftsSamplesAfterResume` (`Unit`) |
| QPC conversion to source seconds | `Lightshot.Platform.Windows.Tests.QpcClockTests.ConvertsToSourceSeconds` (`Unit`) |
| Rect on a non-primary display records that display | `Lightshot.Platform.Windows.Tests.RecordingDisplayResolverTests.PicksLargestOverlapNotPrimary` (`Unit`, synthetic two-display topology with secondary at negative origin); ported `Lightshot.Core.Tests.RecordedAreaTests` (3); live multi-monitor UNCOVERED on single-monitor host |
| File plays; fragmented MP4 written | `Lightshot.Platform.Windows.Tests.MfFragmentedWriterTests.WritesPlayableFragmentedMp4` (`Media`) |
| A/V drift under 40 ms, dropped frames under 1%, CPU under 15% over 60 s at 1440p60 | `Lightshot.Platform.Windows.Tests.AudioSyncTests.DriftUnder40MsOver60Seconds` (`Media`, hard-coded thresholds 40 ms, 1%, 15% in test; no configured budget file) |
| Own audio excluded from loopback | `Lightshot.Platform.Windows.Tests.ProcessLoopbackTests.ExcludesOwnProcessAudio` (`Media`) |
| Killed take is recovered and remuxed | `Lightshot.Platform.Windows.Tests.RecordingRecoveryTests.RemuxesKilledTake` (`Media`), `RecordingRecoveryTests.DeletesPartialGifAndTooShortTakes` (`Unit`, temp dir) |
| Hooks never block: callback only enqueues | `Lightshot.Platform.Windows.Tests.HookQueueTests.CallbackNeverBlocks` (`Unit`, fake hook feeder) |
| Keystroke labels, repeat counts, typed chars, secure input blanks | `Lightshot.Core.Tests.KeystrokeOverlayTests` (12), `Lightshot.Platform.Windows.Tests.KeyTranslatorTests.ShiftOneReadsShiftOne` (`Unit`), `SecureInputProbeTests.PasswordFieldRaisesSecureInput` (`Desktop`) |
| Pill, frame, countdown, camera preview absent from frames | `Lightshot.Platform.Windows.Tests.RecordingOwnChromeTests.ChromeAbsentFromRecordedFrames` (`Desktop`) |
| Desktop icons covered and cover removed on every end path | `Lightshot.Platform.Windows.Tests.DesktopCoverTests.IconsAbsentFromStillAndCoverRemoved` (phase 5) plus `RecordingFlowTests.CoverRemovedOnCancelAndFailure` (`Desktop`) |
| Notification guidance shown when setting on and notifications would appear | `Lightshot.Platform.Windows.Tests.NotificationStateProbeTests.MapsQunsStatesToGuidance` (`Unit`, fake API) |
| GIF plan, delay and frame count, posterise, transparency within 8/255, cancel removes partial | `Lightshot.Core.Tests.GifFramePlanTests` (5), `Lightshot.Rendering.Tests.GifWriterTests.RoundTripsFramesAndDelays` (phase 3), `Lightshot.Platform.Windows.Tests.MfGifEncoderTests.CancelRemovesPartial` (`Media`) |
| Trim range minimum 0.5 s, dimension presets, bit rate and size estimate | `Lightshot.Core.Tests.VideoEditTests` (5), `Lightshot.App.Tests.VideoEditorViewModelTests.EstimatesSizeAndEnforcesMinimumTrim` (`Unit`) |
| Trim Only passthrough keeps streams and snaps to key frame | `Lightshot.Platform.Windows.Tests.MfVideoTrimmerTests.PassthroughSnapsToPrecedingKeyFrame` (`Media`) |
| Muted mic detection | `Lightshot.Core.Tests.MutedMicrophoneDetectorTests` (3) |
| Camera bubble sizes, corners, cover rect | `Lightshot.Core.Tests.CameraBubbleTests` (7) |
| Post-recording auto-save at 20 s and delete to Recycle Bin | `Lightshot.App.Tests.PostRecordingOverlayViewModelTests.AutoSavesAfter20Seconds` (`Unit`, fake clock) |
| Start, pause, stop end to end | `Lightshot.App.UiTests.RecordingFlowTests.StartPauseStopProducesPlayableFile` (`Desktop`) |
| Recording survives encoder fallback when hardware MFT is absent | `Lightshot.Platform.Windows.Tests.EncoderSelectorTests.FallsBackToSoftwareH264` (`Unit`, fake MFT list) |
| Burn-in pixels (bubble, rings, pills) | `Lightshot.Platform.Windows.Tests.BurnInCompositorTests.DrawsRingsPillsAndBubble` (`Gpu`, WARP; conditioned on phase 3 reachability decision in `docs/porting/defaults.md`, else dropped) |
| Keystroke capture in elevated windows | UNCOVERED: needs an elevated target; documented limitation |
| Camera on real hardware, Bluetooth mic loss, sleep/lock during a take | UNCOVERED: hardware and OS state; manual checklist |
| Visual quality of pulse, dim, pills | UNCOVERED: visual; manual checklist |

## Rollback

The service sits behind `IRecordingService`; revert the phase to restore the screenshot-only app. Scratch files live under `%LocalAppData%\Lightshot\Recordings` and can be deleted. Recording settings keys are additive. If hardware encoders prove unreliable, the software H.264 path stays as the default (flag in `EncoderSelector`).

## Risks

| Risk | L | I | Mitigation |
|---|---|---|---|
| A/V drift or drops at 1440p60 | M | H | Spike B gates; dedicated threads; GPU-only conversion |
| Fragmented MP4 not playable mid-file or remux fails | M | H | Spike B; FFmpeg muxing-only fallback (LGPL dynamic) |
| NAudio lacks process loopback | M | M | Direct COM implementation prepared in the spike |
| DDA access lost on UAC prompt or lock screen | H | M | Recreate duplication; black segment is acceptable and noted |
| HEVC decode missing on user machines | M | M | Detect; default H.264; hide HEVC |
| Low-level hook flagged by antivirus or delayed | M | M | Enqueue only; no network; document; hooks are installed only while a recording runs; submit each release to the Microsoft Defender false-positive portal; scan with VirusTotal before publishing; open-source build instructions |
| Hardware encoder variance across vendors | M | M | Software fallback; `SizeEstimator` constants re-tuned from measured output |
| Multi-monitor path untested on single-monitor host | H | M | Unit tests on `DisplayMath`; mark Media test UNCOVERED until a second display exists |
| Notification state API not reflecting Focus on 26200 | M | L | Step 1 empirical check; banner fallback |
| Scope size (30–40d) | H | M | Packages R1..R9 each shippable behind the Record Screen menu item |

## Dependencies

Phase 2 verdicts A, B, D. Phase 4 (overlay layer, capture, tray, sinks). Phase 5 (settings framework, `DesktopCover`, History, Quick Access). Phase 3 (recording and Studio-side models, GIF writer, cadence planner). Blocks phase 8.
