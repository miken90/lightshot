# Platform Defaults and Seam Decisions

This document records the upstream defaults, platform differences, and architectural decisions made for the Lightshot Windows port.

## 1. ADR 0001: Deterministic Rendering and Shared Redaction Patch

- **Where**: `LightshotKit/Sources/LightshotKit/RedactionEffect.swift`, `docs/adr/0001-core-image-blur-and-shared-redaction-patch.md` -> `Lightshot.Rendering/RedactionBackdrop.cs`, `GaussianBlur.cs`, `Scramble.cs`.
- **Why**: Upstream used software-rendered Core Image (`CIContext` with color management off) so that canvas preview and final export share the exact same redaction patch and produce byte-exact output across machines. In the Windows port, `Lightshot.Core` is strictly pure BCL (0 package references). Deterministic software rendering (scalar Gaussian blur and SplitMix64 scramble) is implemented in `Lightshot.Rendering` without GPU variance.

## 2. Burn-In Reachability Decision (Phase 7 Scope)

- **Where**: `LightshotKit/Sources/LightshotKit/RecordingOptions.swift` (`studio: Bool`), `RecordingCompositor.swift` -> `Lightshot.Core/Recording/RecordingOptions.cs`, Phase 7 `BurnInCompositor`.
- **Why**:
  - Upstream has two recording paths:
    1. **Studio Mode (`studio: true`)**: Records clean video frames without overlays and stores input events (cursor, clicks, keystrokes, webcam) as metadata sidecars for post-recording editing in the Studio timeline editor.
    2. **Direct/Non-Studio Mode (`studio: false`)**: Uses `RecordingCompositor` to burn input overlays (cursor, click highlight rings, keystroke pill, webcam bubble) directly into the video frames during capture.
  - In the Windows MVP scope, Phase 8 (Studio) is deferred post-MVP. All screen recordings on Windows in Phase 7 execute the direct compositor path (`studio: false`).
  - **Verdict**: Burn-in composition is **fully reachable and mandatory in the MVP (Phase 7)**. Phase 7 will implement `BurnInCompositor` / overlay rendering for click and keystroke overlays.

## 3. Hotkey Defaults

- **Where**: `LightshotKit/Sources/LightshotKit/HotkeyBinding.swift` -> `Lightshot.Core/Hotkeys/HotkeyBinding.cs`.
- **Upstream Value**:
  - `fullscreen`: `⌃⌘3` (`keyCode: 20`, modifiers: `[.command, .control]`, `keyLabel: "3"`)
  - `area`: `⌃⌘4` (`keyCode: 21`, modifiers: `[.command, .control]`, `keyLabel: "4"`)
  - Other actions unbound.
  - Upstream Reason: macOS reserves `⇧⌘3` and `⇧⌘4` for system screen capture, so Lightshot adds `Control` to avoid registration collisions.
- **Windows Value**:
  - `area`: `PrintScreen` (`keyCode: 0x2C` / `VK_SNAPSHOT`, modifiers: none, `keyLabel: "PrintScreen"`)
  - `fullscreen`: `Ctrl+PrintScreen` (`keyCode: 0x2C` / `VK_SNAPSHOT`, modifiers: `Control`, `keyLabel: "PrintScreen"`)
  - Other actions (`window`, `repeatLast`, `recordScreen`, `captureText`, `pauseResumeRecording`, `restartRecording`) remain unbound by default.
  - Windows Reason: Standard Windows convention and classic Lightshot behavior rely on `PrintScreen` for area capture and `Ctrl+PrintScreen` for fullscreen capture.
  - Tested by: `HotkeyBindingTests.DefaultsArePrintScreenVariants`.

## 4. Filename Sanitization and Windows-Forbidden Characters

- **Where**: `LightshotKit/Sources/LightshotKit/SettingsStore.swift` (`FilenameFormatter`) -> `Lightshot.Core/Settings/FilenameFormatter.cs`.
- **Upstream Value**: Replaced `/` and `:` with `-`, trimmed whitespace and surrounding dots.
- **Windows Value**: Windows file systems forbid the characters `< > : " / \ | ? *`, control characters `0x00`-`0x1F`, trailing periods, trailing spaces, and reserved device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1`-`COM9`, `LPT1`-`LPT9`).
- **Windows Reason**: Windows paths containing forbidden characters or reserved device names cause file creation errors. `FilenameFormatter.Sanitized` replaces forbidden characters with `-`, strips control characters, trims dots and spaces, and prefixes reserved device names.
- **Tested by**: `FilenameFormatterTests.StripsWindowsForbiddenCharacters`.

## 5. Recording and Video Defaults

- **Where**: `LightshotKit/Sources/LightshotKit/RecordingOptions.swift` -> `Lightshot.Core/Recording/RecordingDefaults.cs`.
- **Preserved Upstream Values**:
  - Video: H.264, 30 fps, native resolution (`MaxResolution.Original`), `scaleRetinaTo1x: false`.
  - GIF: 15 fps, quality 0.8, max width 800 px, `optimize: true`.
  - Audio: Narration volume 1.0 (unity), computer audio volume 1.0, stereo (`monoAudio: false`), single track mix (`separateAudioTracks: false`).
  - Overlays: Click highlight enabled (`Ring`, medium size 22 px, yellow, animated), keystroke overlay enabled (`AllKeys`, bottom center, medium 22 pt, blur background).
  - Countdown: 3 seconds with sounds enabled.
  - After recording: `ShowOverlay`.
