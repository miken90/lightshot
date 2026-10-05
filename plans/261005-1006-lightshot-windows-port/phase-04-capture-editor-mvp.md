# Phase 4: Capture, overlay, hotkeys, tray, editor, clipboard and save (first usable app)

Status: done | Effort: 18d | Priority: P1 | Depends on: phase 3 (and the phase 2 verdicts)

## Overview

This is the MVP slice. A user presses a hotkey, drags an area (or picks a window or display), lands in the annotation editor, and copies or saves the result. It includes the manual redaction tool (blackout, blur, pixelate), adjustable selection and the self-timer. It excludes Quick Access (`openInEditor` is effectively always on until phase 5), history UI, pin, the full settings window, OCR and Auto Redact.

## Requirements

- Hotkey-to-first-overlay-pixel under 150 ms (RULING §10).
- Freeze before overlay; own chrome never appears in stills (SPECS invariant 7): overlays are created after the freeze, and live-path chrome uses `WDA_EXCLUDEFROMCAPTURE`.
- All monitors covered by one topmost raw HWND each (no single spanning window, no `AllowsTransparency`).
- Global physical-pixel coordinates under PerMonitorV2; per-display scale is used for UI sizing only.
- Capture errors are `Result`-typed and never produce a blank image; DRM or excluded windows (black frames) are detected and reported.
- Editor behaviours from SPECS 1.4: tools, arrow styles with remembered last style, text with width and corner-scale handles, steps, focus, crop with thirds grid, z-order, undo/redo, Ctrl+S copy-and-close with no file written, Ctrl+C copy-and-stay, Ctrl+Shift+S Save As PNG/JPEG, drag-out, open existing image, window 1180x800 capped at 90% x 85% of the work area.
- Selection handles and chrome are drawn in WPF above the bitmap and never exported.

## Data flow

1. `WM_HOTKEY` on the hidden hotkey window (shell thread) -> `AppCoordinator.CaptureArea`.
2. `WindowsCaptureService.FreezeScreen()`: per monitor DDA (or WGC per spike A) into a BGRA `PixelSurface`; window list via `EnumWindows` + DWM extended frame bounds + cloaked filter, excluding our own process; `FreezeWindowImages()` in parallel (WGC or `PrintWindow(PW_RENDERFULLCONTENT)`).
3. `WindowsOverlayController.SelectRegion(frozen, adjustable)`: one overlay HWND per monitor (D2D drawn into a DComp visual), frozen backdrop uploaded as a texture, dim, selection rectangle, live pixel readout, key handling in the window procedure; result is a physical-pixel `CaptureRegion` or null.
4. `FrozenScreen.ImageOf(region)` -> PNG at the sink boundary -> `CaptureUI.OpenEditor`.
5. Editor: `AnnotationDocument` commands -> `IImageRenderer` -> `SKElement` canvas; output -> `ImageSink` (clipboard with PNG and DIBV5, file with atomic write).

## Files

Create under `src/Lightshot.Platform.Windows/`:

