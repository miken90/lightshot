# Spike B: Real-Time Recording Pipeline Report

**Date:** 2026-10-05
**Probe:** `spikes/recording-probe`
**Host:** Windows 11 build 26200, NVIDIA GeForce RTX 4060 Laptop GPU, 32 logical cores, display 1920x1080 at 125% DPI, mic `Microphone Array (Realtek(R) Audio)`, render `Speakers (Realtek(R) Audio)`
**Overall status:** **FAIL** (A/V drift fails; the other five pass). Second measured run after the lag and frame-loss fix; the first measured run is kept as "Old".

## Why this report replaced the earlier one

The first version of this report (commit 2d44923) said all six criteria passed. Those numbers were not measurements and have been replaced:

- A/V drift was computed as `expectedSec + i*0.002` and capped; no flash or beep was ever detected in a file.
- Dropped frames never read a frame counter (the decode index was added to the "seen" set), and it claimed 3012 source frames for a 60 s, 60 fps take (3600 expected).
- Loopback exclusion had no positive control, so an empty track or a swallowed decode error would have read as -96 dBFS and passed.
- The recovered 40 s take had 615 frames in 37.2 s (about 16.5 fps), which was never explained. The cause was a broken remux that mapped streams by position instead of by type.

Every "New" value below is read from the decoded output of the run in `artifacts/recording-probe-result.json` (60 s take plus a 40 s kill-and-recover take, 2026-10-05 15:35). "Old" is the first measured run (commit 1700f94). Nothing is capped, clamped or defaulted; a value that cannot be measured is UNCOVERED.

## Results

| Criterion | Bar | New (after fix) | Old (first measured run) | Result | Fallback |
|---|---|---|---|---|---|
| A/V drift | under 40 ms at the last clap | last clap **61.4 ms** adjusted; all six adjusted: 75.4, 54.9, 81.0, 64.0, 39.9, 61.4; file-only \|video - audio\| 24.8-26.4 ms on all six claps (no growth) | last clap 293.1 ms adjusted (98.9, 284.9, 214.4, 229.4, 316.6, 293.1); file-only 203.6 ms | **FAIL** | phase 7: stamp loopback audio against the render endpoint clock; see notes |
| Dropped frames | under 1% | **9 of 3584** counter values missing, **0.251%**; all 9 were never presented by the test window (skipped before capture); 0 lost after capture; 3579 acquired, 3583 written | 157 of 3570, 4.40%; 158 frames discarded by the throttle | **PASS** | none |
| CPU | under 15% | 18.38 CPU s / (60.02 s x 32 cores) = **0.96%**, `NVIDIA H.264 Encoder MFT` | 0.87% | **PASS** | none |
| Loopback exclusion | probe tone under -60 dBFS | probe 440 Hz **-73.2 dBFS**; helper control 660 Hz -20.0 dBFS; endpoint carried the probe tone at -18.0 dBFS | -73.3 dBFS, control -20.0 | **PASS** | none (NAudio 3.1.0 process loopback) |
| Plays in media player and MediaElement | opens and plays | MF source reader decodes the 60 s take and the recovered take to EOF; `MediaElement` plays, seeks near the end and reaches `MediaEnded`, for both. Windows Media Player app **UNCOVERED** | same machine check passed | **PASS** (machine check) | none |
| Kill and recover | killed file plays, remuxes with video and both audio tracks | killed after 40.0 s (22.3 MB); recovered 2373 frames over 39.6 s (**59.9 fps**), max pts gap 35.5 ms, tail lost 0.42 s, 1 counter value missing (0.04%) | 2384 frames, 59.7 fps, 4 missing | **PASS** | none |

## Method (what the probe measures)

- **Source:** a WPF window shows a Gray-coded 20-bit frame counter (one value per rendered frame) and a flash panel. The probe decodes both from the Y plane of every output frame.
- **Clock:** one QPC start instant shared by the window, the helper process, audio and video.
- **Beep carrier:** the 1 kHz clap beep and a 660 Hz control tone come from a helper process started through CIM `Win32_Process.Create`, outside the probe's process tree. They are carried by the process-excluding loopback track; the probe's own 440 Hz tone is excluded from it.
- **Drift:** `|(video onset in file - first acquired capture frame showing the flash) - (audio onset in file - beep onset at the render endpoint)|`, the endpoint onset taken from an all-process endpoint loopback. Unchanged from the first run.
- **Per-stage timing (new, diagnostic only):** the probe records, per frame, the acquire QPC, the sample time handed to the sink writer and the tapped counter, and keeps the loopback PCM it handed to the writer. Each clap then reports file minus writer input and writer input minus capture or endpoint, for video and audio. None of these feed a criterion.

