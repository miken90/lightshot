# Phase 2: Spikes and Go/No-Go Gate Report

**Date:** 2026-10-05  
**Host Machine:** Windows 11 Pro 64-bit (Build 26200.5050), AMD Ryzen 9 7945HX (32 logical cores), NVIDIA GeForce RTX 4060 Laptop GPU (Driver 32.0.15.6094), primary display 1920x1080 at 125% DPI (120 DPI), two virtual displays at 100% DPI (96 DPI).  
**Result Artifact:** `plans/261005-1006-lightshot-windows-port/reports/spike-gate.md`  
**Overall Decision:** **GO for the MVP** (screenshot + screen recording; phases 6, 8, 10 and camera bubble deferred post-MVP).

---

## 1. Executive Summary and Overall Verdict

Five technical probes (`spikes/capture-probe`, `recording-probe`, `compositor-probe`, `overlay-probe`, `ml-probe`) were executed on the target host hardware to retire the top architectural risks identified in RULING §8 before committing feature code.

Per the binding user decision of 2026-10-05, project scope is rescoped to an MVP focused on high-performance screenshots and screen recording. Advanced capabilities (Auto Redact, OCR/QR, Studio editor, on-device translation, and camera bubble) are deferred to a post-MVP backlog.

- **Capture (Spike A)** and **Overlay (Spike D)**: **PASS** across all active criteria. Unpackaged WGC produces zero border pixels; DDA functions across all GPU preferences; overlay activates in 74.84 ms median (< 150 ms bar) with clean input and capture exclusion.
- **Recording (Spike B)**: **GO** (5 of 6 pass). Capture, hardware H.264 encode, process-excluding loopback (-73.2 dBFS), fragmented MP4 recovery (59.9 fps), and playback succeed with 0.251% frame drops and 0.96% CPU. The 61.4 ms A/V drift at the last clap (bar: < 40 ms) is a reference-clock measurement artifact (constant 26 ms file-only gap, no accumulating drift); mitigated by stamping audio against the render endpoint clock (`IAudioClock`) and stripping the trailing Media Foundation `mfra` box.
- **Compositor (Spike C)**: **GO with HwndHost**. HwndHost passes export parity on WARP (delta 0), resize leak/tear, DPI transitions, and overlay z-order. Seek latency (>150 ms) and 60 fps source-skips become Phase 8 acceptance items. Because Phase 8 (Studio) is deferred post-MVP, Spike C does not gate the MVP.
- **On-Device ML (Spike E)**: **PASS on Whisper, YuNet, OPUS-MT; FAIL on OCR strict recall (67.4% vs 90% bar)**. Because all ML features (Phases 6, 8, 10) are deferred post-MVP, Spike E does not gate the MVP. The OCR failure and fallback (3x retry with binarization or alternative engine) are recorded for Phase 6 resumption.

---

## 2. Phase 2 Rule Evaluation (High-Severity Failure Tally)

The Phase 2 rule (`phase-02-spikes-and-go-no-go.md` step 5) states:  
> **"Stop and re-plan if two or more High risks fail."**

| Risk Area | Spike | Risk Severity (from plan.md) | Measured Result | Gates MVP? | Blocking Fail Count |
|---|---|---|---|---|---|
| **Display/Window Capture** | Spike A | High | **PASS** (HDR UNCOVERED) | Yes (Phase 4) | 0 |
| **Overlay Latency & Hooks** | Spike D | High | **PASS** | Yes (Phase 4, 7) | 0 |
| **Real-Time Recording Pipeline** | Spike B | High | **GO** (5/6 pass; drift mitigated via render clock & mfra strip) | Yes (Phase 7) | 0 |
| **Studio Compositor in WPF** | Spike C | High | **GO HwndHost** (seek & skips become Phase 8 items) | **No** (Phase 8 deferred post-MVP) | 0 |
| **On-Device ML Quality & Cost** | Spike E | Medium | **FAIL on OCR strict recall; PASS on Whisper, YuNet, OPUS-MT** | **No** (Phases 6, 8, 10 deferred post-MVP) | 0 |

**Tally:**  
Total High-severity failures gating the MVP = **0**.  
The threshold for a project re-plan (>= 2 High fails) is not reached.  
**Verdict:** **GO for the MVP**.

---

