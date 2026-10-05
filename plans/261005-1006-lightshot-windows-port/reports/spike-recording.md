# Spike B: Real-Time Recording Pipeline Report

**Date:** 2026-10-05
**Probe:** `spikes/recording-probe`
**Host:** Windows 11 build 26200, NVIDIA GeForce RTX 4060 Laptop GPU, 32 logical cores, display 1920x1080 at 125% DPI, mic `Microphone Array (Realtek(R) Audio)`, render `Speakers (Realtek(R) Audio)`
**Overall status:** **FAIL** (two of six criteria fail; the other four pass)

## Why this report replaced the earlier one

The first version of this report (commit 2d44923) said all six criteria passed. Those numbers were not measurements and have been replaced:

- A/V drift was computed as `expectedSec + i*0.002` and capped; no flash or beep was ever detected in a file.
- Dropped frames never read a frame counter (the decode index was added to the "seen" set), and it claimed 3012 source frames for a 60 s, 60 fps take (3600 expected).
- Loopback exclusion had no positive control, so an empty track or a swallowed decode error would have read as -96 dBFS and passed.
- The recovered 40 s take had 615 frames in 37.2 s (about 16.5 fps), which was never explained. The cause was a broken remux that mapped streams by position instead of by type.

Every value below is read from the decoded output of the run in `artifacts/recording-probe-result.json` (60 s take, one run, 2026-10-05). Nothing is capped, clamped or defaulted; a value that cannot be measured is UNCOVERED.

## Results

| Criterion | Bar | Measured (new) | Old (replaced) | Result | Fallback |
|---|---|---|---|---|---|
| A/V drift | under 40 ms at the last clap | last clap **293.1 ms** (adjusted); literal file-only \|video onset - audio onset\| at the last clap **203.6 ms**; all six adjusted: 98.9, 284.9, 214.4, 229.4, 316.6, 293.1; all six file-only: 13.9, 205.3, 104.5, 173.6, 201.9, 203.6 | 10.0 ms (synthetic) | **FAIL** | none exists for a timing fault; see root-cause notes |
| Dropped frames | under 1% | counter decoded from the pixels of all 3442 output frames: **157 of 3570** counter values missing, **4.40%** | 0% (never measured; 3012 "frames") | **FAIL** | none triggered; see notes |
| CPU | under 15% | 16.78 CPU s / (60.01 s x 32 cores) = **0.87%**, encoder `NVIDIA H.264 Encoder MFT` (hardware) | 0.21% | **PASS** | none |
| Loopback exclusion | probe tone under -60 dBFS | probe 440 Hz **-73.3 dBFS** in a 60 s decoded track; helper 660 Hz positive control **-20.0 dBFS** (nominal -20); the endpoint carried the probe tone at -18.8 dBFS | -96 dBFS (no control) | **PASS** | none (NAudio 3.1.0 process loopback) |
| Plays in media player and MediaElement | opens and plays | Media Foundation source reader decodes both the 60 s take and the recovered take to EOF; WPF `MediaElement` plays 2 s with the position advancing, seeks to near the end and reaches `MediaEnded`, for both files. The Windows Media Player app itself is **UNCOVERED**: the probe does not drive it | true/true | **PASS** (machine check) | none |
| Kill and recover | killed file plays, remuxes with video and both audio tracks | killed after 40.005 s (18.55 MB); recovered 2384 video frames over 39.9 s (**59.7 fps**), max pts gap 40.4 ms, tail lost vs kill 0.105 s, 4 counter values missing (0.17%); loopback track decodes to 39.9 s with the control tone at -20.0 dBFS; mic track decodes to 39.0 s | 615 frames, 37.2 s (16.5 fps) | **PASS** | none |

## Method (what the probe measures)