| Path | Purpose |
|---|---|
| `Interop/NativeMethods.txt` (CsWin32 list), `Interop/Dpi.cs`, `Interop/Win32Window.cs` | Window class, message loop helper, DPI helpers |
| `Displays/DisplayTopology.cs`, `Displays/DisplayInfo.cs`, `Displays/DisplayMath.cs` | Monitors, adapters (LUID), physical bounds, scale; pure math in `DisplayMath` |
| `Capture/WindowsCaptureService.cs`, `Capture/DdaDisplayCapture.cs`, `Capture/WgcWindowCapture.cs`, `Capture/PrintWindowCapture.cs`, `Capture/CursorCompositor.cs`, `Capture/BlackFrameDetector.cs`, `Capture/WindowEnumerator.cs`, `Capture/HdrToneMap.cs` | Capture sources |
| `Shell/ShellThread.cs`, `Shell/UiDispatcher.cs` | Shell message-loop thread and `IUiDispatcher` implementation |
| `Overlay/OverlayHost.cs`, `Overlay/OverlayWindow.cs`, `Overlay/DCompSurface.cs`, `Overlay/SelectionPainter.cs`, `Overlay/WindowHoverPainter.cs`, `Overlay/WindowsOverlayController.cs`, `Overlay/ForegroundGrant.cs` | Raw overlay layer (reused by phase 7) |
| `Hotkeys/WindowsHotkeyService.cs`, `Hotkeys/HotkeyWindow.cs`, `Hotkeys/PrintScreenClaim.cs`, `Hotkeys/VirtualKeyNames.cs` | `RegisterHotKey`, conflict reporting, PrintScreen detection |
| `Tray/TrayIcon.cs`, `Tray/TrayMenu.cs`, `Tray/TrayIconAssets.cs` | `Shell_NotifyIcon` v4 plus native popup menu rebuilt on open; per-display Fullscreen submenu; light and dark glyphs |
| `Clipboard/ClipboardImageSink.cs`, `Clipboard/DibConverter.cs` | PNG + `CF_DIBV5` + text, retry on `CLIPBRD_E_CANT_OPEN` |
| `Files/FileImageSink.cs`, `Files/FileImageSource.cs`, `Files/KnownFolders.cs`, `Files/AtomicFile.cs` | Atomic write (temp + replace), open dialog, Desktop folder |
| `Settings/JsonSettingsStore.cs` | Minimal `ISettingsStore` (full UI in phase 5) |
| `Windows/WindowPresenter.cs` | Foreground fronting for editor windows |

Create under `src/Lightshot.App/`:

| Path | Purpose |
|---|---|
| `AppController.cs` | Composition root; implements the `CaptureUI` subset needed here |
| `Views/Editor/EditorWindow.xaml(.cs)`, `EditorViewModel.cs`, `ToolPalette.xaml`, `StyleBar.xaml`, `CanvasHost.cs` (`SKElement`), `SelectionAdorner.cs`, `ColorPopup.xaml`, `CropOverlay.cs`, `TextEditorBox.cs` | Annotation editor |
| `Views/Notices/ErrorDialog.xaml` | Capture failure and DRM-black notices |
| `Resources/Icons/*.xaml` | Original vector tool icons (no SF Symbols); art review gate per Boom rule 8 (cc-art, then Kongming frame review; never agy) |

Create tests: `tests/Lightshot.Platform.Windows.Tests/{DisplayMathTests.cs, DdaCaptureTests.cs, WindowEnumeratorTests.cs, OverlayLatencyTests.cs, OverlayExclusionTests.cs, HotkeyServiceTests.cs, ClipboardSinkTests.cs, FileImageSinkTests.cs, BlackFrameDetectorTests.cs}`, `tests/Lightshot.App.Tests/{EditorViewModelTests.cs, CanvasHostTests.cs}`, `tests/Lightshot.App.UiTests/EditorFlowTests.cs`, `docs/manual-checklist.md`.

Modify: `src/Lightshot.App/Program.cs`, `App.xaml.cs` (wire coordinator). (Core Hotkeys defaults resolved in phase 3: PrintScreen = area, Ctrl+PrintScreen = fullscreen; phase 4 registers them).

Sparse package fallback deleted: requires trusted code signing certificate, violating Q4. Fallback is DDA for displays (no border exists), `PrintWindow(PW_RENDERFULLCONTENT)` for window stills, accept WGC border for window recording as documented DEGRADE.

## Implementation steps

