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

# Phase 7 Manual Verification Checklist

This document details the manual verification procedures for acceptance rows in Phase 7 (Screen Recording) that cannot be reliably covered by headless or automated test suites.

## Row 1: Visual quality of toolbar, pill, frame pulse and dim
- **Scope**: Topmost toolbar window, ControlsPill position and appearance, 3 px pulsing red border, desktop dimming.
- **Preconditions**:
  - `Lightshot.App` is running.
- **Steps**:
  1. Trigger screen recording via tray menu or hotkey.
  2. Select an area on screen and press Enter.
  3. Observe the `RecordingToolbarWindow` positioned at the bottom center of the work area, 24 DIP above the bottom.
  4. Click "Record Video" and observe the 3-2-1 countdown.
  5. While recording is active, inspect the recording frame border (3 px red border pulsing when active, solid when paused) and surrounding screen dimming if enabled in settings.
  6. Inspect `ControlsPill` floating near the top/bottom of screen.
- **Expected Results**:
  - Toolbar, countdown, frame, and pill render cleanly with proper themes and zero visual artifacts.
  - Border pulses smoothly during active recording and becomes steady upon pause.

## Row 2: Tray menu clicks, timer tooltip, and live red-dot swap
- **Scope**: Tray icon glyph swap during recording, tooltip elapsed timer formatting, tray context menu recording commands.
- **Preconditions**:
  - `Lightshot.App` is running with tray icon visible.
- **Steps**:
  1. Right-click the Lightshot tray icon; verify "Record Screen" item appears with current shortcut chord.
  2. Click "Record Screen", confirm selection, and start recording.
  3. Observe tray icon immediately transforms into a high-visibility red dot icon (`0xFFFF3B30`).
  4. Hover cursor over the tray icon; verify tooltip reads `Lightshot - Recording mm:ss` with ticking elapsed time when `ShowRecordingTimeInMenuBar` is enabled.
  5. Right-click the tray icon while recording; verify "Record Screen" is replaced by disabled "Recording mm:ss" row and enabled "Stop Recording" row.
  6. Click "Stop Recording".
- **Expected Results**:
  - Tray icon transitions to red dot on start and reverts to standard feather icon upon stop.
  - Tooltip accurately formats and advances elapsed seconds.
  - Context menu reflects active recording state and properly triggers stop.

## Row 3: Microphone disconnect prompt, Bluetooth loss, sleep/lock during a take
- **Scope**: Audio hardware detachment, WASAPI device invalidation, power/session interruption handling.
- **Preconditions**:
  - External USB microphone or Bluetooth headset connected and selected as recording input.
  - Active screen recording with microphone audio enabled.
- **Steps**:
  1. Unplug the USB microphone or disconnect Bluetooth audio while recording is in progress.
  2. Observe the application response.
  3. In the prompted dialog ("The microphone was disconnected. Continue recording without audio?"), choose "OK" to continue or "Cancel" to stop.
  4. Repeat in a new take and test locking Windows (`Win+L`) or putting the machine to sleep.
- **Expected Results**:
  - Audio device loss is gracefully caught without crashing the application or encoder pipeline.
  - The confirmation dialog appears; continuing writes silent audio or drops the microphone track, and stopping finalizes the take intact.

## Row 4: Notification guidance banner presentation and Do Not Disturb
- **Scope**: `NotificationGuidanceBanner` display logic, `SHQueryUserNotificationState` detection, once-per-session enforcement.
- **Preconditions**:
  - Windows Do Not Disturb / Focus Assist is enabled or disabled.
  - Recording Defaults configured with `HideNotifications = true`.
- **Steps**:
  1. Enable Windows Do Not Disturb / Focus Assist in Windows Settings.
  2. Start a screen recording selection.
  3. Observe whether the guidance banner appears above the recording toolbar linking to notification settings.
  4. Dismiss the banner or complete the take, then trigger another recording within the same app session.
  5. Repeat with Do Not Disturb turned off.
- **Expected Results**:
  - The banner appears when Windows Do Not Disturb is enabled and `HideNotifications` is active, alerting the user to Windows notification behavior.
  - The banner displays at most once per application session.

## Row 5: GIF conversion progress, cancel, and keep-video
- **Scope**: ProgressPopup presentation, cancel callback routing, and GIF-to-video fallback prompt.
- **Preconditions**:
  - `Lightshot.App` is running.
