using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MlProbe;

public class TranslationDirectionResult
{
    public string Direction { get; set; } = "";
    public bool Pass { get; set; }
    public double ChrFScore { get; set; }
    public double BleuScore { get; set; }
    public double P50LatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
    public double MaxLatencyMs { get; set; }
    public int SentenceCount { get; set; }
    public string? ErrorMessage { get; set; }
}

public class OpusMtProbeResult
{
    public bool Pass { get; set; }
    public TranslationDirectionResult EnToVi { get; set; } = new();
    public TranslationDirectionResult ViToEn { get; set; } = new();
    public string CorpusSource { get; set; } = "Tatoeba (tatoeba.org)";
    public string CorpusLicense { get; set; } = "CC BY 2.0 FR";
    public string ModelSource { get; set; } = "Hugging Face Helsinki-NLP/opus-mt-en-vi & opus-mt-vi-en (Apache-2.0)";
    public string? FallbackTriggered { get; set; }
    public string? ErrorMessage { get; set; }
}

public class MarianSpmTokenizer
{
    public record PieceEntry(string Piece, float Score, int TokenId);

    private readonly Dictionary<string, PieceEntry> _vocab = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _idToPiece = new();
    private readonly int _unkId;
    private readonly int _eosId;

    public MarianSpmTokenizer(string spmVocabPath, string vocabJsonPath, int unkId = 1, int eosId = 0)
    {
        _unkId = unkId;
        _eosId = eosId;

        // Load vocab.json (token string -> model id)
        var vocabJson = File.ReadAllText(vocabJsonPath);
        using var vocabDoc = JsonDocument.Parse(vocabJson);
        var modelVocab = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var prop in vocabDoc.RootElement.EnumerateObject())
        {
            modelVocab[prop.Name] = prop.Value.GetInt32();
            _idToPiece[prop.Value.GetInt32()] = prop.Name;
        }

        // Load spm_vocab.json (piece -> score)
        var spmJson = File.ReadAllText(spmVocabPath);
        using var spmDoc = JsonDocument.Parse(spmJson);
        foreach (var elem in spmDoc.RootElement.EnumerateArray())
        {
            string piece = elem.GetProperty("piece").GetString() ?? "";
            float score = (float)elem.GetProperty("score").GetDouble();
            if (modelVocab.TryGetValue(piece, out int modelId))
            {
                _vocab[piece] = new PieceEntry(piece, score, modelId);
            }
        }
    }

    public List<long> Encode(string text)
    {
        string norm = "\u2581" + text.Trim().Replace(" ", "\u2581");
        int n = norm.Length;
        float[] dp = new float[n + 1];
        Array.Fill(dp, float.NegativeInfinity);
        dp[0] = 0.0f;

        int[] parentIdx = new int[n + 1];
        int[] parentId = new int[n + 1];

        for (int i = 0; i < n; i++)
        {
            if (float.IsNegativeInfinity(dp[i]))
                continue;

            int maxLen = Math.Min(n - i, 32);
            for (int len = 1; len <= maxLen; len++)
            {
                string sub = norm.Substring(i, len);
                if (_vocab.TryGetValue(sub, out var entry))
                {
                    if (dp[i] + entry.Score > dp[i + len])
                    {
                        dp[i + len] = dp[i] + entry.Score;
                        parentIdx[i + len] = i;
                        parentId[i + len] = entry.TokenId;
                    }
                }
            }

            // Unk fallback for single char if unreachable
            if (float.IsNegativeInfinity(dp[i + 1]))
            {
                dp[i + 1] = dp[i] - 100.0f;
                parentIdx[i + 1] = i;
                parentId[i + 1] = _unkId;
            }
        }

        var ids = new List<long>();
        int curr = n;
        while (curr > 0)
        {
            ids.Add(parentId[curr]);
            curr = parentIdx[curr];
        }

        ids.Reverse();
        ids.Add(_eosId); // </s>
        return ids;
    }

    public string Decode(IEnumerable<long> tokenIds, int startTokenId, int padTokenId)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var id in tokenIds)
        {
            int intId = (int)id;
            if (intId == _eosId || intId == startTokenId || intId == padTokenId)
                continue;

            if (_idToPiece.TryGetValue(intId, out var piece))
            {
                sb.Append(piece);
            }
        }

        return sb.ToString().Replace("\u2581", " ").Trim();
    }
}

