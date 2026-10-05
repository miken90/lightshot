# Phase 6: Auto Redact, OCR Text, QR/barcode

Status: pending | Effort: 9d | Priority: P1 | Depends on: Phase 4, Phase 5 (runs sequentially after phase 5; parallel only for Ml/ and its tests), Phase 2 (verdict E)

## Overview

Adds the on-device recognition features: Auto Redact in the editor, the OCR Text capture mode with the `TextCaptureStatus` HUD, and QR/barcode decoding. Manual redaction already shipped in phase 4. Translation is deferred to phase 10 (the HUD's Translate button stays hidden until then).

## Requirements

- OCR: `Windows.Media.Ocr` with the user-profile language plus a setting `ocr.language` listing `OcrEngine.AvailableRecognizerLanguages`; upscale small crops 2x; tile above `OcrEngine.MaxImageDimension`; map words to boxes and UTF-16 ranges. Language packs come from Windows Optional Features: guide the user when none is installed. DEGRADE: no automatic language detection, quality differs from Apple Vision (RULING §6).
- OCR Text capture (SPECS 1.6): dragged area's text is copied; QR/barcode wins over text, decoded and copied (never opened); several codes one per line; notices "QR code copied" and "Barcode copied"; `ocr.keepLineBreaks` default true; rows grouped at >= 50% vertical overlap; no history, no Repeat Last update; ignored while recording; `TextCaptureStatus` HUD states reading, copied, noText, failed, about 4 s.
- Auto Redact (SPECS 1.5): Ctrl+Shift+R in the editor; categories secret, paymentCard, bankAccount, email, phone, idNumber, ipAddress, postalAddress, link, face, code; off by default: postalAddress, link, face; padding 20% of line height, merge touching boxes, skip detections 80% covered; `add(contentsOf:)` is one undo step.
- Faces: YuNet ONNX on ONNX Runtime CPU, post-processing in C#, single-threaded and basic graph optimisation for cross-build determinism.
- Phone, link and address detection: libphonenumber-csharp plus link regex; postal address is heuristic and off by default.
- Everything on-device; no network.

## Data flow

Area selected (`selectFrozenArea`) -> `ITextRecognizer.RecognizeText(CapturedImage)` -> `TextRecognition { lines, codes }` -> `TextCapture.PlainText(...)` or code text -> `IImageSink.CopyText` -> HUD. Auto Redact: current base image -> recognizer (language correction not applicable) + `FaceDetector` + `CodeReader` -> Core `SensitiveDataScanner` (with `IWeakEntityDetector`) -> `AutoRedactPlan` -> redaction elements -> one undo step.

## Files

Create under `src/Lightshot.Platform.Windows/Ml/`:

| Path | Purpose |
|---|---|
| `WindowsTextRecognizer.cs` | `ITextRecognizer` over `OcrEngine`; `SoftwareBitmap` from BGRA |
| `OcrPreprocessor.cs` | 2x upscale, tiling with overlap, coordinate remap, tile de-duplication |
| `OcrLanguages.cs` | Available languages, guidance links (`ms-settings:regionlanguage`) |
| `ZXingCodeReader.cs` | ZXing.Net multi-decode, all formats, TryHarder, BGRA luminance source |
| `YuNetFaceDetector.cs`, `YuNetDecoder.cs` | ONNX session, letterbox, prior decoding (strides 8, 16, 32), NMS in C# |
| `PhoneLinkDetector.cs` | `IWeakEntityDetector` (libphonenumber-csharp, link regex, address heuristic) |
| `SensitiveContentRecognizer.cs` | Aggregates OCR, faces, codes for Auto Redact |
| `OnnxEnvironment.cs` | Shared ONNX Runtime options (CPU, 1 thread, `ORT_ENABLE_BASIC`) |

Create under `src/Lightshot.App/`:

| Path | Purpose |
|---|---|
| `Views/Editor/AutoRedactController.cs` | Ctrl+Shift+R, category list, progress, one-undo add |
| `Views/Notices/TextCaptureNotice.xaml(.cs)`, `TextCaptureNoticeController.cs` | HUD: topmost, no-activate, click-through unless an action button is shown, `WDA_EXCLUDEFROMCAPTURE`, screen under the pointer |
| `Views/Settings/OcrSection.xaml(.cs)` | Language, keep line breaks, Auto Redact categories (Advanced pane) |

Add assets: `assets/models/face_detection_yunet_2023mar.onnx` entry in `assets/models.lock.json` (MIT, SHA-256 pinned, fetched by `fetch-models.ps1`).

Tests: `tests/Lightshot.Platform.Windows.Tests/{OcrPreprocessorTests.cs, OcrFixtureTests.cs, ZXingCodeReaderTests.cs, YuNetDecoderTests.cs, YuNetFaceDetectorTests.cs, PhoneLinkDetectorTests.cs, AutoRedactFixtureTests.cs}`, `tests/Lightshot.Platform.Windows.Tests/Fixtures/` (images generated at test time with `Lightshot.Rendering` plus static QR/face images with licences recorded; openly licensed fixtures only, never user or children photos, using the WIDER FACE validation subset of >=200 faces from Spike E), `tests/Lightshot.App.Tests/AutoRedactControllerTests.cs`, `tests/Lightshot.App.UiTests/TextCaptureFlowTests.cs`.

Modify: `Lightshot.Core.Tests/AutoRedactTests` is not edited; detector-dependent cases are added in `PhoneLinkDetectorTests`. `AppController.cs` (CaptureUI text status), `TrayMenu.cs` (OCR Text item, disabled while recording). (Hotkeys: no default chord, nothing to change in Core).

## Implementation steps

1. `OcrPreprocessor`: choose upscale factor from crop height; tile with 10% overlap when a side exceeds `MaxImageDimension`; map boxes back; drop duplicates by IoU.
2. `WindowsTextRecognizer`: engine per language, cached; convert `OcrLine`/`OcrWord` to `RecognizedLine` with UTF-16 ranges; no-text returns an empty result, not an error; a missing language pack returns a typed `TextRecognitionError` that triggers the guidance dialog.
3. `ZXingCodeReader`: decode multiple codes, dedupe, return `RecognizedCode` with text and kind (QR vs barcode).
4. Wire `captureText`: onboarding gate, freeze, `selectFrozenArea`, HUD `reading`, recognise, QR/barcode wins, else `plainText(keepingLineBreaks)`, else `noText`; never writes history.
5. HUD controller: ~4 s auto-dismiss; pause on hover only when an action button is present; position on the monitor under the pointer.
6. `YuNetFaceDetector`: download via `fetch-models.ps1` (dev time), embed as content in the app folder; letterbox to a multiple of 32; decode scores and boxes; confidence and NMS thresholds fixed constants; return boxes in image pixels.
7. `PhoneLinkDetector`: `PhoneNumberUtil.FindNumbers` with the system region and lenient matching; link regex excluding `mailto`; address heuristic; pin fixtures and re-pin the source's phone/link/address expectations deliberately in `PhoneLinkDetectorTests`.
8. `SensitiveContentRecognizer` feeds the Core scanner; `AutoRedactController` runs it off the UI thread, applies `add(contentsOf:)`, shows a count and "Not secure for blur/pixelate" reminder.
9. Settings: OCR language and category toggles persist (`ocr.language`, `autoRedact.categories`); defaults per SPECS 1.5.
10. Record recall numbers against the spike thresholds; if below 90%, apply the retry strategy from the spike gate.

## Acceptance criteria

| Criterion | Test |
|---|---|
| Reading order, row grouping at 50% overlap, line-break option, code dedupe | `Lightshot.Core.Tests.TextCaptureTests` (14, phase 3) |
| QR/barcode wins over text; notice wording; never opened | `Lightshot.Core.Tests.AppCoordinatorTests` ported `captureText`/`codeCapture` cases |
| ZXing decodes QR and 1D codes, several per image | `Lightshot.Platform.Windows.Tests.ZXingCodeReaderTests.DecodesMultipleCodes` (`Unit`) |
| Tiling and upscale remap boxes correctly | `Lightshot.Platform.Windows.Tests.OcrPreprocessorTests.TilesRemapAndDeduplicate` (`Unit`) |
| OCR reads rendered fixtures (exact match after whitespace normalisation across >=200 entities, 3 fonts Segoe UI/Consolas/Inter, 11-24px, light/dark) | `Lightshot.Platform.Windows.Tests.OcrFixtureTests.ReadsRenderedCategoriesAtLeast90Percent` (`Media` tier: needs the OCR language pack) |
| Missing language pack produces guidance, not a crash | `Lightshot.Platform.Windows.Tests.OcrFixtureTests.MissingLanguageReturnsTypedError` (`Unit`, fake engine factory) |
| Scanner rules: Luhn, IBAN, SSN, key prefixes, labelled values, entropy | `Lightshot.Core.Tests.AutoRedactTests` (21 scanner cases, phase 3) |
| Phone, link, address detection pinned | `Lightshot.Platform.Windows.Tests.PhoneLinkDetectorTests` (new fixtures, `Unit`) |
| Weak matches dropped when overlapping strong ones | `Lightshot.Core.Tests.AutoRedactTests` ported overlap cases |
| YuNet decoding is deterministic and finds faces (openly licensed fixtures only, never user or children photos; >=200 faces from WIDER FACE subset per Spike E) | `Lightshot.Platform.Windows.Tests.YuNetDecoderTests.DecodesKnownTensorToBoxes` (`Unit`, canned tensor), `YuNetFaceDetectorTests.FindsFacesAtLeast90Percent` (`Media`) |
| Auto Redact is one undo step and categories default correctly | `Lightshot.App.Tests.AutoRedactControllerTests.AddsAllBoxesAsOneUndoStep` (`Unit`) and ported `AutoRedactTests` plan/undo cases |
| End to end: OCR Text hotkey copies text and shows the HUD | `Lightshot.App.UiTests.TextCaptureFlowTests.CopiesTextAndShowsNotice` (`Desktop`) |
| HUD stays out of captures | `Lightshot.Platform.Windows.Tests.OverlayExclusionTests.ExcludedWindowAbsentFromDdaAndWgc` extended with the HUD (`Desktop`) |
| No network use | `Lightshot.Architecture.Tests.NetworkPolicyTests` (created in phase 9; until then `Lightshot.Architecture.Tests.AssemblyReferenceTests.NoHttpClientInMlNamespace`, `Unit`) |
| Vision-parity OCR quality | UNCOVERED: no macOS host; ground-truth fixtures substitute (resolved decision 2026-10-05) |

## Rollback

All features are additive and isolated in `Ml/`. Disable by removing the hotkey and the editor button registration; no persisted data except two settings keys with defaults. The YuNet file is a content asset and can be deleted without code changes if `face` is hidden.

## Risks

| Risk | L | I | Mitigation |
|---|---|---|---|
| OCR language pack missing on the user's machine | H | M | Detect, guide to settings, remember the choice |
| Windows OCR recall on small or low-contrast text | M | M | 2x upscale, retry strategy from the spike gate |
| ONNX Runtime native DLL load fails (VC runtime) | L | M | Self-contained publish includes the runtime; startup self-check logs and disables faces |
| Face detector false positives annoy users | M | L | `face` off by default; conservative threshold |
| libphonenumber over-matches digit strings | M | M | Strong-match precedence rule in the Core scanner; fixtures |
| SoftwareBitmap conversion cost on 4K crops | L | L | Crop first, convert once |
| Clipboard text with RTL or surrogate pairs | L | L | UTF-16 range tests |

## Dependencies

Phase 4 (editor, capture, HUD placement helpers), phase 3 (scanner, `TextCapture`), phase 2 verdict E (thresholds, bundle budget). Runs sequentially after phase 5 (parallel work allowed only for `Ml/` folder and its tests while phase 5 runs; phase 5 supplies the Advanced pane). Blocks phase 10.
