# Spike E: On-Device ML Quality and Cost Report

**Date:** 2026-10-05  
**Probe:** `spikes/ml-probe`  
**Host:** Windows 11 Pro 64-bit (Build 26200), AMD Ryzen 9 7945HX (32 logical cores), NVIDIA GeForce RTX 4060 Laptop GPU  
**Command:** `powershell.exe -NoProfile -File scripts/spike.ps1 -Name ml -Configuration Release -Assert`  
**Overall Status:** PASS  

---

## 1. Hardware Matrix Covered

| Property | Value / Configuration |
|---|---|
| OS Version | Microsoft Windows NT 10.0.26200.0 (Windows 11 Build 26200) |
| Architecture | X64 |
| CPU | AMD Ryzen 9 7945HX with Radeon Graphics (16 cores / 32 threads) |
| GPU | NVIDIA GeForce RTX 4060 Laptop GPU (Driver 572.16) |
| Audio Subsystem | Windows TTS (`System.Speech.Synthesis`) 16 kHz 16-bit Mono PCM |
| OCR Engine | Windows.Media.Ocr (`en-US` language pack, OS built-in) |
| ML Runtime | Microsoft.ML.OnnxRuntime 1.20.1 (CPU execution provider) + Whisper.net 1.7.4 |

---

## 2. Pass Criteria and Measured Results

| Criterion | Numeric Bar | Measured Value | Result | Fallback Triggered | Measuring Code |
|---|---|---|---|---|---|
| **Whisper Captions Word Timing** | Median error <= 300 ms | **150.0 ms median** (P95: 715.0 ms, Max: 1025.0 ms) | **PASS** | None | `WhisperProbe.RunAsync` (`WhisperProbe.cs`) |
| **Whisper Captions Speed (CPU)** | Real-Time Factor (RTF) < 1.0 | **RTF = 0.059** (16.9x faster than real-time; 13.9s for 234.5s audio) | **PASS** | None | `WhisperProbe.RunAsync` (`WhisperProbe.cs`) |
| **Windows.Media.Ocr Recall** | Overall Recall >= 90.0% (>= 200 entities, 3 fonts, 7 categories, 11-24 px) | **91.96% recall** (206 / 224 matched) | **PASS** | Retry at 3x upscale with binarization triggered for difficult glyphs | `OcrProbe.RunAsync` (`OcrProbe.cs`) |
| **YuNet Face Detection Recall** | Recall >= 90.0% on >= 200 open faces (WIDER FACE validation, IoU >= 0.4, score 0.6, NMS 0.3) | **91.54% recall** (184 / 201 matched on 59 validation images) | **PASS** | None | `YuNetProbe.Run` (`YuNetProbe.cs`) |
| **OPUS-MT EN -> VI Translation Quality** | chrF >= 45.0 (or BLEU >= 25.0) on 100 sentences | **chrF = 57.07**, **BLEU = 36.02** | **PASS** | None | `OpusMtProbe.Run` (`OpusMtProbe.cs`) |
| **OPUS-MT EN -> VI Translation Latency** | p50 latency <= 300 ms per sentence (CPU) | **p50 = 77.1 ms** (p95: 118.6 ms, max: 135.4 ms) | **PASS** | None | `OpusMtProbe.Run` (`OpusMtProbe.cs`) |
| **OPUS-MT VI -> EN Translation Quality** | chrF >= 45.0 (or BLEU >= 25.0) on 100 sentences | **chrF = 65.75**, **BLEU = 39.89** | **PASS** | None | `OpusMtProbe.Run` (`OpusMtProbe.cs`) |
| **OPUS-MT VI -> EN Translation Latency** | p50 latency <= 300 ms per sentence (CPU) | **p50 = 56.2 ms** (p95: 88.8 ms, max: 113.2 ms) | **PASS** | None | `OpusMtProbe.Run` (`OpusMtProbe.cs`) |

---

## 3. Detailed Raw Measurements

### 3.1 Part 1: Captions (Whisper.net with `ggml-base-q5_1.bin`)
- **Audio Ground Truth:** Windows TTS generated narration (16 kHz, 16-bit mono PCM).
  - Audio Duration: 234.55 s (~4.0 minutes narration, 483 words).
  - Audio File Size: 7.16 MB.
- **Whisper Processing Time (CPU):** 13.95 s.
- **Real-Time Factor (RTF):** `13.95 / 234.55 = 0.0595` (16.9x real-time speed on AMD Ryzen 9 7945HX CPU).
- **Recognition Accuracy:** 576 tokens recognized, 439 words aligned with ground truth TTS boundary events (90.9% match).
- **Word-Timing Errors (Matched Words):**
  - **Median Error:** 150.0 ms (Passes <= 300 ms bar).
  - **P95 Error:** 715.0 ms.
  - **Max Error:** 1025.0 ms.

### 3.2 Part 2: OCR (`Windows.Media.Ocr` on Auto Redact Fixtures)
- **Dataset:** 224 seeded synthetic entities across 7 Auto Redact categories, 3 typefaces, 5 font sizes (12, 14, 16, 18, 22 px), balanced light and dark backgrounds.
- **Overall Recall:** **91.96%** (206 / 224 matched).
- **Recall by Typeface:**
  - `Segoe UI`: 90.7% (68 / 75)
  - `Consolas`: 98.7% (74 / 75)
  - `Inter`: 86.5% (64 / 74)
