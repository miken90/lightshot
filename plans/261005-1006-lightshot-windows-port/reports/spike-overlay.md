# Spike D: Overlay Layer Report

**Date:** 2026-10-05  
**Probe:** `spikes/overlay-probe`  
**Host:** Windows 11 Pro 64-bit (Build 26200.5050), RTX 4060 Laptop GPU  
**Command:** `powershell.exe -File scripts/spike.ps1 -Name overlay-probe -Assert`  
**Overall Status:** PASS  

---

## 1. Hardware Matrix Covered

| Property | Value / Configuration |
|---|---|
| OS Version | Microsoft Windows NT 10.0.26200.0 (Windows 11 Build 26200.5050) |
| Primary GPU | NVIDIA GeForce RTX 4060 Laptop GPU |
| Active Monitors | 3 displays |
| Monitor 1 (`\\.\DISPLAY1`) | Primary, physical 1920x1080 at (0, 0), 125% DPI (1536x864 DIP) |
| Monitor 2 (`\\.\DISPLAY2`) | Secondary, physical 1920x1080 at (-1920, 0), 100% DPI |
| Monitor 3 (`\\.\DISPLAY5`) | Tertiary, physical 1920x1080 at (1920, 0), 100% DPI |
| Thread Model | Main Thread (Coordinator/UI): 33624<br>Dedicated STA Shell Thread: 34532<br>Dedicated Hook Thread: 27980 |

---

## 2. Pass Criteria and Measured Results

| Criterion | Numeric Bar | Measured Value | Result | Fallback Triggered |
|---|---|---|---|---|
| **Hotkey to First Overlay Pixel** | Median < 150 ms (20 iterations) | **74.84 ms median** (Min: 69.74 ms, Max: 79.95 ms) | **PASS** | None |
| **Keys Delivered** | 20 of 20 keys reach window | **20 / 20 delivered** (Esc, Return, Left, Up, Right, Down) | **PASS** | None |
| **Foreground and Focus** | Obtained from `WM_HOTKEY` | Foreground: `true`, Focus: `true` | **PASS** | None |
| **Capture Exclusion** | Excluded from 20 of 20 frames | **20 / 20 frames excluded** via `WDA_EXCLUDEFROMCAPTURE` | **PASS** | None |
| **Hook Callback Latency** | Max < 5.0 ms, not blocked | Max: **1.084 ms**, Avg: **0.344 ms**, Blocked: `false` | **PASS** | None |
| **PrintScreen Claim** | Outcome recorded with exact setting | `VK_SNAPSHOT` registered successfully. Setting documented | **PASS** | None |
| **Shell Thread Model** | HWNDs on dedicated STA thread | Window Thread ID: 34532 == Shell Thread ID: 34532 != Main: 33624 | **PASS** | None |

---

## 3. Detailed Raw Measurements

### 3.1 Hotkey to First Presented Overlay Pixel Latency
Measured from QPC at `SendInput` test chord (`Ctrl + Shift + F9`) to QPC at first DWM-presented frame containing the overlay pixel (magenta canary) captured via DXGI Desktop Duplication:
- Iterations (ms, sorted):
  1. 69.74 ms
  2. 70.17 ms
  3. 71.23 ms
  4. 71.61 ms
  5. 72.69 ms
  6. 73.29 ms
  7. 73.92 ms
  8. 73.95 ms
  9. 74.15 ms
  10. 74.40 ms
  11. **74.84 ms (Median)**
  12. 74.96 ms
  13. 75.61 ms
  14. 76.32 ms
  15. 76.76 ms
  16. 76.94 ms
  17. 77.52 ms
  18. 78.71 ms
  19. 79.78 ms
  20. 79.95 ms
- Result: **74.84 ms median**, well within the 150 ms threshold (50% headroom).

### 3.2 Focus and Navigation Input
- Primary overlay window received `SetForegroundWindow` and `SetFocus` upon `WM_HOTKEY`.
- 20 sequential test keys (Esc, Return, ArrowLeft, ArrowUp, ArrowRight, ArrowDown) were dispatched and received by the overlay's message loop (`WM_KEYDOWN`).
- Total delivered: 20/20.

### 3.3 Capture Exclusion (`WDA_EXCLUDEFROMCAPTURE`)
- Test card (150x150) and pill (100x50) windows created with `WS_EX_TOPMOST` and affinity `WDA_EXCLUDEFROMCAPTURE` (0x00000011).
- 20 sequential DDA frames acquired on primary display while nudging cursor to force DWM presentation.
- In 20 of 20 frames, inspection of mapped staging buffer showed 0 magenta pixels at window coordinates. Both windows were completely absent from desktop capture.

### 3.4 Low-Level Hooks and Security Defense
- `WH_KEYBOARD_LL` and `WH_MOUSE_LL` installed on dedicated background STA thread.
- 30 simulated events benchmarked:
  - Max callback execution time: 1.084 ms (< 5 ms limit).
  - Average callback execution time: 0.344 ms.
  - Windows Defender / AMSI: No alerts, blocks, or unhook events triggered.
- Clean unhooking confirmed via `UnhookWindowsHookEx` on thread termination.

### 3.5 PrintScreen Interception
- Probed registry `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled`.
- On this host, bare `RegisterHotKey(hWnd, id, 0, VK_SNAPSHOT)` succeeded without error (`win32Error = 0`).
- Guidance for systems with Snipping Tool interception active:
  - Registry setting: Set `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` to `0` (DWORD).
  - Fallback chords: `Ctrl + PrintScreen` and `Ctrl + Shift + PrintScreen`.

### 3.6 Shell-Thread Model Validation
- Validated thread decoupling per `plan.md`:
  - Main thread ID: 33624 (simulates WPF coordinator).
  - Dedicated STA shell thread ID: 34532 (owns `HotkeyWindow` and `OverlayWindow` HWNDs).
  - `GetWindowThreadProcessId` on overlay HWND returned 34532.
  - Dedicated hook thread ID: 27980 (owns `SetWindowsHookExW`).
- All 3 threads operated independently without cross-thread deadlock.

---

## 4. Architectural Impact on Phase 4

- DirectComposition overlay window activation latency is 74.84 ms, satisfying the < 150 ms bar without requiring startup pre-warming.
- `WDA_EXCLUDEFROMCAPTURE` is fully supported on Windows 11 DWM for hiding capture cards, toolbar pills, and magnifier bubbles.
- The dedicated STA shell-thread model is proven and ready for Phase 4 implementation.