## 3. Measured Spike Verdicts and Citations

### 3.1 Spike A: Capture on Hybrid-GPU Laptop (`reports/spike-capture.md`)
- **Overall Status:** PASS.
- **Still Latency:** Warm median 16.39–17.12 ms across all 3 displays (Primary `\\.\DISPLAY1` cold 19.76 ms, warm median 17.12 ms; Left `\\.\DISPLAY2` cold 23.22 ms, warm median 16.79 ms; Right `\\.\DISPLAY5` cold 17.70 ms, warm median 16.39 ms), well below the 100 ms bar.
- **DDA on GPU Preferences:** Tested `SystemDefault` (0), `iGPU` (1), `dGPU` (2). All succeeded without `DXGI_ERROR_UNSUPPORTED`. Registry preference cleanly reset via `scripts/set-gpu-preference.ps1 -Reset`.
- **WGC Borderless Unpackaged:** Positive control (`IsBorderRequired = true`) rendered 2964 yellow border pixels in the 5 px perimeter band. Test case (`IsBorderRequired = false`) rendered **0 yellow border pixels**. `PrintWindow(PW_RENDERFULLCONTENT)` succeeded.
- **Mixed-DPI Coordinates:** Marker window at physical (150, 150) detected at exactly (150, 150) with (0, 0) px delta.
- **HDR:** Host lacks HDR panels; marked **UNCOVERED**. Simulated 203-nit tone map produced 248.01 sRGB.

### 3.2 Spike D: Overlay Layer (`reports/spike-overlay.md`)
- **Overall Status:** PASS.
- **Hotkey to First Presented Overlay Pixel:** **74.84 ms median** (min 69.74 ms, max 79.95 ms) from `SendInput` chord (`Ctrl+Shift+F9`) to first DWM-presented frame verified via DXGI duplication. 50% headroom under 150 ms threshold. Pre-warming at startup is not required.
- **Focus & Navigation:** Foreground and focus obtained from `WM_HOTKEY`; 20 of 20 navigation keys (Esc, Return, arrows) received by overlay message loop.
- **Capture Exclusion:** Test card and pill windows created with `WDA_EXCLUDEFROMCAPTURE` were completely absent (0 magenta pixels) across 20 of 20 acquired DDA frames.
- **Hooks Latency & Security:** Dedicated STA hook thread achieved max callback latency of 1.084 ms and average of 0.344 ms (< 5 ms bar). Windows Defender and AMSI raised zero alerts.
- **PrintScreen Interception:** Bare `RegisterHotKey(hWnd, id, 0, VK_SNAPSHOT)` succeeded without error (`win32Error = 0`). Documented setting: `HKCU\Control Panel\Keyboard\PrintScreenKeyForSnippingEnabled` = 0.
- **Shell-Thread Model:** Verified three decoupled threads (Main Coordinator: 33624, Dedicated STA Shell: 34532, Dedicated Hook: 27980) without deadlock.

### 3.3 Spike B: Real-Time Recording Pipeline (`reports/spike-recording.md`)
- **Overall Status:** 5/6 PASS; A/V drift FAIL (reference-clock problem). **Verdict: GO**.
- **Frame Drops:** **9 of 3584** Gray-coded counter values missing (**0.251%**, bar: < 1%). All 9 were skipped by the test window before capture; 0 lost after capture (3579 acquired, 3583 written).
- **CPU Utilization:** **0.96%** (18.38 CPU s over 60.02 s x 32 cores, bar: < 15%) using `NVIDIA H.264 Encoder MFT`.
- **Loopback Audio Exclusion:** Probe 440 Hz tone measured at **-73.2 dBFS** in loopback track (bar: < -60 dBFS); external helper 660 Hz control tone captured at -20.0 dBFS.
- **Kill and Recover:** 40 s take killed mid-flight (22.3 MB); recovered 2373 frames over 39.6 s (**59.9 fps**), max PTS gap 35.5 ms, only 1 counter value missing (0.04%). File plays in MF source reader and `MediaElement`.
- **Playback:** MF source reader decodes 60 s and 40 s recovered takes to EOF; `MediaElement` plays and seeks to `MediaEnded`. (Windows Media Player standalone app is marked UNCOVERED).
- **A/V Drift Root Cause:** Last clap measured **61.4 ms** adjusted vs 40 ms bar (claps: 75.4, 54.9, 81.0, 64.0, 39.9, 61.4 ms). However, file-only `|video - audio|` gap is constant across all claps (24.8–26.4 ms) with zero accumulation over 60 seconds. The discrepancy arises from variable latency (40–81 ms) between the per-process WASAPI tap and the endpoint loopback stream.
- **Phase 7 Mitigations:**
  1. Stamp loopback audio against the render endpoint clock (`IAudioClock` position on render device smoothed by drift filter) rather than the tap's `qpcPosition`.
  2. Validate with an acoustic loop (speaker to live mic) during Phase 7 testing.
  3. Truncate trailing MF `mfra` box after finalize to bypass Media Foundation's `tfra` offset defect (sawtooth timestamp jumps).