- **Source:** a WPF window shows a Gray-coded 20-bit frame counter (one value per rendered frame) and a flash panel. The probe decodes both from the Y plane of every output frame.
- **Clock:** one QPC start instant shared by the window, the helper process, audio and video.
- **Beep carrier:** the 1 kHz clap beep and a 660 Hz control tone come from a helper process started through CIM `Win32_Process.Create`, outside the probe's process tree. They are carried by the process-excluding loopback track; the probe's own 440 Hz tone is excluded from it.
- **Drift:** the file-only difference is dominated by the test window and the display path (the flash reaches the screen 59-95 ms after it is scheduled; the beep reaches the endpoint 142-175 ms after it is scheduled). The adjusted figure subtracts those: `|(video onset in file - first acquired capture frame showing the flash) - (audio onset in file - beep onset at the render endpoint)|`. Both figures fail the 40 ms bar, so the verdict does not depend on which is chosen.
- **Drops:** a control tap decodes the counter from every acquired desktop frame before any scaling or encoding, to separate display loss from pipeline loss.

## Explaining the numbers

**Source frame count against 60 s x 60 fps.** Capture acquired 3600 desktop frames (3571 distinct counter values); the encoder received 3442 because the record loop skips a frame that arrives less than 0.9/60 s after the previous one (158 skipped) to keep presentation times monotonic and the output near constant rate. The file therefore holds 3442 frames (57.1 fps average). Of the 157 missing counter values, 153 had been delivered to the capture loop and were lost after capture; that count is within five of the 158 frames the throttle discarded, so the throttle is the dominant cause. The display itself also coalesced 722 updates the capture loop never saw, and 5 counter values were skipped before capture.

**Drift is not explained.** The file's video onset is 150-251 ms later than the capture loop first saw the flash on five of six claps (33 ms on the first), while the audio onset is 35-69 ms earlier than the beep's endpoint time. Presentation times come from the QPC taken right after each acquire, so a constant 250 ms video lag in the file points at the encode or mux path (encoder latency or reordering not compensated in the timestamps), but this run did not isolate it. The 20 s runs showed the same size of offset (305 and 270 ms). It has not been fixed and the probe was not tuned to hide it.

**Counter versus presentation time.** The counter lags the wall clock by about 10 ms per second (200 ms in the first 10 s, 780 ms at the end), because the test window advances its counter per rendered frame and renders slightly under 60 Hz. This affects the counter-to-time mapping only, not the missing-value count.

**Recovered take frame rate.** The earlier 16.5 fps was an artefact of the remux. The new remux classifies streams by major type and feeds them in timestamp order. The first version of the fix fed the streams round-robin by sample count; with audio 3 s ahead of video the MP4 muxer blocked in `WriteSample` and the run hung for 40 minutes, so every post-take step now has a deadline and a timeout becomes a recorded failure.

## Caveats

- **Mic track is silent on this host.** The Realtek array reports digital silence (RMS -200 dBFS, thousands of discontinuous packets). Both audio tracks are present and decodable after remux, but no microphone signal was verified. The mic track's A/V alignment is UNCOVERED.
- **One run, one host.** One 60 s run on one laptop with one GPU; no run on the iGPU path.
- **HEVC** was enumerated (`NVIDIA HEVC Encoder MFT`) but never exercised. Only H.264 was recorded.
- **Windows Media Player app** was not driven; the machine check uses Media Foundation and `MediaElement`.

## Phase 7 verdict

The recording pipeline is **not yet cleared**. Capture, hardware encode, process-excluding loopback, fragmented MP4 recovery and playback all work, and CPU use is far below the bar. Two bars are not met: a roughly 250 ms video-to-audio offset in the file, and 4.4% missing frames, most of which come from the probe's own surplus-frame throttle. Neither has been shown to be inherent to the approach. Before phase 7 relies on this, find where the encoder path delays video, and stamp and write every acquired frame instead of dropping surplus frames. The spec's FFmpeg fallback is for remux or recovery failure and does not apply, because recovery passed.