- **Steps**:
  1. Select a recording region and click "Record GIF" on the toolbar.
  2. Record a ~5 second take and click Stop.
  3. Observe the "Converting to GIF" modal progress popup displaying live conversion progress.
  4. Click "Cancel" on the progress popup.
  5. When prompted "GIF conversion was cancelled. Keep the video instead?", select "Yes".
  6. Inspect `save.location` or post-recording overlay.
- **Expected Results**:
  - The GIF progress popup tracks encode progress accurately.
  - Cancelling cleanly aborts GIF encoding and prompts whether to retain the source MP4 take.
  - Selecting "Yes" saves the original MP4 without data loss.

## Row 6: Sounds follow the "Play sounds" setting
- **Scope**: Audio cues for countdown ticks and start/stop recording events.
- **Preconditions**:
  - System audio output enabled.
- **Steps**:
  1. Open Lightshot Settings -> Recording and ensure "Play sounds" is enabled.
  2. Start a recording with countdown enabled; listen for audio beeps during countdown and start tone.
  3. Stop recording; listen for completion tone.
  4. Open Settings -> Recording and toggle "Play sounds" to OFF.
  5. Start and stop another recording.
- **Expected Results**:
  - Audio cues play during countdown and start/stop when enabled.
  - Zero audio cues play when "Play sounds" is disabled.

## Row 7: Video editor opens from post-recording overlay with accurate duration
- **Scope**: Post-recording overlay "Edit" action, metadata extraction, VideoEditorWindow launch.
- **Preconditions**:
  - A video recording take has just been completed with `AfterRecording = ShowOverlay`.
- **Steps**:
  1. Stop an active recording to bring up the `PostRecordingOverlay`.
  2. Click the "Edit" button (`PostRecordingEditButton`).
  3. Observe `VideoEditorWindow` opening.
  4. Inspect the timeline slider, duration label, and preview canvas.
- **Expected Results**:
  - `VideoEditorWindow` launches smoothly without blocking the UI dispatcher.
  - Duration and video dimensions match the recorded take's actual Media Foundation metadata.

## Row 8: Recording hotkeys fire globally
- **Scope**: Global system hotkeys for Record Screen, Pause/Resume Recording, and Restart Recording.
- **Preconditions**:
  - Lightshot running in background with non-conflicting hotkeys assigned in Settings -> Shortcuts (e.g., `Ctrl+Shift+R` for Record, `Ctrl+Shift+P` for Pause/Resume, `Ctrl+Shift+X` for Restart).
- **Steps**:
  1. Focus another application (e.g., Notepad or browser).
  2. Press the Record Screen hotkey (`Ctrl+Shift+R`).
  3. Confirm the selection overlay activates.
  4. Start recording, then while another app is focused, press Pause/Resume (`Ctrl+Shift+P`).
  5. Press Pause/Resume again to resume.
  6. Press Restart (`Ctrl+Shift+X`) and confirm the restart dialog.
- **Expected Results**:
  - Hotkeys trigger reliably from any foreground application window.
  - Recording pauses, resumes, and restarts according to the configured chords.

# Update and Release Manual Verification Checklist

This section details manual verification procedures for the update and release pipeline (rows M1-M5) that require interactive desktop environments, Windows Sandbox, or visual inspection.

## Row M1: Installed app starts, the hotkey works, uninstall removes the Run value
- **Scope**: Clean installation, background launch, global hotkey registration, and uninstallation cleanup.
- **Preconditions**:
  - Fresh Windows Sandbox instance or throwaway user profile without Lightshot installed.
  - Built release artifact `LightshotApp-win-Setup.exe`.
- **Steps**:
  1. Run `LightshotApp-win-Setup.exe --silent` in the sandbox.
  2. Verify the application installs to `%LocalAppData%\LightshotApp\current\Lightshot.App.exe`.
  3. Start the application with `--background`.
  4. Enable "Launch Lightshot at login" in Settings -> General (or verify HKCU Run key).
  5. Press `PrintScreen` to verify the capture overlay triggers.
  6. Signal quit via tray menu or `Local\Lightshot.Quit`.
  7. Run `%LocalAppData%\LightshotApp\Update.exe --uninstall`.
  8. When prompted "Also remove your Lightshot settings and capture history?", choose "No".