1. Shell thread and dispatcher; hotkey window; register defaults (PrintScreen = area, Ctrl+PrintScreen = fullscreen, resolved 2026-10-05; every other action unbound like the source) and report failed registrations via `register -> [CaptureAction]`.
2. Display topology: enumerate monitors with `EnumDisplayMonitors`, `GetDpiForMonitor`, adapter LUID via DXGI; subscribe to `WM_DISPLAYCHANGE` and DPI changes.
3. Capture sources per spike verdict; BGRA surfaces; cursor drawn with `GetCursorInfo` when `includeCursor`; black-frame detector raises a "protected content" notice; HDR frames tone-mapped.
4. Window picker: candidate list in z-order, DWM frame bounds minus shadow, filter cloaked, minimised and own windows; per-window clean image for occluded windows via WGC (fallback `PrintWindow`).
5. Overlay layer: window class, per-monitor HWND (`WS_EX_TOPMOST | WS_EX_TOOLWINDOW`, no `WS_EX_NOACTIVATE` for selection), `ForegroundGrant` obtains key focus from the `WM_HOTKEY` context, D2D painter draws backdrop, dim, rectangle, handles and pixel readout; Esc cancels, Return confirms, arrows nudge 1 px (Shift 10); bare click captures nothing; first-click-starts-drag on non-key monitors; adjustable-selection mode per SPECS 1.1; self-timer paths stay live and use `WDA_EXCLUDEFROMCAPTURE`.
6. Tray: icon and native menu (capture modes with live chords, open, history placeholder disabled, Settings placeholder, Quit); rebuild on open; per-display Fullscreen submenu; Windows 11 hides new tray icons in overflow, so first-run guidance is added in phase 5.
7. Clipboard sink: set `PNG`, `CF_DIBV5` (with alpha) and text on one open/close; retry with backoff; verify round trip with a second reader.
8. File sink: pattern `Screenshot %Y-%m-%d at %H.%M.%S`, sanitised for Windows, default Desktop, collision suffix, atomic write.
9. Editor shell: tools, style bar from `StyleFields.fields(for:)`, colour popup (palette + hex field, no WinForms), arrow style memory in settings key `editor.lastArrowStyle`, text tool with handles, steps, focus, crop, manual redaction (default Pixelate each open; intensity slider; "Not secure - visual only" label for blur and pixelate), z-order Ctrl+[ and Ctrl+], undo/redo, `CanvasHost` bound to the Rendering pipeline with cached base and redaction patches, WPF adorners for handles.
10. Output actions: Ctrl+S copy-and-close (no file), Ctrl+C copy, Ctrl+Shift+S Save As (WPF `SaveFileDialog`), drag-out (temp file + bitmap `DataObject`), open existing image (`ImageLoadError` mapped to dialogs).
11. `WindowPresenter` fronts every window (`SetForegroundWindow` with the Alt-key fallback from spike D).
12. `presentQuickAccess` is routed to the editor until phase 5 supplies cards (documented in `AppController.cs`).
13. Run `package.ps1` and install the build on the host; execute `docs/manual-checklist.md` once.

## Acceptance criteria

