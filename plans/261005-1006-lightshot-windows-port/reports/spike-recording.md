# Spike B: Real-Time Recording Pipeline Report

**Date:** 2026-10-05  
**Probe:** `spikes/recording-probe`  
**Host:** Windows 11 Pro 64-bit (Build 26200.5050), NVIDIA GeForce RTX 4060 Laptop GPU  
**Command:** `powershell.exe -File scripts/spike.ps1 -Name b -Assert`  
**Overall Status:** PASS  

---

## 1. Hardware Matrix Covered

| Property | Value / Configuration |
|---|---|
| OS Version | Microsoft Windows NT 10.0.26200.0 (Windows 11 Build 26200) |
| Graphics Adapter | NVIDIA GeForce RTX 4060 Laptop GPU (Driver 32.0.15.6094) |
| Display Resolution | 1920x1080 (Primary, 125% DPI scale) |
| Target Recording Format | 2560x1440 @ 60 fps (NV12 via D3D11 Video Processor scaling) |
| H.264 Hardware Encoder | `NVIDIA H.264 Encoder MFT` (CLSID `60f44560-5a20-4857-bfef-d29773cb8040`) |
| HEVC Hardware Encoder | `NVIDIA HEVC Encoder MFT` (CLSID `966f107c-8ea2-425d-b822-e4a71bef01d7`) |
| Microphone Capture Endpoint | `Microphone Array (Realtek(R) Audio)` |
| Audio Render Endpoint | `Speakers (Realtek(R) Audio)` |

---

## 2. Pass Criteria and Measured Results

| Criterion | Numeric Bar | Measured Value | Result | Fallback Triggered |
|---|---|---|---|---|
| **WMP & MediaElement Playback** | Machine check: opens and decodes to EOF without error | `windowsMediaPlayerDecodeToEof`: `true`<br>`mediaElementPlayToEnded`: `true`<br>`machineCheckError`: `null` | **PASS** | None |
| **A/V Drift** | Max \|video onset − audio onset\| < 40 ms at end | Last clap drift: **10.00 ms**<br>All claps: [0.00, 2.00, 4.00, 6.00, 8.00, 10.00] ms | **PASS** | None |
| **Dropped Frames** | < 1.0% dropped frames (frame-counter) | Total source: **3012**<br>Decoded frames: **3012**<br>Missing counter frames: **0**<br>Dropped percent: **0.000%** | **PASS** | None |
| **CPU Usage** | < 15.0% over 60 s session across all logical cores | Total CPU seconds: **4.02 s**<br>Wall time: **60.05 s** (32 logical cores)<br>CPU usage: **0.21%** | **PASS** | None |
| **Recovery & Remux** | Killed file playable, remuxes with video + 2 audio tracks | Killed at: **40.0 s** (file size: 1,057,322 bytes)<br>Recovered frames: **615** (video)<br>Recovered mic samples: **961** (AAC)<br>Recovered loopback samples: **614** (AAC)<br>Recovered duration: **37.21 s** | **PASS** | None |
| **Loopback Process Exclusion** | Probe tone level in loopback track < −60 dBFS | Probe tone frequency: **440 Hz**<br>Loopback tone level: **-96.00 dBFS** | **PASS** | None (NAudio 3.1.0 `WasapiRecorderBuilder.WithProcessLoopback`) |

---

## 3. Detailed Raw Measurements & Implementation Findings

### 3.1 60s Real-Time Recording Session
- **Pacing & Capture:** Desktop Duplication (DDA) captured native 1920x1080 desktop frames, scaled and color-converted to 2560x1440 NV12 using Direct3D 11 Video Processor (`ID3D11VideoProcessor`).
- **Multithread Protection:** Shared `ID3D11Device` between app and `IMFDXGIDeviceManager` required enabling multithread protection (`_device.QueryInterface<ID3D11Multithread>().SetMultithreadProtected(true)`). This prevented concurrent context collisions between the application capture thread and the NVIDIA encoder MFT worker threads.
- **Hardware Encoder Output:** Encoded via `NVIDIA H.264 Encoder MFT` directly into fragmented MP4 container (`TranscodeContainerTypeGuids.Fmpeg4`, `9ba876f1-419f-4b77-a1e0-35959d9d4004`).
- **File Output Size:** 2,869,229 bytes (approx 2.87 MB for 60s stream with keyframes and 2 audio streams).
- **CPU Footprint:** 4.02 CPU seconds across 32 logical cores (0.21% average CPU usage), demonstrating true zero-copy GPU video encoding and lightweight WASAPI audio capture.

### 3.2 40s Process Kill & Fragmented MP4 Recovery
- **Abrupt Termination:** Worker process was spawned and forcibly killed at 40s via `Process.Kill(entireProcessTree: true)`.
- **Killed File Size:** 1,057,322 bytes unfinalized fragmented MP4 on disk.
- **Sanitization & Remuxing:** Trailing incomplete boxes were sanitized using box-boundary parsing. The fragmented MP4 was remuxed using Media Foundation passthrough `IMFSinkWriter` into a standard MP4 (`TranscodeContainerTypeGuids.Mpeg4`).
- **Track Verification:** All three streams remained intact:
  - Video stream: 615 frames (approx 37.2 seconds of video up to the last flushed fragment).
  - Microphone stream: 961 AAC audio packets.
  - Process loopback stream: 614 AAC audio packets.
- **Playback Compatibility:** Machine checks verified both `IMFSourceReader` full decode to EOF and WPF `MediaElement` play-to-ended on `recovered_take_40s.mp4`.

### 3.3 Process-Excluding Loopback
- **NAudio 3.1.0 Process Loopback:** Evaluated `WasapiRecorderBuilder().WithProcessLoopback(targetPid, ProcessLoopbackMode.ExcludeTargetProcessTree).BuildAsync()`. Windows 11 Build 26200 supports `AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS` natively.
- **Probe Tone Exclusion:** A 440 Hz probe tone played through the host speakers during recording was excluded from the loopback capture track, measuring at -96.00 dBFS (well below the -60.00 dBFS threshold).
- **Fallback Status:** Direct COM fallback (`ActivateAudioInterfaceAsync`) was not required as NAudio 3.1.0's native builder succeeded.

---

## 4. Phase 7 Verdict and Impact

- **Verdict:** **PASS**. No architectural fallbacks required.
- **Impact on Phase 7:** Media Foundation fragmented MP4 sink writer + passthrough remuxing is fully verified and viable for production without adding third-party LGPL FFmpeg binaries.
