using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using SkiaSharp;

namespace MlProbe;

public record SeededEntity(string Category, string Text, string FontName, float FontSize, bool DarkBackground);

public class OcrProbeResult
{
    public bool Pass { get; set; }
    public int TotalEntities { get; set; }
    public int MatchedEntities { get; set; }
    public double OverallRecall { get; set; }
    public int FirstPassMatchedEntities { get; set; }
    public double FirstPassRecall { get; set; }
    public int LenientMatchedEntities { get; set; }
    public double LenientRecall { get; set; }
    public string MatchRule { get; set; } = "strict: case-sensitive, whole OCR line equals the entity after whitespace normalisation, per entity";
    public string RenderRule { get; set; } = "fixtures rendered at native font size (1x, grayscale AA), upscaled 2x with bicubic (Mitchell) resampling, 5 entities tiled per bitmap; 3x retry upscales the native-size bitmap and binarises";
    public int RetryRecovered { get; set; }
    public int MinFontSizePx { get; set; }
    public int MaxFontSizePx { get; set; }
    public Dictionary<string, double> RecallByFont { get; set; } = new();
    public Dictionary<string, double> RecallByCategory { get; set; } = new();
    public string? FallbackTriggered { get; set; }
    public string? ErrorMessage { get; set; }
}

public static class OcrProbe
{
    public static async Task<OcrProbeResult> RunAsync(string fixturesDir)
    {
        var result = new OcrProbeResult();

        try
        {
            var ocr = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
            if (ocr == null)
            {
                result.Pass = false;
                result.ErrorMessage = "Windows.Media.Ocr failed to initialize en-US engine.";
                result.FallbackTriggered = "OCR engine missing";
                return result;
            }

            // Load fonts
            string interPath = Path.Combine(fixturesDir, "Inter-Regular.ttf");
            var tfSegoe = SKTypeface.FromFamilyName("Segoe UI");
            var tfConsolas = SKTypeface.FromFamilyName("Consolas");
            var tfInter = File.Exists(interPath) ? SKTypeface.FromFile(interPath) : null;

            var fonts = new Dictionary<string, SKTypeface>
            {
                ["Segoe UI"] = tfSegoe,
                ["Consolas"] = tfConsolas,
            };
            if (tfInter != null) fonts["Inter"] = tfInter;

            Console.WriteLine($"[OcrProbe] 1. Generating seeded Auto Redact entities across {fonts.Count} fonts...");
            var seeds = GenerateSeededEntities(fonts.Keys);
            result.TotalEntities = seeds.Count;
            Console.WriteLine($"[OcrProbe] Generated {seeds.Count} seeded entities (>=200 required).");

            int matchedCount = 0;
            int firstPassCount = 0;
            int lenientCount = 0;
            var fontStats = new Dictionary<string, (int total, int matched)>();
            var catStats = new Dictionary<string, (int total, int matched)>();

            foreach (var f in fonts.Keys) fontStats[f] = (0, 0);

            // Group by background mode (light / dark) and batch in 5 entities for tiling
            var groupedBatches = seeds
                .GroupBy(s => s.DarkBackground)
                .SelectMany(g => g.Chunk(5))
                .ToList();

            foreach (var batchArr in groupedBatches)
            {
                var batch = batchArr.ToList();
                var ocrLines = await RenderAndRecognizeBatchAsync(ocr, batch, fonts, upscale: 2);
                var batchText = string.Join(" ", ocrLines);

                foreach (var entity in batch)
                {
                    bool matched = IsEntityMatchedStrict(ocrLines, entity.Text);
                    bool lenient = matched || IsEntityMatchedLenient(batchText, entity.Text);
                    if (matched) firstPassCount++;

                    // Fallback retry at 3x scale (native bitmap upscaled) with binarization if missed
                    if (!matched)
                    {
                        var retryLines = await RenderAndRecognizeSingleAsync(ocr, entity, fonts, upscale: 3, binarize: true);
                        if (IsEntityMatchedStrict(retryLines, entity.Text))
                        {
                            matched = true;
                            lenient = true;
                            result.RetryRecovered++;
                            result.FallbackTriggered ??= "Retry at 3x upscale with binarization triggered for difficult glyphs";
                        }
                        else if (!lenient)
                        {
                            lenient = IsEntityMatchedLenient(string.Join(" ", retryLines), entity.Text);
                        }
                    }

                    (int total, int matched) fs = fontStats.GetValueOrDefault(entity.FontName, (0, 0));
                    fontStats[entity.FontName] = (fs.total + 1, fs.matched + (matched ? 1 : 0));

                    (int total, int matched) cs = catStats.GetValueOrDefault(entity.Category, (0, 0));
                    catStats[entity.Category] = (cs.total + 1, cs.matched + (matched ? 1 : 0));

                    if (matched) matchedCount++;
                    if (lenient) lenientCount++;
                }
            }

            result.FirstPassMatchedEntities = firstPassCount;
            result.FirstPassRecall = (double)firstPassCount / seeds.Count;
            result.LenientMatchedEntities = lenientCount;
            result.LenientRecall = (double)lenientCount / seeds.Count;
            result.MinFontSizePx = (int)seeds.Min(e => e.FontSize);
            result.MaxFontSizePx = (int)seeds.Max(e => e.FontSize);
            result.MatchedEntities = matchedCount;
            result.OverallRecall = (double)matchedCount / seeds.Count;

            foreach (var (k, v) in fontStats)
            {
                result.RecallByFont[k] = v.total > 0 ? (double)v.matched / v.total : 0.0;
            }
            foreach (var (k, v) in catStats)
            {
                result.RecallByCategory[k] = v.total > 0 ? (double)v.matched / v.total : 0.0;
            }

            // Pass bar: recall >= 90%
            result.Pass = result.OverallRecall >= 0.90 && result.TotalEntities >= 200;

            Console.WriteLine($"[OcrProbe] Strict recall (with 3x retry): {result.OverallRecall:P1} ({result.MatchedEntities}/{result.TotalEntities}); first pass only {result.FirstPassRecall:P1}; lenient (informational) {result.LenientRecall:P1}");
            foreach (var (k, v) in result.RecallByFont)
            {
                Console.WriteLine($"  - Font {k}: {v:P1}");
            }
            foreach (var (k, v) in result.RecallByCategory)
            {
                Console.WriteLine($"  - Category {k}: {v:P1}");
            }
        }
        catch (Exception ex)
        {
            result.Pass = false;
            result.ErrorMessage = ex.ToString();
        }

        return result;
    }

