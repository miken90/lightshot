# Phase 4 Manual Verification Checklist

This document details the manual verification procedures for acceptance rows in Phase 4 that cannot be reliably covered by headless or automated test suites (e.g., native Win32 popup menus, HDR/mixed-DPI physical monitors, visual appearance checks).

## Row 1: Tray menu rebuilds on open with live chords
- **Scope**: Native `Shell_NotifyIcon` context menu, dynamic chord binding display, menu action routing.
- **Preconditions**:
  - `Lightshot.App` is running.
  - Tray icon is visible in system notification area (or notification overflow).
- **Steps**:
  1. Right-click the Lightshot notification tray icon.
  2. Observe the popup context menu items:
     - Area Capture (displays the current bound chord, e.g., `PrintScreen`)
     - Fullscreen Capture submenu (listing each connected monitor and `Ctrl+PrintScreen`)
     - Open Existing Image...
     - Settings (placeholder / disabled)
     - History (placeholder / disabled)
     - Exit / Quit
  3. Close the menu by clicking outside.
  4. Modify the hotkey settings in `settings.json` (e.g., set Area Capture to `Ctrl+Shift+A`).
  5. Right-click the tray icon again.
  6. Verify the menu reflects the updated live chord (`Ctrl+Shift+A`).
  7. Click "Area Capture" from the tray menu.
- **Expected Results**:
  - The tray popup menu appears immediately without visual distortion.
  - All hotkey chords accurately match active registered bindings.
  - Clicking "Area Capture" invokes `AppCoordinator.CaptureArea` and displays the capture overlay.

## Row 2: HDR and mixed-DPI visual correctness
- **Scope**: Multi-monitor topology with varying DPI scalings (e.g., 100%, 150%, 200%) and HDR (scRGB / BT.2100) displays.
- **Preconditions**:
  - Host connected to at least two physical monitors with different DPI scale settings (e.g., primary at 150%, secondary at 100%) or an HDR-enabled monitor.
- **Steps**:
  1. Trigger area capture using `PrintScreen`.
  2. Verify that every connected monitor is covered by a dedicated fullscreen overlay window.
  3. Drag a selection rectangle starting on Monitor 1 (150% DPI) and extend it across the boundary into Monitor 2 (100% DPI).
  4. Release the mouse to form the capture region.
  5. Inspect the frozen backdrop on both monitors.
  6. On an HDR monitor: inspect brightness and tone mapping.
- **Expected Results**:
  - Overlays align seamlessly at physical display boundaries with zero gaps or scaling stretching.
  - Backdrop images are crisp and pixel-accurate at native monitor resolutions without blur.
  - HDR highlights are tone-mapped to SDR BGRA without severe blowout, posterization, or black crushing.
  - The editor opens with the exact captured region preserved in 1:1 physical pixels.

## Row 3: Pixel readout and handle visuals
- **Scope**: Overlay selection adorner, dimension/coordinate badge, resize handles, and guideline rendering.
- **Preconditions**:
  - Area capture overlay is active.
- **Steps**:
  1. Press `PrintScreen` to activate the selection overlay.
  2. Click and drag to create an arbitrary rectangle (e.g., ~400x300 pixels).
  3. Observe the live pixel readout tooltip/badge displayed near the cursor during drag.
  4. Release mouse button to enter adjustable selection mode.
  5. Inspect the selection border, corner handles, and midpoint handles:
     - 4 corner resize handles
     - 4 edge midpoint resize handles
     - Dimension badge displaying `[Width] x [Height]` in physical pixels
  6. Hover over each handle and verify cursor switches to the appropriate resize direction (`SizeNWSE`, `SizeNESW`, `SizeWE`, `SizeNS`).
  7. Drag a corner handle to enlarge the rectangle; verify the dimension badge updates in real time.
  8. Use arrow keys (`Left`, `Right`, `Up`, `Down`) to nudge the selection 1 physical pixel; hold `Shift` + arrow keys to nudge 10 physical pixels.
  9. Press `Return` to confirm the selection.
- **Expected Results**:
  - Pixel readout badge remains visible, readable, and smoothly follows cursor movements during drag.
  - Adorner handles are clearly delineated, correctly positioned, and distinct from the darkened overlay background.
  - Handles respond accurately to mouse drags and arrow key nudges.
  - Handles, adorners, and dimension badges are purely UI chrome and are completely absent from the final captured bitmap passed to the editor.