- **Recall by Auto Redact Category:**
  - `secrets`: 68.8% (22 / 32) (complex mixed-case high-entropy API tokens)
  - `cards`: 100.0% (32 / 32)
  - `IBANs`: 100.0% (32 / 32)
  - `emails`: 96.9% (31 / 32)
  - `SSN`: 100.0% (32 / 32)
  - `IP`: 100.0% (32 / 32)
  - `labelled values`: 78.1% (25 / 32)
- **Fallback Triggered:** Per-line retry at 3x scale with adaptive binarization / luminance inversion triggered for dark mode and low-contrast glyphs, elevating overall recall above the 90% threshold.

### 3.3 Part 3: Face Detection (OpenCV Zoo YuNet ONNX on WIDER FACE)
- **Model:** `face_detection_yunet_2023mar.onnx` (OpenCV Zoo, Apache 2.0).
- **Evaluation Dataset:** WIDER FACE validation split (`0--Parade` subset, CC BY-SA 3.0).
- **Receptive Field Strategy:** Dynamic native image resolution with stride-32 padding (avoids aspect ratio distortion and interpolation blur).
- **Ground Truth Prominent Faces:** $W \ge 40$, $H \ge 40$, clear/normal blur ($blur \le 1$), valid annotation ($invalid = 0$).
- **Matching Rule:** Hungarian/Greedy IoU match threshold $\ge 0.4$ at default confidence (score $\ge 0.6$) and NMS threshold $0.3$.
- **Results:**
  - Evaluated Images: 59 images.
  - Ground Truth Faces: 201 faces.
  - Matched Faces: 184 faces.
  - **Recall:** **91.54%** (Passes $\ge 90.0\%$ bar on $\ge 200$ faces).

### 3.4 Part 4: Translation (OPUS-MT ONNX on Tatoeba Parallel Corpus)
- **Models:** `Helsinki-NLP/opus-mt-en-vi` and `Helsinki-NLP/opus-mt-vi-en` exported to ONNX via Hugging Face Optimum.
- **Tokenizer:** Marian-aware SentencePiece Unigram tokenizer with Viterbi dynamic programming segmentation.
- **Evaluation Corpus:** Tatoeba Project (`tatoeba.org`), 100 parallel sentence pairs (CC BY 2.0 FR), length 4-15 words.
- **English -> Vietnamese (`en->vi`):**
  - **chrF:** 57.07 (Passes $\ge 45.0$ bar).
  - **BLEU:** 36.02 (Passes $\ge 25.0$ bar).
  - **p50 Latency:** 77.1 ms per sentence on CPU (Passes $\le 300$ ms bar).
  - **p95 Latency:** 118.6 ms per sentence on CPU.
  - **Max Latency:** 135.4 ms.
- **Vietnamese -> English (`vi->en`):**
  - **chrF:** 65.75 (Passes $\ge 45.0$ bar).
  - **BLEU:** 39.89 (Passes $\ge 25.0$ bar).
  - **p50 Latency:** 56.2 ms per sentence on CPU (Passes $\le 300$ ms bar).
  - **p95 Latency:** 89.4 ms per sentence on CPU.
  - **Max Latency:** 113.2 ms.

---

## 4. Model and Runtime Footprint Budget (`package.ps1 -MaxSetupMB`)

| Component / Artifact | File / Package | Size (MB) | Setup Budget Decision |
|---|---|---|---|
| **YuNet Face Detection** | `face_detection_yunet_2023mar.onnx` | 0.22 MB | **Mandatory Bundled** (Setup payload: +0.22 MB) |
| **Windows OCR Engine** | `Windows.Media.Ocr` | 0.00 MB | **Mandatory Zero-Cost** (OS built-in runtime) |
| **ONNX Runtime (CPU)** | `onnxruntime.dll` | 15.70 MB | **Mandatory Bundled** (Shared across YuNet and translation) |
| **Whisper Base Model** | `ggml-base-q5_1.bin` | 56.94 MB | **Optional On-Demand** (or default bundle if budget permits <= 75 MB) |
| **OPUS-MT en->vi Model** | `encoder_model.onnx` + `decoder_model.onnx` + `spm` | 486.18 MB | **Optional On-Demand** (Language pack download) |
| **OPUS-MT vi->en Model** | `encoder_model.onnx` + `decoder_model.onnx` + `spm` | 486.44 MB | **Optional On-Demand** (Language pack download) |
| **Total Mandatory Bundle** | YuNet + OnnxRuntime DLL | **15.92 MB** | Fits comfortably within initial setup budget |
| **Total Optional Packages** | Whisper Base + OPUS-MT en<->vi | **1,029.56 MB** | Downloaded dynamically to user AppData when enabled |

---

## 5. Licences and Provenance

1. **YuNet:** OpenCV Zoo (`face_detection_yunet_2023mar.onnx`), licensed under Apache License 2.0. Copyright (c) Shenzhen Institute of Artificial Intelligence and Robotics for Society.
2. **WIDER FACE:** Validation split (`0--Parade`), CUHK Multimedia Lab, licensed under CC BY-SA 3.0.
3. **OPUS-MT:** `Helsinki-NLP/opus-mt-en-vi` and `Helsinki-NLP/opus-mt-vi-en`, University of Helsinki / OPUS project, licensed under Apache License 2.0.
4. **Tatoeba:** 100 parallel sentence pairs from `tatoeba.org`, licensed under Creative Commons Attribution 2.0 France (CC BY 2.0 FR).
5. **Inter Typeface:** `Inter-Regular.ttf`, Rasmus Andersson, licensed under SIL Open Font License 1.1 (`spikes/ml-probe/Fixtures/Inter-OFL.txt`).
6. **Whisper:** OpenAI Whisper `base` quantized `q5_1`, MIT License.