    private static SKPaint MakePaint(SeededEntity entity, Dictionary<string, SKTypeface> fonts) => new SKPaint
    {
        Color = entity.DarkBackground ? SKColors.White : SKColors.Black,
        TextSize = entity.FontSize, // native size: 1x, never scaled; scaling happens on the bitmap
        IsAntialias = true,
        Typeface = fonts.GetValueOrDefault(entity.FontName, SKTypeface.Default)
    };

    // Resamples a rendered bitmap; bicubic (Mitchell) as a stand-in for the filter a product would use.
    private static SKBitmap Upscale(SKBitmap src, int factor)
    {
        var dst = new SKBitmap(src.Width * factor, src.Height * factor, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var img = SKImage.FromBitmap(src);
        using var canvas = new SKCanvas(dst);
        using var paint = new SKPaint();
        canvas.DrawImage(img, new SKRect(0, 0, dst.Width, dst.Height), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        return dst;
    }

    private static async Task<List<string>> RenderAndRecognizeBatchAsync(OcrEngine ocr, List<SeededEntity> batch, Dictionary<string, SKTypeface> fonts, int upscale)
    {
        // Native-size canvas: 1x lines sized for 11-24 px text.
        const int width = 500;
        const int lineHeight = 40;
        int height = batch.Count * lineHeight + 20;

        using var native = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(native))
        {
            bool isDark = batch[0].DarkBackground;
            canvas.Clear(isDark ? new SKColor(20, 20, 20) : SKColors.White);

            float y = 30;
            foreach (var entity in batch)
            {
                using var paint = MakePaint(entity, fonts);
                canvas.DrawText(entity.Text, 20, y, paint);
                y += lineHeight;
            }
        }

        using var scaled = Upscale(native, upscale);
        return await RecognizeSkBitmapAsync(ocr, scaled, invertIfDark: batch[0].DarkBackground);
    }

    private static async Task<List<string>> RenderAndRecognizeSingleAsync(OcrEngine ocr, SeededEntity entity, Dictionary<string, SKTypeface> fonts, int upscale, bool binarize)
    {
        using var native = new SKBitmap(500, 50, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(native))
        {
            canvas.Clear(entity.DarkBackground ? new SKColor(20, 20, 20) : SKColors.White);
            using var paint = MakePaint(entity, fonts);
            canvas.DrawText(entity.Text, 20, 32, paint);
        }

        using var bmp = Upscale(native, upscale);

        if (binarize)
        {
            // Binarize directly to dark text on pure white background
            bool isDark = entity.DarkBackground;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    var col = bmp.GetPixel(x, y);
                    int gray = (col.Red * 299 + col.Green * 587 + col.Blue * 114) / 1000;
                    if (isDark)
                    {
                        bmp.SetPixel(x, y, gray > 100 ? SKColors.Black : SKColors.White);
                    }
                    else
                    {
                        bmp.SetPixel(x, y, gray < 180 ? SKColors.Black : SKColors.White);
                    }
                }
            }
            return await RecognizeSkBitmapAsync(ocr, bmp, invertIfDark: false);
        }

