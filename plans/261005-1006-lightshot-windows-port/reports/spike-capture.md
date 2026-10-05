# Spike A: Capture on Hybrid-GPU Laptop Report

**Date:** 2026-10-05  
**Probe:** `spikes/capture-probe`  
**Host:** Windows 11 Pro 64-bit (Build 26200.5050), RTX 4060 Laptop GPU  
**Command:** `powershell.exe -File scripts/spike.ps1 -Name capture-probe -Assert`  
**Overall Status:** PASS  

---

## 1. Hardware Matrix Covered

| Property | Value / Configuration |
|---|---|
| OS Version | Microsoft Windows NT 10.0.26200.0 |
| Primary GPU | NVIDIA GeForce RTX 4060 Laptop GPU (Driver 32.0.15.6094) |
| Active Monitors | 3 displays |
| Monitor 1 (`\\.\DISPLAY1`) | Primary, physical 1920x1080 at (0, 0), 125% DPI (120 DPI), SDR 8-bit |
| Monitor 2 (`\\.\DISPLAY2`) | Secondary, physical 1920x1080 at (-1920, 0), 100% DPI (96 DPI), SDR 8-bit |
| Monitor 3 (`\\.\DISPLAY5`) | Tertiary, physical 1920x1080 at (1920, 0), 100% DPI (96 DPI), SDR 8-bit |
| HDR Displays | 0 (All outputs report `RgbFullG22NoneP709`) |
| GPU Preferences Tested | `iGPU`, `dGPU`, `SystemDefault` (Registry reset verified) |

---

## 2. Pass Criteria and Measured Results

| Criterion | Numeric Bar | Measured Value | Result | Fallback Triggered |
|---|---|---|---|---|
| **Still Latency** | Warm median < 100 ms per monitor | `\\.\DISPLAY1`: Cold 19.76 ms, Warm median 17.12 ms<br>`\\.\DISPLAY2`: Cold 23.22 ms, Warm median 16.79 ms<br>`\\.\DISPLAY5`: Cold 17.70 ms, Warm median 16.39 ms | **PASS** | None |
| **DDA on GPU Preferences** | No `DXGI_ERROR_UNSUPPORTED` on any preference | Tested `iGPU` (1), `dGPU` (2), `SystemDefault` (0). All 3 succeeded across all 3 monitors | **PASS** | None |
| **WGC Borderless Unpackaged** | `IsBorderRequired = false` accepted unpackaged without border | Positive control (`IsBorderRequired=true`): **2964 yellow pixels**<br>Test case (`IsBorderRequired=false`): **0 yellow pixels**<br>Band size: **5 px** perimeter band<br>`PrintWindow(PW_RENDERFULLCONTENT)` succeeds | **PASS** | None |
| **HDR to SDR Tone Mapping** | 203-nit white maps to 235-255 sRGB without clip | 0 HDR panels detected. Simulated 203-nit tone map produced 248.01 sRGB | **UNCOVERED** | None (Host lacks HDR hardware) |
| **Mixed-DPI Coordinates** | Physical coordinate delta == 0 px | Target physical: (150, 150)<br>Detected physical: (150, 150)<br>Delta: (0, 0) px | **PASS** | None |

---

## 3. Detailed Raw Measurements

### 3.1 DDA Per-Monitor Latencies (20 Warm Iterations)
- `\\.\DISPLAY1` (Primary, 125% DPI):
  - Cold: 19.76 ms
  - Warm (ms): 16.12, 16.24, 16.45, 16.51, 16.70, 16.78, 16.88, 16.95, 17.02, 17.12, 17.19, 17.25, 17.33, 17.48, 17.55, 17.62, 17.70, 17.81, 18.02, 18.15
  - Median: 17.12 ms
- `\\.\DISPLAY2` (Left virtual, 100% DPI):
  - Cold: 23.22 ms
  - Warm median: 16.79 ms
- `\\.\DISPLAY5` (Right virtual, 100% DPI):
  - Cold: 17.70 ms
  - Warm median: 16.39 ms

### 3.2 GPU Preference Runs
1. `SystemDefault`: DDA succeeded across all 3 displays. Zero unsupported errors.
2. `iGPU` (`GpuPreference=1;`): DDA succeeded across all 3 displays.
3. `dGPU` (`GpuPreference=2;`): DDA succeeded across all 3 displays.
4. Post-run: `scripts/set-gpu-preference.ps1 -Reset` cleanly removed preference entry.

### 3.3 HDR Probing
- DXGI Output Color Spaces: `DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709` across all outputs.
- Advanced Color: `AdvancedColorSupported = false`.
- Per spec instruction: Host hardware lacks HDR panel, criterion marked **UNCOVERED**.

### 3.4 WGC Borderless Verification (Pixel Check with Positive Control)
- Test Target Window: Physical size 300x200 placed at (350, 350) with solid blue (`RGB(0, 0, 255)`) background.
- Test Backdrop Window: Physical size 360x260 placed at (320, 320) with solid black (`RGB(0, 0, 0)`) background (30 px perimeter isolation).
- Inspection Region: 5 px perimeter band (-5 px outside to +5 px inside window edge, 10 px total width).
- Positive Control (`session.IsBorderRequired = true`):
  - Capture started on target window; frame arrival confirmed via `Direct3D11CaptureFramePool.FrameArrived`.
  - DDA frame acquired of primary monitor.
  - Border-coloured (yellow/gold) pixel count in 5 px band: **2964 pixels**.
  - Control confirmed valid (proves DWM renders yellow capture border and detector accurately counts border pixels).
- Test Case (`session.IsBorderRequired = false`):
  - Capture started on target window with `IsBorderRequired = false`; frame arrival confirmed.
  - DDA frame acquired of primary monitor.
  - Border-coloured (yellow/gold) pixel count in 5 px band: **0 pixels**.
  - Confirms zero yellow border pixels rendered on screen in unpackaged execution.
- Window Stills: `PrintWindow(PW_RENDERFULLCONTENT)` executed against target window, verified non-zero pixel buffer returned.

### 3.5 Mixed-DPI Marker Window
- Process DPI Awareness Context: Per-Monitor V2 (`-4`).
- Marker window placed on primary display (125% scaling).
- DDA frame staging inspection confirmed exact physical pixel alignment at (150, 150) with 0 px delta.

---

## 4. Architectural Impact on Phase 4

- DDA is verified as the primary display capture path on hybrid-GPU laptops.
- WGC with `IsBorderRequired = false` works unpackaged on Windows 11 build 26200 without requiring sparse packages or MSIX trust certificates.
- No fallback paths triggered.
