// Ported from LightshotKit/Tests/LightshotKitTests/DocumentRenderTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Lightshot.Core;
using SkiaSharp;
using Xunit;

namespace Lightshot.Rendering.Tests;

/// <summary>
/// Helper fixtures, pixel color probing, and golden comparison/update utilities.
/// </summary>
public static class PixelAssert
{
    public static CapturedImage SolidImage(int width = 100, int height = 100, (double r, double g, double b)? rgb = null)
    {
        var (r, g, b) = rgb ?? (1.0, 1.0, 1.0);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            var color = new SKColor(
                (byte)Math.Round(r * 255.0),
                (byte)Math.Round(g * 255.0),
                (byte)Math.Round(b * 255.0),
                255);
            canvas.Clear(color);
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new CapturedImage(width, height, data.ToArray());
    }

    public static CapturedImage HalvesImage(
        int width,
        int height,
        (double r, double g, double b) left,
        (double r, double g, double b) right)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            var leftColor = new SKColor(
                (byte)Math.Round(left.r * 255.0),
                (byte)Math.Round(left.g * 255.0),
                (byte)Math.Round(left.b * 255.0),
                255);
            var rightColor = new SKColor(
                (byte)Math.Round(right.r * 255.0),
                (byte)Math.Round(right.g * 255.0),
                (byte)Math.Round(right.b * 255.0),
                255);

            using var leftPaint = new SKPaint { Color = leftColor, Style = SKPaintStyle.Fill };
            canvas.DrawRect(new SKRect(0, 0, width / 2f, height), leftPaint);

            using var rightPaint = new SKPaint { Color = rightColor, Style = SKPaintStyle.Fill };
            canvas.DrawRect(new SKRect(width / 2f, 0, width, height), rightPaint);
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return new CapturedImage(width, height, data.ToArray());
    }

    public static bool IsRed((double r, double g, double b) c) => c.r > 0.6 && c.g < 0.4 && c.b < 0.4;
    public static bool IsWhite((double r, double g, double b) c) => c.r > 0.9 && c.g > 0.9 && c.b > 0.9;
    public static bool IsGreen((double r, double g, double b) c) => c.g > 0.6 && c.r < 0.4 && c.b < 0.4;
    public static bool IsBlack((double r, double g, double b) c) => c.r < 0.1 && c.g < 0.1 && c.b < 0.1;
    public static bool IsBlue((double r, double g, double b) c) => c.b > 0.6 && c.r < 0.4 && c.g < 0.4;

    public static double ChannelDistance((double r, double g, double b) a, (double r, double g, double b) b) =>
        Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b);

    public static void AssertOrUpdateGolden(string testName, RenderedImage rendered, bool exact = false)
    {
        string goldensDir = FindGoldensDirectory();
        if (!Directory.Exists(goldensDir))
        {
            Directory.CreateDirectory(goldensDir);
        }

        string imagePath = Path.Combine(goldensDir, $"{testName}.png");
        string hashesPath = Path.Combine(goldensDir, "hashes.json");

        string hash = ComputeSha256Hex(rendered.Data);

        bool updateGoldens = Environment.GetEnvironmentVariable("LIGHTSHOT_UPDATE_GOLDENS") == "1";

        if (updateGoldens)
        {
            File.WriteAllBytes(imagePath, rendered.Data);

            Dictionary<string, string> hashes = new();
            if (File.Exists(hashesPath))
            {
                try
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(hashesPath));
                    if (existing != null) hashes = existing;
                }
                catch { }
            }

            hashes[testName] = hash;
            File.WriteAllText(hashesPath, JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        // Normal run: missing golden PNG or missing hashes.json must fail
        if (!File.Exists(imagePath))
        {
            Assert.Fail($"Golden image is missing: '{imagePath}'. Run scripts/test.ps1 -UpdateGoldens to create it.");
            return;
        }

        if (!File.Exists(hashesPath))
        {
            Assert.Fail($"Golden hashes file is missing: '{hashesPath}'. Run scripts/test.ps1 -UpdateGoldens to create it.");
            return;
        }

        var storedHashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(hashesPath));
        if (storedHashes == null || !storedHashes.TryGetValue(testName, out var expectedHash))
        {
            Assert.Fail($"Golden hash entry for '{testName}' is missing in '{hashesPath}'. Run scripts/test.ps1 -UpdateGoldens to create it.");
            return;
        }

        if (hash == expectedHash) return; // Exact hash match

        if (exact)
        {
            Assert.Fail($"Golden '{testName}' hash mismatch in exact mode: expected {expectedHash}, got {hash}.");
            return;
        }

        // 2 LSB tolerance check on pixel bytes (for Skia-drawn vector shapes only)
        byte[] expectedBytes = File.ReadAllBytes(imagePath);
        using var actualMs = new MemoryStream(rendered.Data);
        using var expectedMs = new MemoryStream(expectedBytes);
        using var actualBmp = SKBitmap.Decode(actualMs);
        using var expectedBmp = SKBitmap.Decode(expectedMs);

        Assert.NotNull(actualBmp);
        Assert.NotNull(expectedBmp);
        Assert.Equal(expectedBmp.Width, actualBmp.Width);
        Assert.Equal(expectedBmp.Height, actualBmp.Height);

        var actualPixels = actualBmp.GetPixelSpan();
        var expectedPixels = expectedBmp.GetPixelSpan();
        Assert.Equal(expectedPixels.Length, actualPixels.Length);

        for (int i = 0; i < actualPixels.Length; i++)
        {
            int diff = Math.Abs(actualPixels[i] - expectedPixels[i]);
            Assert.True(diff <= 2, $"Pixel channel difference at byte {i} was {diff}, exceeding 2 LSB tolerance for golden '{testName}'");
        }
    }

    public static string ComputeSha256Hex(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    private static string FindGoldensDirectory()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "tests", "Lightshot.Rendering.Tests", "Goldens");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            if (File.Exists(Path.Combine(current, "Lightshot.slnx")))
            {
                return candidate;
            }
            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        return Path.Combine(AppContext.BaseDirectory, "Goldens");
    }
}

/// <summary>
/// Probes pixel colors in a RenderedImage.
/// </summary>
public sealed class Pixels
{
    public int Width { get; }
    public int Height { get; }
    private readonly byte[] _buffer;

    public Pixels(RenderedImage rendered)
    {
        using var ms = new MemoryStream(rendered.Data);
        using var bitmap = SKBitmap.Decode(ms);
        if (bitmap == null)
        {
            Width = rendered.PixelWidth;
            Height = rendered.PixelHeight;
            _buffer = new byte[Width * Height * 4];
            return;
        }

        Width = bitmap.Width;
        Height = bitmap.Height;

        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var converted = new SKBitmap(info);
        using (var canvas = new SKCanvas(converted))
        {
            canvas.DrawBitmap(bitmap, 0, 0);
        }

        _buffer = new byte[Width * Height * 4];
        converted.GetPixelSpan().CopyTo(_buffer);
    }

    public (double r, double g, double b) Rgb(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return (0, 0, 0);
        }

        int index = (y * Width + x) * 4;
        return (
            _buffer[index] / 255.0,
            _buffer[index + 1] / 255.0,
            _buffer[index + 2] / 255.0
        );
    }

    public (byte r, byte g, byte b, byte a) RgbaByte(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return (0, 0, 0, 0);
        }

        int index = (y * Width + x) * 4;
        return (_buffer[index], _buffer[index + 1], _buffer[index + 2], _buffer[index + 3]);
    }
}