        return await RecognizeSkBitmapAsync(ocr, bmp, invertIfDark: entity.DarkBackground);
    }

    private static async Task<List<string>> RecognizeSkBitmapAsync(OcrEngine ocr, SKBitmap bmp, bool invertIfDark)
    {
        using var target = new SKBitmap(bmp.Info);
        bmp.CopyTo(target);

        if (invertIfDark)
        {
            // Invert dark background to light for Windows.Media.Ocr
            for (int y = 0; y < target.Height; y++)
            {
                for (int x = 0; x < target.Width; x++)
                {
                    var c = target.GetPixel(x, y);
                    target.SetPixel(x, y, new SKColor((byte)(255 - c.Red), (byte)(255 - c.Green), (byte)(255 - c.Blue), c.Alpha));
                }
            }
        }

        using var ms = new MemoryStream();
        target.Encode(ms, SKEncodedImageFormat.Png, 100);
        ms.Position = 0;

        var ras = ms.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(ras);
        var sbm = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var result = await ocr.RecognizeAsync(sbm);
        return result.Lines.Select(l => l.Text ?? "").ToList();
    }

    private static string NormalizeWhitespace(string s)
    {
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    // Spec rule: exact (case-sensitive, ordinal) string match after whitespace normalisation, per entity.
    private static bool IsEntityMatchedStrict(List<string> ocrLines, string entityText)
    {
        string normEntity = NormalizeWhitespace(entityText);
        return ocrLines.Any(l => string.Equals(NormalizeWhitespace(l), normEntity, StringComparison.Ordinal));
    }

    // Informational only, never used for PASS/FAIL: case-insensitive, punctuation-insensitive, over the whole batch text.
    private static bool IsEntityMatchedLenient(string ocrText, string entityText)
    {
        string normOcr = NormalizeWhitespace(ocrText);
        string normEntity = NormalizeWhitespace(entityText);

        if (normOcr.Contains(normEntity, StringComparison.OrdinalIgnoreCase))
            return true;

        string alnumOcr = Regex.Replace(normOcr, @"[^a-zA-Z0-9]", "");
        string alnumEntity = Regex.Replace(normEntity, @"[^a-zA-Z0-9]", "");
        return alnumOcr.Contains(alnumEntity, StringComparison.OrdinalIgnoreCase);
    }

    private static List<SeededEntity> GenerateSeededEntities(IEnumerable<string> fontNames)
    {
        var list = new List<SeededEntity>();
        var fonts = fontNames.ToList();
        float[] fontSizes = { 11f, 12f, 14f, 16f, 18f, 20f, 24f };

        int id = 100;
        int fontIdx = 0;
        int sizeIdx = 0;

        // Auto Redact categories: secrets, cards, IBANs, emails, SSN, IP, labelled values
        string[] categories = { "secrets", "cards", "IBANs", "emails", "SSN", "IP", "labelled values" };

        foreach (var cat in categories)
        {
            for (int i = 0; i < 32; i++)
            {
                string text = cat switch
                {
                    "secrets" => i % 2 == 0 ? $"sk_live_{id}aBcDeFgHiJkLmNoP" : $"ghp_TOKEN{id}XyZ123456789Secret",
                    "cards" => $"4532-{1000 + i:D4}-5678-{id:D4}",
                    "IBANs" => $"GB{20 + i % 70:D2}NWBK601613{id:D6}",
                    "emails" => $"user.sec{id}@subdomain{i}.corp.net",
                    "SSN" => $"{123 + i % 800:D3}-{45 + i % 50:D2}-{6780 + i:D4}",
                    "IP" => $"192.168.{i % 254 + 1}.{10 + i % 200}",
                    "labelled values" => $"API_KEY: secret_{id}_token_{i}",
                    _ => $"Value_{id}"
                };

                string font = fonts[fontIdx % fonts.Count];
                float size = fontSizes[sizeIdx % fontSizes.Length];
                bool dark = i % 2 == 1;

                list.Add(new SeededEntity(cat, text, font, size, dark));

                id++;
                fontIdx++;
                sizeIdx++;
            }
        }

        return list;
    }
}