public static class OpusMtProbe
{
    public static OpusMtProbeResult Run(string enViDir, string viEnDir, string tatoebaJsonPath)
    {
        var result = new OpusMtProbeResult();

        if (!File.Exists(tatoebaJsonPath))
        {
            result.Pass = false;
            result.ErrorMessage = $"Tatoeba dataset not found at {tatoebaJsonPath}";
            return result;
        }

        try
        {
            Console.WriteLine("[OpusMtProbe] 1. Loading Tatoeba test sentence pairs...");
            var json = File.ReadAllText(tatoebaJsonPath);
            using var doc = JsonDocument.Parse(json);
            var pairs = new List<(string En, string Vi)>();

            foreach (var item in doc.RootElement.GetProperty("sentences").EnumerateArray())
            {
                string en = item.GetProperty("en").GetString() ?? "";
                string vi = item.GetProperty("vi").GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(en) && !string.IsNullOrWhiteSpace(vi))
                {
                    pairs.Add((en, vi));
                }
            }

            Console.WriteLine($"[OpusMtProbe] Loaded {pairs.Count} evaluation sentence pairs.");

            // Evaluate EN -> VI
            Console.WriteLine("[OpusMtProbe] 2. Evaluating EN -> VI...");
            result.EnToVi = EvaluateDirection(
                enViDir,
                pairs.Select(p => (Input: p.En, Reference: p.Vi)).ToList(),
                "en->vi");

            // Evaluate VI -> EN
            Console.WriteLine("[OpusMtProbe] 3. Evaluating VI -> EN...");
            result.ViToEn = EvaluateDirection(
                viEnDir,
                pairs.Select(p => (Input: p.Vi, Reference: p.En)).ToList(),
                "vi->en");

            result.Pass = result.EnToVi.Pass && result.ViToEn.Pass;
            Console.WriteLine($"[OpusMtProbe] Complete. Overall Pass={result.Pass}");
        }
        catch (Exception ex)
        {
            result.Pass = false;
            result.ErrorMessage = ex.ToString();
        }

