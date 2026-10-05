# Spike E: On-Device ML Quality and Cost Report

**Date:** 2026-10-05 (re-run 18:00 +07 after the measurement fixes)
**Probe:** `spikes/ml-probe`
**Host:** Windows 11 (10.0.26200), AMD Ryzen 9 7945HX (32 logical cores), NVIDIA GeForce RTX 4060 Laptop GPU
**Command:** `scripts/spike.ps1 -Name ml -Configuration Release -Assert`
**Result artifact:** `artifacts/ml-probe-result.json` (timestamp 2026-10-05T18:00:29+07:00, gitignored)
**Overall status: FAIL.** OCR recall is below the bar. Captions, YuNet and translation pass.

Why this replaces the first report: the first run rendered OCR text at 2x/3x font size (crisp vector text), matched case-insensitively with punctuation stripped over whole 5-entity batches, used a 0.895 bar, and ran a 234.5 s narration. All four are fixed; the old numbers are listed only for comparison.

## 1. Results

| Criterion | Bar | Measured (new) | Old | Result | Fallback triggered | Measuring code |
|---|---|---|---|---|---|---|
| Captions word timing | median <= 300 ms | median 145.0 ms, p95 715.0 ms, max 1105.0 ms (750 of 812 TTS words matched) | 150.0 ms | PASS | none | `WhisperProbe.RunAsync` |
| Captions speed (CPU) | RTF < 1.0 | RTF 0.058 (22.0 s for 376.5 s audio) | 0.059 | PASS | none | `WhisperProbe.RunAsync` |
| Narration length | >= 300 s | 376.5 s (812 words) | 234.5 s | valid | none | `WhisperProbe.RunAsync` (`durationValid`) |
| OCR recall, strict | >= 90 % over >= 200 entities | **67.4 % (151 / 224)** | 91.96 % | **FAIL** | per-line 3x retry + binarised variant already applied (recovered 11); spec fallback: Phase 6 adds a retry strategy, +2d | `OcrProbe.RunAsync`, `IsEntityMatchedStrict` |
| YuNet recall, prominent faces | >= 90 % over >= 200 faces | 91.54 % (184 / 201, 59 images) | 91.54 % | PASS on the filtered set only | none | `YuNetProbe.Run`, `CountMatched` |
| YuNet recall, all valid GT faces, same 59 images | informational | 62.37 % (590 / 946) | not measured | informational | none | `YuNetProbe.Run`, `CountMatched` |
| Translation en->vi | chrF >= 45 (or BLEU >= 25), p50 <= 300 ms | chrF 57.07, BLEU 36.02, p50 64.7 ms, p95 98.1 ms | chrF 57.07, p50 77.1 ms | PASS | none | `OpusMtProbe.Run` |
| Translation vi->en | same | chrF 65.75, BLEU 39.89, p50 58.4 ms, p95 88.9 ms | chrF 65.75, p50 56.2 ms | PASS | none | `OpusMtProbe.Run` |

Pass bars are now 0.90 in `OcrProbe.cs` and `YuNetProbe.cs` (was 0.895). The first run's YuNet figure is unchanged because the bar fix did not move 91.54 %.

### OCR details
- **Fixtures:** 224 seeded entities, 7 Auto Redact categories, Segoe UI / Consolas / Inter, font sizes 11, 12, 14, 16, 18, 20, 24 px (min 11, max 24), light and dark backgrounds.
- **Rendering:** each batch of 5 entities is drawn at native size (1x, grayscale antialiasing), then the whole bitmap is upscaled 2x with bicubic (Mitchell) resampling. The 3x retry upscales a native-size single-entity bitmap and binarises it; text is never re-rendered at a larger size. Dark fixtures are colour-inverted before OCR (a preprocessing choice the product would also have to make).
- **Strict rule (PASS/FAIL):** case-sensitive ordinal equality between one OCR line and the entity, after whitespace normalisation, per entity.
- **Strict, first pass only (no retry):** 62.5 % (140 / 224). **Lenient, informational and not used for the verdict** (case-insensitive, punctuation stripped, over batch text, retry included): 79.5 % (178 / 224).
- **Recall by font:** Segoe UI 76.0 %, Consolas 45.3 %, Inter 81.1 %.
- **Recall by category:** secrets 50.0 %, cards 87.5 %, IBANs 71.9 %, emails 62.5 %, SSN 96.9 %, IP 81.2 %, labelled values 21.9 %.
- Even the lenient number is below 90 %, so the failure is not only the stricter matcher; the first report's 91.96 % came from large crisp text plus a forgiving matcher. No threshold, fixture or matcher was tuned to reach the bar.

### YuNet details
- Model `face_detection_yunet_2023mar.onnx` (OpenCV Zoo, Apache-2.0); WIDER FACE validation `0--Parade` subset (CC BY-SA 3.0); score threshold 0.6, NMS 0.3, match IoU >= 0.4, native image size padded to a multiple of 32.
- **Filter on the pass row:** prominent faces only, with w >= 40 px, h >= 40 px, blur <= 1, occlusion <= 1 and invalid = 0. Images are evaluated in file order until the filtered count reaches 200.
- The unfiltered row uses every GT face with invalid = 0 on the same 59 images. It is far lower (62.37 %), so the 91.54 % applies to large, mostly clear faces only. Whether that is acceptable for the product is a decision for the chief.

### Caption details
- Windows TTS (`System.Speech`), 16 kHz 16-bit mono, word-boundary events as ground truth. Whisper.net `ggml-base-q5_1`, token timestamps, CPU only. CUDA/Vulkan were not tried (UNCOVERED).
- 62 of 812 words were not matched to a Whisper token and are excluded from the error statistics.

## 2. Model and runtime footprint

| Component | Size (MB) | Decision |
|---|---|---|
| YuNet ONNX | 0.22 | Mandatory bundled |
| ONNX Runtime CPU DLL | 15.70 | Mandatory bundled |
| Windows.Media.Ocr | 0 | OS built-in |
| Whisper `ggml-base-q5_1` | 56.94 | Optional download |
| OPUS-MT en-vi (encoder 178.08, decoder 307.33, spm 0.77) | 486.18 | Optional download |
| OPUS-MT vi-en (encoder 178.18, decoder 307.54, spm 0.72) | 486.44 | Optional download |
| **Mandatory total** | **15.92** | |
| **Optional total** | **1029.56** | |

## 3. Licences and provenance
YuNet: Apache-2.0 (OpenCV Zoo). WIDER FACE: CC BY-SA 3.0. OPUS-MT: Apache-2.0. Tatoeba 100 pairs: CC BY 2.0 FR. Inter: SIL OFL 1.1 (`spikes/ml-probe/Fixtures/Inter-OFL.txt`). Whisper base q5_1: MIT.