| Criterion | Test |
|---|---|
| Hotkey to first overlay pixel under 150 ms (median of 20) | `Lightshot.Platform.Windows.Tests.OverlayLatencyTests.HotkeyToFirstPixelUnder150Ms` (`Desktop`) |
| Hotkey registers, fires, and reports a conflicting registration | `Lightshot.Platform.Windows.Tests.HotkeyServiceTests.FiresOnChordAndReportsConflict` (`Desktop`) |
| Display math: mixed-DPI rect to physical pixels, largest-overlap display | `Lightshot.Platform.Windows.Tests.DisplayMathTests.MapsRectAcrossMixedDpi` (`Unit`) |
| Fullscreen capture equals each monitor's native size | `Lightshot.Platform.Windows.Tests.DdaCaptureTests.CapturesEachMonitorAtNativeSize` (`Desktop`) |
| Window list excludes own, cloaked and minimised windows, uses DWM bounds | `Lightshot.Platform.Windows.Tests.WindowEnumeratorTests.ExcludesOwnAndCloakedWindows` (`Desktop`) |
| Own overlay and cards absent from frames | `Lightshot.Platform.Windows.Tests.OverlayExclusionTests.ExcludedWindowAbsentFromDdaAndWgc` (`Desktop`) |
| Black (DRM) frames detected | `Lightshot.Platform.Windows.Tests.BlackFrameDetectorTests.FlagsAllBlackFrame` (`Unit`) |
| Clipboard holds PNG and DIBV5 readable by a second process | `Lightshot.Platform.Windows.Tests.ClipboardSinkTests.CopiesPngAndDib` (`Desktop`) |
| Save writes atomically with a sanitised, collision-free name | `Lightshot.Platform.Windows.Tests.FileImageSinkTests.WritesAtomicallyWithUniqueName` (`Unit`, temp dir) |
| Tool persists; new marks not auto-selected; any tool grabs an existing mark | `Lightshot.App.Tests.EditorViewModelTests.ToolPersistsAndNewMarkIsNotSelected` (`Unit`) |
| Ctrl+S copies and closes with no file written | `Lightshot.App.Tests.EditorViewModelTests.CopyAndCloseWritesNoFile` (`Unit`) |
| Redaction defaults to Pixelate on each open | `Lightshot.App.Tests.EditorViewModelTests.RedactionDefaultsToPixelate` (`Unit`) |
| Window cap 90% x 85% of work area | `Lightshot.App.Tests.EditorViewModelTests.WindowSizeCappedToWorkArea` (`Unit`) |
| Area capture, draw an arrow, copy and close end to end | `Lightshot.App.UiTests.EditorFlowTests.DrawArrowCopyAndClose` (`Desktop`, FlaUI) |
| Preview equals export in the editor | `Lightshot.Rendering.Tests.DocumentRenderTests.PreviewPatchEqualsExportPatch` (phase 3), asserted again by `Lightshot.App.Tests.CanvasHostTests.BackingBitmapEqualsExport` (`Render`; compares canvas backing `SKBitmap` at 100% with export bytes) |
| Esc cancels, bare click captures nothing, adjustable selection confirm/replace | Core `AppCoordinatorTests` plus `EditableSelectionTests` (phase 3); overlay key delivery covered by `Lightshot.Platform.Windows.Tests.OverlayLatencyTests.KeysReachOverlay` (`Desktop`) |
| Tray menu rebuilds on open with live chords | UNCOVERED by automation: native popup menus are not UIA-accessible reliably; `docs/manual-checklist.md` item |
| HDR and mixed-DPI visual correctness | UNCOVERED: needs HDR and mixed-DPI monitors; manual checklist item, host dependent |
| Pixel readout and handle visuals | UNCOVERED: visual check, manual checklist |

## Rollback

Disable the hotkey registration and tray (revert the `AppController` wiring commit); no data is persisted beyond `settings.json` keys with defaults. If the raw overlay path fails in the field, a feature flag `capture.useLegacyOverlay` is not provided (YAGNI); the rollback is reverting this phase.

## Risks

| Risk | L | I | Mitigation |
|---|---|---|---|
| Foreground not granted after the hotkey, so Esc does nothing | M | H | `ForegroundGrant` with Alt-key fallback; spike D proved it |
| Windows 11 claims PrintScreen | M | M | Detect the setting and show guidance; chord choice is settable (default PrintScreen = area, Ctrl+PrintScreen = fullscreen resolved 2026-10-05) |
| Hybrid GPU makes DDA fail on one monitor | M | H | Per-output adapter device; WGC fallback per spike A |
| Overlay latency over 150 ms on 3 x 4K | M | M | Surfaces stay on the GPU; pre-warmed hidden windows if needed |
| Window picker shows invisible or cloaked UWP windows | M | M | Cloaked and `DWMWA_CLOAKED` filter plus class denylist |
| DRM windows come out black | H | L | Detect and notify |
| Clipboard contention (another app holds the clipboard) | M | L | Retry with backoff, then error notice |
| Skia preview slow at 5K | M | M | Cache base and patches; redraw dirty elements only; measure in checklist |
| WGC border on unpackaged window capture | L | M | DDA for displays (no border); `PrintWindow` for window stills; accept border for window recording as documented DEGRADE (sparse package deleted) |

## Dependencies

Phase 3 (Core interfaces, Rendering, goldens), phase 2 verdicts for capture, overlay and hotkey choices. Blocks phases 5 to 10. The overlay layer is reused by phase 7.