        return result;
    }

    private static TranslationDirectionResult EvaluateDirection(
        string modelDir,
        List<(string Input, string Reference)> testSet,
        string directionLabel)
    {
        var dirResult = new TranslationDirectionResult { Direction = directionLabel, SentenceCount = testSet.Count };

        try
        {
            // Read config.json
            string configPath = Path.Combine(modelDir, "config.json");
            using var cfgDoc = JsonDocument.Parse(File.ReadAllText(configPath));
            int decoderStartTokenId = cfgDoc.RootElement.GetProperty("decoder_start_token_id").GetInt32();
            int padTokenId = cfgDoc.RootElement.GetProperty("pad_token_id").GetInt32();
            int eosTokenId = cfgDoc.RootElement.GetProperty("eos_token_id").GetInt32();

            string spmVocabPath = Path.Combine(modelDir, "spm_vocab.json");
            string vocabPath = Path.Combine(modelDir, "vocab.json");
            var tokenizer = new MarianSpmTokenizer(spmVocabPath, vocabPath, unkId: 1, eosId: eosTokenId);

            var sessionOptions = new SessionOptions();
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

            using var encSession = new InferenceSession(Path.Combine(modelDir, "encoder_model.onnx"), sessionOptions);
            using var decSession = new InferenceSession(Path.Combine(modelDir, "decoder_model.onnx"), sessionOptions);

            var hypotheses = new List<string>();
            var references = new List<string>();
            var latencies = new List<double>();

            int count = 0;
            foreach (var (input, reference) in testSet)
            {
                references.Add(reference);

                var sw = Stopwatch.StartNew();

                var inputIds = tokenizer.Encode(input);
                int seqLen = inputIds.Count;

                var inputIdsTensor = new DenseTensor<long>(inputIds.ToArray(), new[] { 1, seqLen });
                var attentionMaskTensor = new DenseTensor<long>(Enumerable.Repeat(1L, seqLen).ToArray(), new[] { 1, seqLen });

                var encInputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                    NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
                };

                using var encOutputs = encSession.Run(encInputs);
                var lastHiddenState = encOutputs[0].AsTensor<float>();

                var decIds = new List<long> { decoderStartTokenId };
                for (int step = 0; step < 50; step++)
                {
                    var decIdsTensor = new DenseTensor<long>(decIds.ToArray(), new[] { 1, decIds.Count });
                    var decInputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input_ids", decIdsTensor),
                        NamedOnnxValue.CreateFromTensor("encoder_hidden_states", lastHiddenState),
                        NamedOnnxValue.CreateFromTensor("encoder_attention_mask", attentionMaskTensor)
                    };

                    using var decOutputs = decSession.Run(decInputs);
                    var logits = decOutputs[0].AsTensor<float>();

                    // Last token logits
                    int lastTokenIdx = decIds.Count - 1;
                    int vocabSize = logits.Dimensions[2];

                    float maxLogit = float.NegativeInfinity;
                    long bestToken = 0;

                    for (int v = 0; v < vocabSize; v++)
                    {
                        float val = logits[0, lastTokenIdx, v];
                        if (val > maxLogit)
                        {
                            maxLogit = val;
                            bestToken = v;
                        }
                    }

                    decIds.Add(bestToken);
                    if (bestToken == eosTokenId)
                        break;
                }

                sw.Stop();
                latencies.Add(sw.Elapsed.TotalMilliseconds);

                string hyp = tokenizer.Decode(decIds, decoderStartTokenId, padTokenId);
                hypotheses.Add(hyp);

                count++;
                if (count % 25 == 0)
                {
                    Console.WriteLine($"  [{directionLabel}] Processed {count}/{testSet.Count} sentences...");
                }
            }

            latencies.Sort();
            dirResult.P50LatencyMs = latencies[(int)(latencies.Count * 0.50)];
            dirResult.P95LatencyMs = latencies[(int)(latencies.Count * 0.95)];
            dirResult.MaxLatencyMs = latencies[^1];

            dirResult.ChrFScore = ComputeCorpusChrF(hypotheses, references);
            dirResult.BleuScore = ComputeCorpusBleu(hypotheses, references);

            // Pass bar: chrF >= 45 (or BLEU >= 25) AND p50 <= 300 ms
            bool qualityPass = dirResult.ChrFScore >= 45.0 || dirResult.BleuScore >= 25.0;
            bool latencyPass = dirResult.P50LatencyMs <= 300.0;
            dirResult.Pass = qualityPass && latencyPass;

            Console.WriteLine($"  [{directionLabel}] chrF={dirResult.ChrFScore:F2}, BLEU={dirResult.BleuScore:F2}, p50={dirResult.P50LatencyMs:F1}ms, p95={dirResult.P95LatencyMs:F1}ms (Pass={dirResult.Pass})");
        }
        catch (Exception ex)
        {
            dirResult.Pass = false;
            dirResult.ErrorMessage = ex.ToString();
        }

        return dirResult;
    }

    public static double ComputeCorpusChrF(List<string> hypotheses, List<string> references, int maxN = 6, double beta = 2.0)
    {
        double totalPrec = 0.0;
        double totalRec = 0.0;

        for (int n = 1; n <= maxN; n++)
        {
            long nGramMatch = 0;
            long nGramHyp = 0;
            long nGramRef = 0;

            for (int i = 0; i < hypotheses.Count; i++)
            {
                var hypGrams = GetCharNGrams(hypotheses[i], n);
                var refGrams = GetCharNGrams(references[i], n);

                nGramHyp += hypGrams.Values.Sum();
                nGramRef += refGrams.Values.Sum();

                foreach (var (gram, count) in hypGrams)
                {
                    if (refGrams.TryGetValue(gram, out int refCount))
                    {
                        nGramMatch += Math.Min(count, refCount);
                    }
                }
            }

            double prec = nGramHyp > 0 ? (double)nGramMatch / nGramHyp : 0.0;
            double rec = nGramRef > 0 ? (double)nGramMatch / nGramRef : 0.0;

            totalPrec += prec;
            totalRec += rec;
        }

        double avgPrec = totalPrec / maxN;
        double avgRec = totalRec / maxN;

        double beta2 = beta * beta;
        if (beta2 * avgPrec + avgRec == 0)
            return 0.0;

        return (1.0 + beta2) * (avgPrec * avgRec) / (beta2 * avgPrec + avgRec) * 100.0;
    }

    private static Dictionary<string, int> GetCharNGrams(string s, int n)
    {
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        // Normalize spaces
        string norm = string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (norm.Length < n)
            return dict;

        for (int i = 0; i <= norm.Length - n; i++)
        {
            string gram = norm.Substring(i, n);
            dict[gram] = dict.GetValueOrDefault(gram, 0) + 1;
        }

        return dict;
    }

    public static double ComputeCorpusBleu(List<string> hypotheses, List<string> references, int maxN = 4)
    {
        double logSum = 0.0;
        long totalHypLen = 0;
        long totalRefLen = 0;

        for (int n = 1; n <= maxN; n++)
        {
            long nGramMatch = 0;
            long nGramHyp = 0;

            for (int i = 0; i < hypotheses.Count; i++)
            {
                var hypWords = hypotheses[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var refWords = references[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                if (n == 1)
                {
                    totalHypLen += hypWords.Length;
                    totalRefLen += refWords.Length;
                }

                var hypGrams = GetWordNGrams(hypWords, n);
                var refGrams = GetWordNGrams(refWords, n);

                nGramHyp += hypGrams.Values.Sum();

                foreach (var (gram, count) in hypGrams)
                {
                    if (refGrams.TryGetValue(gram, out int refCount))
                    {
                        nGramMatch += Math.Min(count, refCount);
                    }
                }
            }

            double p_n = nGramHyp > 0 ? (double)nGramMatch / nGramHyp : 1e-10;
            logSum += 0.25 * Math.Log(Math.Max(p_n, 1e-10));
        }

        double bp = 1.0;
        if (totalHypLen < totalRefLen)
        {
            bp = Math.Exp(1.0 - (double)totalRefLen / Math.Max(totalHypLen, 1));
        }

        return bp * Math.Exp(logSum) * 100.0;
    }

    private static Dictionary<string, int> GetWordNGrams(string[] words, int n)
    {
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (words.Length < n)
            return dict;

        for (int i = 0; i <= words.Length - n; i++)
        {
            string gram = string.Join(" ", words.Skip(i).Take(n));
            dict[gram] = dict.GetValueOrDefault(gram, 0) + 1;
        }

        return dict;
    }
}
