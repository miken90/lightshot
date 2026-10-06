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

# Phase 5 Manual Verification Checklist

This section details manual verification procedures for Phase 5 integration features (visual quality, card slide-in, tray overflow, onboarding, launch-at-login after reboot, live app icon in Explorer and taskbar).

## Row 4: Light/Dark Visual Quality across Panes and Cards
- **Scope**: Window chrome, contrast, DWM immersive dark mode, theme dictionary switching.
- **Preconditions**: `Lightshot.App` is running.
- **Steps**:
  1. Open Settings window from the tray icon.
  2. In the General pane, switch Appearance from "System" to "Light".
  3. Verify Settings window, Pin windows, History window, and Quick Access cards immediately adopt light palette tokens (light background, dark text/glyphs, high contrast borders).
  4. Switch Appearance to "Dark".
  5. Verify all windows and cards update immediately to dark palette tokens (dark panel backgrounds, white/light text/glyphs, DWM title bar dark mode enabled).
  6. Switch Appearance to "System" and toggle the Windows system apps theme in Windows Settings (Personalization -> Colors).
- **Expected Results**:
  - Theme switches instantly across all open windows and newly opened windows without restart.
  - Text and iconography maintain WCAG AA contrast ratio (> 4.5:1) in both modes.
  - No flickering or unstyled white/black flashes during theme transition.

## Row 5: Quick Access Card Slide-In Animation & Responsive Stacking
- **Scope**: Quick Access card entry animation, auto-close timer, hover pause, overflow eviction.
- **Preconditions**:
  - Settings -> After Capture -> "Open in Editor" is unchecked (Quick Access mode enabled).
- **Steps**:
  1. Trigger area capture using `PrintScreen` and confirm a region.
  2. Observe the bottom-right (or configured side) corner of the primary monitor.
  3. Notice the smooth slide-in translation animation of the card window.
  4. Hover cursor over the card: verify action pills (Copy, Save) and corner buttons (Close, Pin, Annotate) appear smoothly.
  5. Hold cursor over the card past the configured auto-close interval (e.g., 10s); verify the card does not dismiss while hovered.
  6. Move cursor away; verify the auto-close timer resumes.
  7. Take multiple consecutive captures (5+) rapidly to fill the display height.
- **Expected Results**:
  - Cards slide in smoothly with no stutter or clipping against taskbar/work area bounds.
  - Oldest cards cleanly animate/evict off-screen when the stack exceeds the available vertical work area height.
  - Hovering pauses dismissal; moving away resumes countdown.

## Row 6: Tray Overflow Guidance Notification
- **Scope**: Notification area placement, tray icon visibility in Windows taskbar overflow pane.
- **Preconditions**: `Lightshot.App` is running.
- **Steps**:
  1. Launch `Lightshot.App`.
  2. Observe the Windows system notification area.
  3. If Windows places the icon into the hidden icons overflow chevron (`^`), drag it out onto the visible taskbar area or click the chevron to view it.
- **Expected Results**:
  - Lightshot icon appears crisply in the tray and overflow pane with native resolution.
  - Left-clicking triggers Area Capture; right-clicking opens the contextual menu.

## Row 7: First-Launch Onboarding Dialog & Snipping Tool Claim Banner
- **Scope**: Onboarding window first-run presentation, Snipping Tool PrintScreen detection banner.
- **Preconditions**: Clean profile (delete or clear `settings.json` so `app.onboarded` is false).
- **Steps**:
  1. Launch `Lightshot.App` without command-line arguments.
  2. Observe the initial window presented on desktop.
  3. If Windows 11 has PrintScreen mapped to Snipping Tool in registry, verify the warning banner appears with the button linking to `ms-settings:easeofaccess-keyboard`.
  4. Click "Get Started".
  5. Verify the onboarding window closes and `settings.json` records `"app.onboarded": "true"`.
  6. Re-launch `Lightshot.App` normally.
- **Expected Results**:
  - Onboarding window appears centered on first run only.
  - Subsequent app launches do not display the onboarding window.
  - Clicking "Get Started" successfully marks onboarding complete.

## Row 8: Launch-at-Login Persistence After Reboot
- **Scope**: Windows Startup registration via HKCU Run key and Task Manager Startup integration.
- **Preconditions**: `Lightshot.App` installed or running from deployment folder.
- **Steps**:
  1. Open Lightshot Settings -> General.
  2. Toggle "Launch at login" to ON.
  3. Verify the registry entry `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Lightshot` exists pointing to the executable.
  4. Open Windows Task Manager -> Startup apps; verify Lightshot is listed and enabled.
  5. Reboot or sign out/sign in to the Windows user session.
- **Expected Results**:
  - `Lightshot.App` automatically launches upon user login in the background with its tray icon active.
  - No unexpected windows or errors appear on login.

## Row 9: Live Application Icon in Explorer, Taskbar, and Start Menu
- **Scope**: Shell icon extraction, multi-size resolution rendering.
- **Preconditions**: `Lightshot.App.exe` packaged or built with Release configuration.
- **Steps**:
  1. Open Windows File Explorer and navigate to the directory containing `Lightshot.App.exe` and `Setup.exe`.
  2. Change Explorer view mode to: Extra Large Icons, Medium Icons, Details, and Small Icons.
  3. Pin `Lightshot.App` to Windows Taskbar and Start Menu.
  4. Launch the application and observe the active taskbar icon.
- **Expected Results**:
  - The application icon renders crisply with rich color gradient and clean edges at all sizes (16px to 256px).
  - No generic executable placeholder icon is shown at any zoom level or in Start Menu / Taskbar.