- **Expected Results**:
  - The application launches and operates cleanly from the per-user installation path.
  - The `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value is removed immediately on uninstall.
  - The installation directory `%LocalAppData%\LightshotApp` is deleted.
  - User data directory `%LocalAppData%\Lightshot` and settings are preserved.

## Row M2: Real upgrade across two versions
- **Scope**: Two-version upgrade lifecycle, consent timing, dot indicator, notice balloon, and sequence floor advancement.
- **Preconditions**:
  - Version 0.1.0 installed in a Windows Sandbox.
  - Version 0.2.0 release artifacts hosted on a local web server configured as the manifest URL.
- **Steps**:
  1. First launch of v0.1.0: verify no update check occurs and no prompts appear (first-launch consent rule).
  2. Restart Lightshot to simulate the second launch.
  3. Observe the tray icon indicator: a blue dot appears in the bottom-right corner.
  4. Observe the system notification balloon: "Lightshot update ready".
  5. Right-click the tray icon; verify "Restart to Update (0.2.0)" appears above Quit.
  6. Select "Restart to Update (0.2.0)".
- **Expected Results**:
  - Lightshot closes, Velopack applies the staged v0.2.0 package, and the app restarts.
  - The running version is now 0.2.0.
  - The update sequence floor advances to v0.2.0's sequence number, preventing replay of v0.1.0.

## Row M3: SmartScreen "More info -> Run anyway" and Smart App Control blocking
- **Scope**: Windows Defender SmartScreen warning dialog and Smart App Control behavior on unsigned binaries.
- **Preconditions**:
  - Standard Windows 11 system with Windows Defender SmartScreen enabled.
  - Fresh release executable `LightshotApp-win-Setup.exe`.
- **Steps**:
  1. Double-click `LightshotApp-win-Setup.exe`.
  2. Observe the Windows Defender SmartScreen prompt ("Windows protected your PC").
  3. Click "More info".
  4. Verify the publisher is listed as "Unknown publisher" and the app name matches.
  5. Click "Run anyway".
  6. On a system with Smart App Control (SAC) in enforcement mode: verify SAC blocks execution without "Run anyway".
- **Expected Results**:
  - SmartScreen behaves as expected for unsigned open-source binaries.
  - Clicking "Run anyway" allows the installer to proceed normally.
  - SHA-256 matches `SHA256SUMS.txt`.

## Row M4: Tray dot and balloon appearance on light and dark taskbars
- **Scope**: Visual clarity and contrast of the update dot indicator and notification balloon across themes and display scalings.
- **Preconditions**:
  - Windows 11 host with taskbar theme toggling (Light / Dark) and display scaling support (100%, 150%, 200%).
- **Steps**:
  1. Set Windows taskbar to Dark mode (`SystemUsesLightTheme = 0`).
  2. Trigger update staged state; inspect the tray icon dot at 100% and 150% scaling.
  3. Switch Windows taskbar to Light mode (`SystemUsesLightTheme = 1`).
  4. Inspect the tray icon dot again.
  5. Trigger `ShowNotice` and inspect the system notification balloon title and text.
- **Expected Results**:
  - The blue update dot (`#0A84FF`) is distinctly visible with crisp edges on both dark and light taskbars.
  - The base glyph is preserved without distortion or scaling artifacts.
  - Notification balloon displays complete title and body text without truncation.

## Row M5: Uninstall prompt behavior and data retention
- **Scope**: User prompt options during uninstallation and safety of unfinished recordings.
- **Preconditions**:
  - Installed Lightshot instance with existing `%LocalAppData%\Lightshot\Lightshot Recordings\take.mp4`.
- **Steps**:
  1. Trigger uninstallation via `%LocalAppData%\LightshotApp\Update.exe --uninstall`.
  2. When the confirmation prompt appears, let it time out or select "No".
  3. Verify `%LocalAppData%\Lightshot\Lightshot Recordings\take.mp4` still exists.
  4. In a separate test run: select "Yes" when scratch recordings exist.
- **Expected Results**:
  - The dialog defaults to "No" (safe default).
  - An unanswered prompt keeps all user data.
  - Unfinished recordings in `%LocalAppData%\Lightshot\Lightshot Recordings` are preserved even when the user chooses to remove data.
  - The HKCU Run registry value is deleted regardless of the prompt choice.
