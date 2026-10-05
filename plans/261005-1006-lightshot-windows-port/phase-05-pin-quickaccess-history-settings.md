# Phase 5: Pin, Quick Access, history, settings, onboarding, light/dark, launch at login

Status: pending | Effort: 14d | Priority: P1 | Depends on: phase 4

## Overview

Turns the MVP into the full screenshot product: pinned screenshots, the Quick Access card stack, the History window, the Settings window (all panes that exist before recording), first-run onboarding, light/dark theming, launch at login, and the shared `DesktopCover` for hiding desktop icons.

## Requirements

- Pin: borderless always-on-top window; move, aspect-locked resize, close, copy, save; reachable from editor and Quick Access; Ctrl+S on a pin copies and the pin stays; pins stay in screenshots (SPECS 1.7, APP §3).
- Quick Access (SPECS 1.8): cards 220 DIP wide, 90..220 tall, margin 16, spacing 12, bottom-left or bottom-right of the work area; overflow closes oldest; auto-close never/10 s/30 s/1 min; hover shows Copy, Save, Close, Pin, Annotate; double-click annotates; context menu; drag-out closes the card unless Alt is held; cards excluded from captures.
- History (SPECS 1.9): screenshots and recordings with retention (default 50); reopen, reveal in Explorer, delete to Recycle Bin; index decodes backward-compatibly; OCR never added.
- Settings panes: General, Shortcuts, Screenshots, Advanced, About, plus the After Capture table. The Screen Recording pane arrives in phase 7. Settings are read at capture time (invariant 9).
- Onboarding: Windows has no screen-recording grant. First run shows a checklist for the real Windows needs: where the tray icon lives (pin it from overflow), the PrintScreen claim, and lazy microphone/camera privacy later.
- Light/dark: `AppearancePreference` system/light/dark; selection dim, recording border, countdown, camera bubble, letterbox and crop mask ignore appearance; renderers never see it (invariant 12).
- Launch at login via HKCU Run; the source of truth is the registry value, not a settings key.
- Hide desktop icons (screenshots): `DesktopCover` per monitor from `IDesktopWallpaper`, shown before the freeze and torn down on every path.

## Data flow

Capture completes -> `presentCapture` reads `openInEditor` live -> editor, or Quick Access card (thumbnail via `IThumbnailer`). Card actions call the same sinks as the editor. `HistoryStore.Add` writes `<uuid>.png`, `<uuid>-thumb.png` and `index.json` under `%LocalAppData%\Lightshot\History`. Settings UI writes `settings.json` through `ISettingsStore`; consumers read at use time.

## Files

Create under `src/Lightshot.Platform.Windows/`:

| Path | Purpose |
|---|---|
| `Settings/SettingsMigrations.cs`, `Settings/SettingsKeys.cs` | Keys from APP §5 with defaults; additive migrations |
| `Startup/LaunchAtLogin.cs` | HKCU Run value, `StartupApproved` detection |
| `DesktopCover/DesktopCover.cs`, `DesktopCover/WallpaperProvider.cs` | `IDesktopWallpaper` backed cover windows, one per monitor |
| `Windows/AppPaths.cs` | `%AppData%`, `%LocalAppData%\Lightshot` layout |
| `Recycle/RecycleBin.cs` | `IFileOperation` delete-to-bin (`MediaSink.Trash`) |
| `Windows/Explorer.cs` | Reveal in Explorer, open settings URIs |

Create under `src/Lightshot.App/`:

| Path | Purpose |
|---|---|
| `Views/Pin/PinWindow.xaml(.cs)`, `PinBoard.cs` | Pins with aspect-locked `WM_SIZING` hook |
| `Views/QuickAccess/QuickAccessHost.cs`, `CardWindow.xaml(.cs)`, `CardViewModel.cs` | No-activate, topmost, `WDA_EXCLUDEFROMCAPTURE`, DWM rounded corners, slide-in animation |
| `Views/History/HistoryWindow.xaml(.cs)`, `HistoryViewModel.cs` | Grid of thumbnails |
| `Views/Settings/SettingsWindow.xaml(.cs)`, `GeneralPane.*`, `ShortcutsPane.*`, `ScreenshotsPane.*`, `AdvancedPane.*`, `AboutPane.*`, `AfterCapturePane.*`, `HotkeyRecorder.xaml(.cs)`, `SettingsViewModel.cs` | Settings |
| `Views/Onboarding/OnboardingWindow.xaml(.cs)`, `OnboardingViewModel.cs` | First-run checklist (wraps ported `PermissionOnboardingModel`) |
| `Theming/ThemeService.cs`, `Theming/Themes/Light.xaml`, `Dark.xaml`, `Theming/PaletteBridge.cs` | `ThemeMode` Fluent plus `ThemePalette` tokens |
| `Resources/Icons/app.ico`, `tray-light.ico`, `tray-dark.ico` | Original icons derived from the upstream icon set (MIT, attributed). Art review gate per Boom rule 8 (`cc-art`, then Kongming frame review, never agy) |