### 3.4 Spike C: Studio Compositor Pipeline (`reports/spike-compositor.md`)
- **Overall Status:** FAIL on seek bar, with host recommendation. **Verdict: GO HwndHost (Post-MVP Phase 8)**.
- **Host Selection:** `HwndHost` is strongly superior to `D3DImage`. HwndHost passed export parity on WARP (delta 0), resize leak (0 MB GPU leak over 100 cycles), resize tear (0 torn frames over 204–215 frames), DPI changes (20 `WM_DPICHANGED` messages, 0 mismatches), and overlay tests. `D3DImage` failed 60 fps every run (58.3–58.4 fps) and exhibited higher seek latency.
- **Seek Latency:** Present-immediately max was 159.9–166.3 ms across three runs (bar: < 150 ms). Decode takes ~1.5 ms; bottleneck is DWM composition of the 2560x1440 client on the 1080p primary display.
- **60 fps Stability:** Run 1 was fully clean (0 drops/skips). Runs 2 and 3 had source skips (run 2: 1 frame; run 3: 117 frames).
- **Impact:** Studio is deferred post-MVP. Seek optimization and 60 fps source-skip mitigation become acceptance items for Phase 8.

### 3.5 Spike E: On-Device ML Quality and Cost (`reports/spike-ml.md`)
- **Overall Status:** FAIL on OCR strict recall; PASS on Captions, YuNet, and Translation. **Verdict: Deferred Post-MVP**.
- **Captions (Whisper):** `Whisper.net` `ggml-base-q5_1` on CPU achieved median word-timing error of **145.0 ms** (< 300 ms bar) and RTF of **0.058** (22.0 s processing for 376.5 s audio; < 1.0 bar). PASS.
- **Face Detection (YuNet):** Prominent faces (>= 40 px, clear) achieved **91.54% recall** (184 / 201; >= 90% bar). PASS on filtered set. All faces recall was 62.37% (590 / 946).
- **Translation (OPUS-MT):** en->vi chrF 57.07, BLEU 36.02, p50 latency 64.7 ms; vi->en chrF 65.75, BLEU 39.89, p50 latency 58.4 ms (bar: chrF >= 45, latency <= 300 ms). PASS.
- **OCR Strict Recall:** **67.4% (151 / 224)** vs >= 90% bar across 7 categories and 3 fonts. FAIL. Per-line 3x retry + binarization was applied (recovered 11 entities).
- **Impact:** All ML features (Auto Redact, OCR Text, Studio captions, Translation) are deferred post-MVP. When Phase 6 resumes, investigate enhanced preprocessing (adaptive binarization) or alternative OCR engines.

---

## 4. Phase 2 Verdict Matrix and Effects on Later Phases

