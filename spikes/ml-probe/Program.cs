using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MlProbe;

public class ModelSizeInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public double FileSizeMB => Math.Round((double)FileSizeBytes / (1024 * 1024), 2);
    public string Category { get; set; } = "";
    public string BundleDecision { get; set; } = "";
}

public class HardwareContext
{
    public string OsVersion { get; set; } = RuntimeInformation.OSDescription;
    public string Architecture { get; set; } = RuntimeInformation.ProcessArchitecture.ToString();
    public string CpuName { get; set; } = "Unknown CPU";
    public int LogicalCores { get; set; } = Environment.ProcessorCount;
    public List<string> Gpus { get; set; } = new();
}

public class MlProbeReport
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public bool AllPass { get; set; }
    public HardwareContext Hardware { get; set; } = new();
    public WhisperProbeResult Whisper { get; set; } = new();
    public OcrProbeResult Ocr { get; set; } = new();
    public YuNetProbeResult YuNet { get; set; } = new();
    public OpusMtProbeResult Translation { get; set; } = new();
    public List<ModelSizeInfo> ModelSizes { get; set; } = new();
    public double TotalMandatoryBundleMB { get; set; }
    public double TotalOptionalBundleMB { get; set; }
}

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("Lightshot Spike E: On-Device ML Quality and Cost Probe (Console Runner)");
        Console.WriteLine("================================================================================");

        bool assertMode = args.Any(a => a.Equals("--assert", StringComparison.OrdinalIgnoreCase));
        string repoRoot = FindRepoRoot();
        string artifactsDir = Path.Combine(repoRoot, "artifacts");
        string modelsDir = Path.Combine(artifactsDir, "models");
        string fixturesDir = Path.Combine(repoRoot, "spikes", "ml-probe", "Fixtures");
        string widerValImages = Path.Combine(artifactsDir, "datasets", "WIDER_val", "images", "0--Parade");
        string widerValGt = Path.Combine(artifactsDir, "datasets", "wider_face_split", "wider_face_val_bbx_gt.txt");
        string tatoebaJson = Path.Combine(artifactsDir, "tatoeba_100_en_vi.json");

        Console.WriteLine($"Repo root:     {repoRoot}");
        Console.WriteLine($"Artifacts dir: {artifactsDir}");
        Console.WriteLine($"Assert mode:   {assertMode}");
        Console.WriteLine();

        var report = new MlProbeReport();
        report.Hardware = DetectHardware();

        Console.WriteLine($"[Hardware] OS: {report.Hardware.OsVersion}");
        Console.WriteLine($"[Hardware] CPU: {report.Hardware.CpuName} ({report.Hardware.LogicalCores} logical cores)");
        foreach (var gpu in report.Hardware.Gpus)
        {
            Console.WriteLine($"[Hardware] GPU: {gpu}");
        }
        Console.WriteLine();

        // ---------------------------------------------------------------------
        // Part 1: Captions (Whisper.net ggml-base q5 on 5-min narration)
        // ---------------------------------------------------------------------
        Console.WriteLine("--- Part 1: Captions (Whisper.net ggml-base q5) ---");
        string whisperModel = Path.Combine(modelsDir, "ggml-base-q5_1.bin");
        report.Whisper = await WhisperProbe.RunAsync(whisperModel, artifactsDir);
        Console.WriteLine();

        // ---------------------------------------------------------------------
        // Part 2: OCR (Windows.Media.Ocr with 2x upscale + tiling)
        // ---------------------------------------------------------------------
        Console.WriteLine("--- Part 2: OCR (Windows.Media.Ocr on Auto-Redact Fixtures) ---");
        report.Ocr = await OcrProbe.RunAsync(fixturesDir);
        Console.WriteLine();

        // ---------------------------------------------------------------------
        // Part 3: YuNet Face Detection (OpenCV Zoo ONNX on WIDER FACE)
        // ---------------------------------------------------------------------
        Console.WriteLine("--- Part 3: Face Detection (YuNet ONNX on WIDER FACE) ---");
        string yunetModel = Path.Combine(modelsDir, "face_detection_yunet_2023mar.onnx");
        report.YuNet = YuNetProbe.Run(yunetModel, widerValImages, widerValGt);
        Console.WriteLine();

        // ---------------------------------------------------------------------
        // Part 4: Translation (OPUS-MT ONNX en<->vi on Tatoeba)
        // ---------------------------------------------------------------------
        Console.WriteLine("--- Part 4: Translation (OPUS-MT ONNX en<->vi) ---");
        string enViDir = Path.Combine(modelsDir, "opus-mt-en-vi");
        string viEnDir = Path.Combine(modelsDir, "opus-mt-vi-en");
        report.Translation = OpusMtProbe.Run(enViDir, viEnDir, tatoebaJson);
        Console.WriteLine();

        // ---------------------------------------------------------------------
        // Model & Runtime Footprint Measurements
        // ---------------------------------------------------------------------
        Console.WriteLine("--- Model and Runtime Bundle Footprint ---");
        report.ModelSizes = MeasureModelSizes(repoRoot, artifactsDir);
        foreach (var m in report.ModelSizes)
        {
            Console.WriteLine($"  {m.Name,-35} : {m.FileSizeMB,7:F2} MB [{m.BundleDecision}]");
        }

        report.TotalMandatoryBundleMB = Math.Round(report.ModelSizes
            .Where(m => m.BundleDecision.Contains("Mandatory") || m.BundleDecision.Contains("Bundled"))
            .Sum(m => m.FileSizeMB), 2);

        report.TotalOptionalBundleMB = Math.Round(report.ModelSizes
            .Where(m => m.BundleDecision.Contains("Optional"))
            .Sum(m => m.FileSizeMB), 2);

        Console.WriteLine($"  Total Mandatory Setup Footprint: {report.TotalMandatoryBundleMB:F2} MB");
        Console.WriteLine($"  Total Optional On-Demand Models:  {report.TotalOptionalBundleMB:F2} MB");
        Console.WriteLine();

        // ---------------------------------------------------------------------
        // Acceptance Evaluation
        // ---------------------------------------------------------------------
        report.AllPass = report.Whisper.Pass && report.Ocr.Pass && report.YuNet.Pass && report.Translation.Pass;

        string resultJsonPath = Path.Combine(artifactsDir, "ml-probe-result.json");
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        string jsonOutput = JsonSerializer.Serialize(report, jsonOptions);
        File.WriteAllText(resultJsonPath, jsonOutput);
        Console.WriteLine($"Saved full results to {resultJsonPath}");
        Console.WriteLine();

        Console.WriteLine("================================================================================");
        Console.WriteLine("SPIKE E SUMMARY VERDICT");
        Console.WriteLine("================================================================================");
        Console.WriteLine($"Whisper.net Captions:   {(report.Whisper.Pass ? "PASS" : "FAIL")} (RTF={report.Whisper.RealTimeFactor:F3}, Median Error={report.Whisper.MedianErrorMs:F1}ms)");
        Console.WriteLine($"Windows.Media.Ocr:     {(report.Ocr.Pass ? "PASS" : "FAIL")} (Recall={report.Ocr.OverallRecall:P2}, Total={report.Ocr.TotalEntities})");
        Console.WriteLine($"YuNet Face Detection:   {(report.YuNet.Pass ? "PASS" : "FAIL")} (Recall={report.YuNet.Recall:P2}, GT={report.YuNet.TotalGtFaces})");
        Console.WriteLine($"OPUS-MT Translation:    {(report.Translation.Pass ? "PASS" : "FAIL")} (en->vi chrF={report.Translation.EnToVi.ChrFScore:F1}, p50={report.Translation.EnToVi.P50LatencyMs:F1}ms; vi->en chrF={report.Translation.ViToEn.ChrFScore:F1}, p50={report.Translation.ViToEn.P50LatencyMs:F1}ms)");
        Console.WriteLine($"OVERALL VERDICT:        {(report.AllPass ? "PASS" : "FAIL")}");
        Console.WriteLine("================================================================================");

        if (assertMode && !report.AllPass)
        {
            Console.Error.WriteLine("[Spike E] Assertion failed: One or more ML probe criteria did not pass.");
            return 1;
        }

        return 0;
    }

    private static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Lightshot.slnx")) || Directory.Exists(Path.Combine(dir, ".git")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir)!;
        }
        return Directory.GetCurrentDirectory();
    }

    private static HardwareContext DetectHardware()
    {
        var hw = new HardwareContext();
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                hw.CpuName = cpuKey?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? hw.CpuName;

                using var videoKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (videoKey != null)
                {
                    foreach (var subName in videoKey.GetSubKeyNames())
                    {
                        if (subName.StartsWith("000"))
                        {
                            using var sub = videoKey.OpenSubKey(subName);
                            string? desc = sub?.GetValue("DriverDesc")?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(desc) && !hw.Gpus.Contains(desc))
                            {
                                hw.Gpus.Add(desc);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            hw.CpuName = $"Hardware detection error: {ex.Message}";
        }

        return hw;
    }

    private static List<ModelSizeInfo> MeasureModelSizes(string repoRoot, string artifactsDir)
    {
        var list = new List<ModelSizeInfo>();
        string modelsDir = Path.Combine(artifactsDir, "models");

        void AddFile(string name, string relPath, string category, string decision)
        {
            string fullPath = Path.Combine(modelsDir, relPath);
            if (File.Exists(fullPath))
            {
                list.Add(new ModelSizeInfo
                {
                    Name = name,
                    Path = fullPath,
                    FileSizeBytes = new FileInfo(fullPath).Length,
                    Category = category,
                    BundleDecision = decision
                });
            }
        }

        // Whisper
        AddFile("Whisper ggml-base-q5_1 (Base)", "ggml-base-q5_1.bin", "Captions", "Optional Download (or Default if <= 60MB)");
        
        // YuNet
        AddFile("YuNet Face Detection ONNX", "face_detection_yunet_2023mar.onnx", "Auto-Redact Faces", "Mandatory Bundled (0.22 MB)");

        // OPUS-MT en-vi
        AddFile("OPUS-MT en-vi Encoder ONNX", "opus-mt-en-vi/encoder_model.onnx", "Translation", "Optional Download (178 MB)");
        AddFile("OPUS-MT en-vi Decoder ONNX", "opus-mt-en-vi/decoder_model.onnx", "Translation", "Optional Download (307 MB)");
        AddFile("OPUS-MT en-vi SentencePiece", "opus-mt-en-vi/source.spm", "Translation", "Optional Download (0.8 MB)");

        // OPUS-MT vi-en
        AddFile("OPUS-MT vi-en Encoder ONNX", "opus-mt-vi-en/encoder_model.onnx", "Translation", "Optional Download (178 MB)");
        AddFile("OPUS-MT vi-en Decoder ONNX", "opus-mt-vi-en/decoder_model.onnx", "Translation", "Optional Download (308 MB)");
        AddFile("OPUS-MT vi-en SentencePiece", "opus-mt-vi-en/source.spm", "Translation", "Optional Download (0.8 MB)");

        // Windows.Media.Ocr
        list.Add(new ModelSizeInfo
        {
            Name = "Windows.Media.Ocr (OS Built-in)",
            Path = "C:\\Windows\\System32\\OCR",
            FileSizeBytes = 0,
            Category = "OCR",
            BundleDecision = "Mandatory Zero-Cost (OS Built-in)"
        });

        // Native runtimes (locate from probe bin dir)
        string binDir = AppContext.BaseDirectory;
        string onnxDll = Path.Combine(binDir, "onnxruntime.dll");
        if (File.Exists(onnxDll))
        {
            list.Add(new ModelSizeInfo
            {
                Name = "Microsoft.ML.OnnxRuntime CPU DLL",
                Path = onnxDll,
                FileSizeBytes = new FileInfo(onnxDll).Length,
                Category = "ML Runtime",
                BundleDecision = "Mandatory Bundled"
            });
        }

        string whisperDll = Path.Combine(binDir, "whisper.dll");
        if (File.Exists(whisperDll))
        {
            list.Add(new ModelSizeInfo
            {
                Name = "Whisper.net Native CPU DLL",
                Path = whisperDll,
                FileSizeBytes = new FileInfo(whisperDll).Length,
                Category = "ML Runtime",
                BundleDecision = "Mandatory Bundled"
            });
        }

        return list;
    }
}