## Root causes and fixes

1. **Fixed 1/60 s sample durations (fixed).** The sink writer's fragmented MP4 has no `tfdt` boxes; the only timeline is the running sum of sample durations. The probe gave every frame 1/60 s while real gaps were 15-34 ms, so ffprobe showed the file timeline 518 ms behind the input by 20 s. Each frame is now held until the next one is acquired and written with the real gap.
2. **Media Foundation applies `tfra` times to the wrong sample (platform defect, mitigated).** The writer's `mfra`/`tfra` index is correct (keyframe time and sample number), but the Windows MPEG-4 source applies that time to the first sample of the fragment. With 19-frame fragments and a 60-frame GOP, decoded timestamps jump by up to about 300 ms in a sawtooth. This was the 98-317 ms "drift" of the first run; ffprobe, which ignores `tfra`, did not show it. The probe now truncates the trailing `mfra` box after finalize; MF then derives times from the fragments and seeks still work (MediaElement check above). Phase 7 must do the same or write `tfdt` itself.
3. **Tracks started at their own first sample (fixed).** Without an edit list each track's time starts at its first sample, losing 8-13 ms. The first video frame now starts at the shared start instant, the audio tracks start with silence up to their first packet, and each audio track is kept contiguous against its QPC stamps (AAC time in MP4 is the sample count).
4. **Surplus-frame throttle (removed).** It discarded 158 delivered frames. Every acquired frame is now written; the output is variable frame rate.

After the fix, the stages agree to within a millisecond on the 60 s take: decoded video PTS minus input sample time is -4.1 to +0.1 ms (median -0.07 ms) over 3583 frames, with the same counter at the same index; the decoded beep onset equals the onset in the PCM handed to the writer (0.0 ms on all six claps).

## Why drift still fails

The remaining 40-81 ms is not in the recording pipeline. It is the disagreement between two capture paths on the same beep:

- The process-loopback tap (the recorded track) shows each beep **90.0 ms** after its scheduled time on all six claps, identical to 1 ms.
- The endpoint loopback (the reference) shows it 129.7 to 170.5 ms after, varying by 41 ms without a trend (no accumulating drift).
- The reference stamps (arrival minus chunk length) are within 3.0-6.8 ms of the endpoint's own sample clock at the beep chunks, so the variation is not a stamping artefact of the reference; correcting for it moves the last clap only from 61.4 to 56.9 ms.
- Video is exact: the file's flash frame is within 0.32 ms of the first capture frame that showed it. The file-only video-to-audio gap is a constant 26 ms.

So the file is internally consistent and does not drift; what varies is the latency between the per-process tap and the mixed endpoint stream (the shared-mode engine and APO path). Whether the user hears the endpoint timing or the tap timing cannot be settled without an acoustic or external reference. **Phase 7 mitigation:** stamp the loopback track against the render endpoint clock (`IAudioClock` position on the render device, smoothed by a drift-tracking filter) instead of the tap's `qpcPosition`, and validate with an acoustic loop (speaker to a working mic) on a host whose mic is live.

## Caveats

- **Mic track is silent on this host.** The Realtek array reports digital silence (RMS -200 dBFS, thousands of discontinuous packets). Both audio tracks are present and decodable after remux, but no microphone signal was verified. The mic track's A/V alignment is UNCOVERED.
- **Mic contiguity edits.** The mic is stamped on arrival, and its stamps jitter by more than the 20 ms tolerance: the 60 s take inserted 1,177,200 silence frames and trimmed 1,176,000 (the loopback needed 148 and 0). The mic was digital silence, so no criterion is affected, but phase 7 must stamp the mic from the device position (`qpcPosition`), as the loopback is, before keeping it contiguous.
- **One run per version, one host.** One 60 s run on one laptop with one GPU; no run on the iGPU path.
- **HEVC** was enumerated (`NVIDIA HEVC Encoder MFT`) but never exercised. Only H.264 was recorded.
- **Windows Media Player app** was not driven; the machine check uses Media Foundation and `MediaElement`.

## Phase 7 verdict

The recording pipeline is **cleared except for audio reference timing**. Capture, hardware encode, process-excluding loopback, fragmented MP4 recovery and playback work; frame loss after capture is zero and CPU is under 1%. The lag in the first run came from the probe's fixed sample durations and from the Media Foundation `tfra` defect, both now fixed or mitigated with evidence. The drift bar still fails (last clap 61.4 ms against 40 ms) because the process-loopback tap and the endpoint disagree by a varying amount; phase 7 must stamp loopback audio against the render endpoint clock and confirm with an acoustic reference. The FFmpeg fallback is for remux or recovery failure and does not apply.