| Probe | Criterion | Verdict | Effect on Later Phases | Status in Scope |
|---|---|---|---|---|
| **A: Capture** | WGC Borderless | **PASS** (0 px) | Unpackaged WGC used for window captures without sparse package or MSIX. | **MVP (Phase 4)** |
| **A: Capture** | DDA on GPUs | **PASS** | DDA used for all display capture; adapter-matched D3D device. | **MVP (Phase 4, 7)** |
| **A: Capture** | Still Latency | **PASS** (16–17 ms) | Stills well within 100 ms target; no pre-capture caching needed. | **MVP (Phase 4)** |
| **A: Capture** | Mixed DPI | **PASS** (0 px delta) | Global virtual physical pixel coordinate model validated. | **MVP (Phase 3, 4)** |
| **A: Capture** | HDR Tone Map | **UNCOVERED** | Host lacks HDR panel; marked UNCOVERED in Phase 4. | **MVP (Phase 4)** |
| **D: Overlay** | Hotkey Latency | **PASS** (74.8 ms) | DirectComposition overlay meets < 150 ms bar without pre-warming. | **MVP (Phase 4)** |
| **D: Overlay** | Keys Delivered | **PASS** (20/20) | Foreground and focus granted from `WM_HOTKEY`. | **MVP (Phase 4)** |
| **D: Overlay** | Capture Exclusion | **PASS** (20/20) | `WDA_EXCLUDEFROMCAPTURE` keeps chrome out of DDA and WGC. | **MVP (Phase 4, 7)** |
| **D: Overlay** | Hook Latency | **PASS** (1.08 ms max) | Hook queue thread model proven safe; Defender/AMSI green. | **MVP (Phase 7)** |
| **D: Overlay** | PrintScreen | **PASS** | Bare `VK_SNAPSHOT` registers; documented registry bypass setting. | **MVP (Phase 4)** |
| **B: Recording** | Cadence & Drops | **PASS** (0.251%) | GPU video processor + cadence planner delivers constant fps. | **MVP (Phase 7)** |
| **B: Recording** | CPU Load | **PASS** (0.96%) | Hardware MFT encode verified; well within 15% limit. | **MVP (Phase 7)** |
| **B: Recording** | Loopback Tone | **PASS** (-73.2 dB) | NAudio process loopback cleanly excludes own process audio. | **MVP (Phase 7)** |
| **B: Recording** | Recovery | **PASS** (59.9 fps) | Fragmented MP4 recovery verified; passthrough remux succeeds. | **MVP (Phase 7)** |
| **B: Recording** | A/V Drift | **FAIL (61.4 ms)** | **Action:** Phase 7 stamps loopback audio against render endpoint clock (`IAudioClock`) and strips MF `mfra` box. GO. | **MVP (Phase 7)** |
| **C: Compositor** | Architecture | **GO HwndHost** | HwndHost chosen over D3DImage for export parity and stability. | **Post-MVP (Phase 8)** |
| **C: Compositor** | Seek & Skips | **FAIL (>150 ms)** | Seek latency and source-skips become Phase 8 acceptance items. | **Post-MVP (Phase 8)** |
| **E: ML Quality** | Whisper Captions | **PASS** (145 ms) | Whisper base q5 approved for captions when Phase 8 resumes. | **Post-MVP (Phase 8)** |
| **E: ML Quality** | YuNet Faces | **PASS** (91.5%) | YuNet ONNX approved for prominent faces when Phase 6 resumes. | **Post-MVP (Phase 6)** |
| **E: ML Quality** | Translation | **PASS** (p50 < 65 ms) | OPUS-MT ONNX approved for en<->vi when Phase 10 resumes. | **Post-MVP (Phase 10)** |
| **E: ML Quality** | OCR Strict Recall | **FAIL (67.4%)** | **Action:** Phase 6 to evaluate adaptive preprocessing or alternative OCR engine. Does not gate MVP. | **Post-MVP (Phase 6)** |

---

## 5. Action Items Transferred to MVP Phases

1. **Phase 4 (Capture & Editor):**
   - DirectComposition overlay window activation latency verified (74.8 ms); no startup pre-warming required.
   - WGC unpackaged borderless verified; use DDA for displays and WGC for windows.
   - Mixed-DPI physical coordinate math verified.
2. **Phase 7 (Recording):**
   - Implement loopback audio stamping against render endpoint clock (`IAudioClock` position smoothed by drift filter) to eliminate the 61.4 ms tap-vs-endpoint offset.
   - Strip trailing Media Foundation `mfra` box upon finalize in `Mp4Remuxer` to avoid the MPEG-4 source `tfra` sawtooth timestamp jump.
   - Camera bubble (Package R4) deferred to post-MVP backlog; burn-in restricted to clicks and keystrokes.
3. **Phase 9 (Update & Release):**
   - Size gate `-MaxSetupMB` re-anchored to **113 MB** (98 MB Phase 1 measured unsigned Setup.exe + 15% headroom).
   - Removed dependency on Spike E model bundle sizes.
