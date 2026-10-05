# Phase 10: On-device translation (degraded)

Status: pending | Effort: 7d | Priority: P3 | Depends on: phases 2 (verdict E), 6, 9 (phase 9 for NetworkPolicyTests)

## Overview

Replaces Apple's Translation framework for the "Translate" action in the text-capture HUD and for a Translate window. Windows has no system translator, so this uses OPUS-MT (Marian) models on ONNX Runtime. This is a recorded CONFLICT with the source and ships as a degraded feature: fewer language pairs, lower quality, models imported locally (never downloaded by the app).

## Requirements

- Bundled pair and language packs: en<->vi (`opus-mt-en-vi` and `opus-mt-vi-en`, CC-BY 4.0) is the one bundled pair (resolved decision 2026-10-05). Both models are added to `assets/models.lock.json` and counted in the installer size gate (int8 if needed to stay under `-MaxSetupMB`). Other pairs are local imports from a user-chosen folder (the pack folder holds encoder, decoder with past KV cache, tokenizer, vocab, config, and SHA-256 manifest); other pairs are never fetched over the network.
- Marian tokenisation through SentencePiece (managed library chosen in step 1), vocab mapping between tokenizer ids and model ids, greedy decode with the KV cache, max length from config, repetition guard; single thread deterministic settings.
- Translate window: input text (from OCR HUD or typed), source and target pickers limited to imported pairs, copy result, progress and cancel, clear "not installed" guidance with an Import button.
- HUD: the Translate button appears only when at least one pack is imported and a code was not the result (QR/barcode wins; Core `TextCapture` rule).
- Attribution: OPUS-MT models are CC-BY 4.0; `THIRD-PARTY-NOTICES` and the pack manifest display the attribution; the Translate window shows a link to the notice.
- No network use; models loaded lazily; unloaded after idle.
- Feasibility is decided by spike E: if quality or latency fails the bar, this phase is replaced by a documented "not supported" state and the HUD button stays hidden. This outcome is acceptable and is recorded in plan.md.

## Data flow

```
text -> sentence splitter -> SentencePiece (source) -> encoder -> greedy decode loop (decoder + KV cache)
 -> target ids -> SentencePiece decode -> text -> clipboard / window
```

## Files

Create under `src/Lightshot.Platform.Windows/Ml/Translation/`: `OpusMtTranslator.cs` (`ITranslator`), `TranslationPackStore.cs`, `PackManifest.cs`, `MarianTokenizer.cs`, `VocabMapper.cs`, `GreedyDecoder.cs`, `KvCache.cs`, `SentenceSplitter.cs`.

Create under `src/Lightshot.App/Views/Translate/`: `TranslateWindow.xaml(.cs)`, `TranslateViewModel.cs`, `PackImportDialog.xaml(.cs)`; create `src/Lightshot.App/Views/Settings/TranslationPane.xaml(.cs)` (Translation pack list); modify `Views/Notices/TextCaptureNotice.xaml(.cs)` (Translate button), `THIRD-PARTY-NOTICES`.

Add assets: `assets/models/opus-mt-en-vi.onnx` and `assets/models/opus-mt-vi-en.onnx` entries in `assets/models.lock.json` (CC-BY 4.0, SHA-256 pinned, fetched by `fetch-models.ps1`).

Tests: `tests/Lightshot.Platform.Windows.Tests/{MarianTokenizerTests, VocabMapperTests, GreedyDecoderTests, TranslationPackStoreTests, OpusMtTranslatorTests, SentenceSplitterTests}`, `tests/Lightshot.App.Tests/TranslateViewModelTests.cs`, `tests/Lightshot.App.UiTests/TranslateFlowTests.cs`.

## Implementation steps

1. Re-read spike E results; pick the SentencePiece library; confirm export format of the ONNX packs (separate encoder and decoder with past, or merged).
2. `TranslationPackStore`: import, validate hashes, list pairs, remove pack.
3. Tokenizer and vocab mapping with fixtures from the pack's own `source.spm`.
4. Greedy decoder with KV cache; tests with a tiny stub model (generated ONNX test graph) for loop logic, plus a real-pack test in the `Media` tier.
5. Translator service honouring cancellation; chunk by sentence; join with original separators.
6. UI wiring and HUD button visibility rule.
7. Notices and attribution; guidance when no pack is imported.

## Acceptance criteria

| Criterion | Test |
|---|---|
| Tokenizer round trip and Marian-specific cases (language tokens, unknown pieces) | `Lightshot.Platform.Windows.Tests.MarianTokenizerTests.RoundTripsFixtureSentences` (`Unit`) |
| Vocab mapping stable | `Lightshot.Platform.Windows.Tests.VocabMapperTests.MapsIdsBothWays` (`Unit`) |
| Decode loop stops at end token, respects max length, guards repetition | `Lightshot.Platform.Windows.Tests.GreedyDecoderTests` (`Unit`, stub graph) |
| Pack import verifies hashes and rejects tampering | `Lightshot.Platform.Windows.Tests.TranslationPackStoreTests.RejectsHashMismatch` (`Unit`) |
| Real pack translates a fixture sentence set above the Spike E numeric bar (chrF >= 45, BLEU >= 25, <=300ms p50 per sentence) | `Lightshot.Platform.Windows.Tests.OpusMtTranslatorTests.TranslatesFixturesAboveQualityBar` (`Media`, bundled en<->vi pack) |
| Translate button hidden without packs or when a code was the result | `Lightshot.App.Tests.TranslateViewModelTests.ButtonVisibilityFollowsPacksAndResultKind` (`Unit`) |
| Sentence splitting keeps separators | `Lightshot.Platform.Windows.Tests.SentenceSplitterTests` (`Unit`) |
| No network use | `Lightshot.Architecture.Tests.NetworkPolicyTests` (phase 9) |
| End to end from the HUD to the clipboard | `Lightshot.App.UiTests.TranslateFlowTests.TranslatesCapturedTextToClipboard` (`Desktop`, needs a pack) |
| Parity with Apple Translation quality and language coverage | UNCOVERED: no Apple host and no equivalent system service; degraded by decision |

## Rollback

Self-contained: remove the HUD button and the window; packs sit in a user folder and are inert. No settings except the pack folder path.

## Risks

| Risk | L | I | Mitigation |
|---|---|---|---|
| Quality or speed below bar | M | M | Spike E gate; "not supported" fallback |
| ONNX export variants of OPUS-MT differ between packs | M | M | Manifest declares layout; tests with two layouts |
| Tokenizer edge cases (Marian vocab mapping) | H | M | Fixtures from real packs; fail with a clear error, never silent garbage |
| CC-BY attribution omitted | L | M | Notice test checks the string |
| Memory use of loaded models | M | L | Lazy load and idle unload |
| Users expect Apple's coverage | H | L | Window states supported pairs and the degraded nature |

## Dependencies

Phase 2 verdict E, phase 6 (HUD and OCR text), phase 9 only for NetworkPolicyTests. Can ship after the first release.