Modify: `AppController.cs` (CaptureUI additions: Quick Access, pin, history), `Tray/TrayMenu.cs` (enable History and Settings), `EditorWindow.xaml.cs` (Pin action), `THIRD-PARTY-NOTICES.md`.

Create tests: `tests/Lightshot.Platform.Windows.Tests/{LaunchAtLoginTests.cs, JsonSettingsStoreTests.cs, SettingsMigrationTests.cs, RecycleBinTests.cs, DesktopCoverTests.cs}`, `tests/Lightshot.App.Tests/{QuickAccessHostTests.cs, PinWindowMathTests.cs, SettingsViewModelTests.cs, ThemeContrastTests.cs, OnboardingViewModelTests.cs, HistoryViewModelTests.cs}`, `tests/Lightshot.App.UiTests/{SettingsFlowTests.cs, QuickAccessFlowTests.cs, PinFlowTests.cs}`.

## Implementation steps

1. Settings: finalise `JsonSettingsStore` with all keys of APP §5 (except Sparkle keys, replaced in phase 9), atomic writes, change notifications, unknown keys ignored, missing keys default. Hotkey JSON keeps the `{CaptureAction.rawValue: HotkeyBinding}` shape.
2. Theming: apply `Application.ThemeMode` (Fluent, available since .NET 9, marked experimental so suppress WPF0001) for System/Light/Dark; set `DWMWA_USE_IMMERSIVE_DARK_MODE` on each window; map `ThemePalette` tokens into resource dictionaries; subscribe to `UserPreferenceChanged` for system theme switches.
3. Quick Access: card window is a WPF window with `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST` set in `SourceInitialized`, `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`, no `AllowsTransparency`. Stack position from Core `QuickAccessLayout` using the monitor containing the pointer and its work area. Auto-close timers pause on hover. Drag-out starts `DoDragDrop` with a temp-file `FileDrop`; the effect result drives close-after-drag (Alt inverts). Temp folder emptied at launch.
4. Pin: window sized from pixel size and monitor scale, capped to the work area; `WM_SIZING` hook keeps aspect; drag moves; context menu Copy, Save, Close; Esc closes when focused.
5. History: thumbnails via `SkiaThumbnailer`; reopen into editor or viewer; reveal with `explorer.exe /select,<path>`; delete uses `IFileOperation` to the Recycle Bin; retention applied on add and on setting change.
6. Settings window: panes bound to `SettingsViewModel`. Shortcuts pane uses `HotkeyRecorder` (captures VK plus modifiers, shows conflicts from Core, flags chords the OS refused at registration, warns about reserved chords such as Win+L). Screenshots pane: format, JPEG quality, location (folder picker via `OpenFolderDialog`), filename pattern, open in editor, include cursor, adjust area, delay, history retention, hide desktop icons. After Capture table. About: version, licence, third-party link, update toggles stubbed until phase 9.
7. Onboarding: first launch shows the checklist; completion flag `app.onboarded`. Detect `PrintScreenKeyForSnippingEnabled` and offer to open `ms-settings:easeofaccess-keyboard`. Explain the tray overflow and show a one-time toast pointing at it.
8. Launch at login: write the quoted exe path plus `--background` to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Lightshot`; read `StartupApproved\Run` to show "turned off in Task Manager" and offer `ms-settings:startupapps`.
9. `DesktopCover`: read the wallpaper per monitor via `IDesktopWallpaper` (path, position, background colour), render fit/fill/stretch/tile/centre into a topmost-below-windows borderless HWND placed above the desktop list view, click-through, per monitor. Show before `FreezeScreen`, hide in a `finally` on every exit path. Wire the setting `app.hideDesktopIcons` into `AppCoordinator` freeze.
10. Update the manual checklist and run it on a mixed-DPI setup if available.

## Acceptance criteria

| Criterion | Test |
|---|---|
| Card stack layout, overflow closes oldest, newest kept | `Lightshot.Core.Tests.QuickAccessTests` (13, phase 3) and `Lightshot.App.Tests.QuickAccessHostTests.PositionsCardsInPointerMonitorWorkArea` (`Unit`) |
| Auto-close options and hover pause | `Lightshot.App.Tests.QuickAccessHostTests.AutoCloseHonoursSettingAndHoverPause` (`Unit`, fake clock) |
| Cards absent from screenshots | `Lightshot.Platform.Windows.Tests.OverlayExclusionTests.ExcludedWindowAbsentFromDdaAndWgc` (phase 4, `Desktop`), extended with a card window |
| Drag-out closes the card unless Alt held | `Lightshot.App.UiTests.QuickAccessFlowTests.DragOutClosesCardUnlessAltHeld` (`Desktop`) |
| Pin keeps aspect on resize; Ctrl+S copies and pin stays | `Lightshot.App.Tests.PinWindowMathTests.KeepsAspectOnResize` (`Unit`), `Lightshot.App.UiTests.PinFlowTests.CopyKeepsPinOpen` (`Desktop`) |
| History retention, ordering, back-compat decode, media ownership | `Lightshot.Core.Tests.HistoryStoreTests` (phase 3) and `Lightshot.App.Tests.HistoryViewModelTests.ReopenRevealDelete` (`Unit`, fakes) |
| Recycle Bin delete is recoverable | `Lightshot.Platform.Windows.Tests.RecycleBinTests.DeletesToRecycleBin` (`Desktop`) |
| Settings read at use time; missing keys default; unknown keys ignored | `Lightshot.Platform.Windows.Tests.JsonSettingsStoreTests.DefaultsMissingKeysAndIgnoresUnknown` (`Unit`) |
| Settings migrate forward only | `Lightshot.Platform.Windows.Tests.SettingsMigrationTests.AddsKeysWithoutRewritingExisting` (`Unit`) |
| Hotkey recorder flags conflicts and OS-refused chords | `Lightshot.App.Tests.SettingsViewModelTests.ShortcutConflictsAreReported` (`Unit`) |
| Contrast pairs meet 4.5:1 text and 3:1 glyphs in both themes | `Lightshot.App.Tests.ThemeContrastTests.AllTokenPairsMeetWcag` (`Unit`) and ported `AppearanceTests` |
| Appearance never reaches renderers | `Lightshot.Rendering.Tests.DocumentRenderTests.OutputIgnoresAppearance` (new, `Render`) |
| Onboarding completes once; PrintScreen claim detected | `Lightshot.App.Tests.OnboardingViewModelTests.CompletesOnceAndDetectsPrintScreenClaim` (`Unit`) and ported `PermissionOnboardingModelTests` |
| Launch at login writes and removes the Run value | `Lightshot.Platform.Windows.Tests.LaunchAtLoginTests.WritesAndRemovesRunValue` (`Desktop`; uses a test-scoped Run subkey name) |
| Desktop cover hides icons in a still and is torn down on cancel and error | `Lightshot.Platform.Windows.Tests.DesktopCoverTests.IconsAbsentFromStillAndCoverRemoved` (`Desktop`) |
| Light/dark visual quality, card slide-in animation, tray overflow guidance | UNCOVERED: visual judgement; manual checklist. App and tray icons: art review gate per Boom rule 8 (`cc-art`, then Kongming frame review, never agy) |

## Rollback

Each feature is a separate folder; revert per feature. `settings.json` gains only additive keys with defaults, so a revert leaves a readable file. `DesktopCover` is behind `app.hideDesktopIcons` (default off).

## Risks

| Risk | L | I | Mitigation |
|---|---|---|---|
| `ThemeMode` is experimental and may change | M | L | Isolated in `ThemeService`; fall back to two resource dictionaries |
| Card windows steal focus from the app being used | M | M | `WS_EX_NOACTIVATE`, `WM_MOUSEACTIVATE` returns `MA_NOACTIVATE`; UI test checks foreground unchanged |
| WPF drag-out with large temp files is slow | L | L | Write the temp file once at card creation |
| `IDesktopWallpaper` slideshows or per-monitor wallpapers mismatched | M | M | Use per-monitor query; if unavailable, skip the cover and notify |
| Cover flashes visibly before the freeze | M | L | Show, wait for two DWM flushes, then freeze; measure |
| Startup entry disabled by Task Manager looks "on" | M | L | Read `StartupApproved`; show state |
| Recycle Bin API unavailable on network drives | L | L | Fallback to confirm-then-delete |

## Dependencies

Phase 4 (editor, sinks, settings store, overlay, tray). Phase 3 `HistoryStore` and `QuickAccess`. Blocks phase 9 (which runs straight after phase 5 to provide updater), phase 6 (sequential; parallel only for `Ml/`), and phase 7 (`DesktopCover`, settings pane framework).
