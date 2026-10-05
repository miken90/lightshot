// Ported for Lightshot Windows Port (Phase 3 Core domain)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Linq;

namespace Lightshot.Core;

public readonly record struct Rgb24(byte R, byte G, byte B) : IComparable<Rgb24>
{
    public int CompareTo(Rgb24 other)
    {
        int r = R.CompareTo(other.R);
        if (r != 0) return r;
        int g = G.CompareTo(other.G);
        if (g != 0) return g;
        return B.CompareTo(other.B);
    }
}

public sealed record QuantizedGifFrame(
    IReadOnlyList<Rgb24> Palette,
    byte[] IndexedPixels,
    int Width,
    int Height)
{
    public byte[] PaletteBytes
    {
        get
        {
            var bytes = new byte[Palette.Count * 3];
            for (int i = 0; i < Palette.Count; i++)
            {
                bytes[i * 3] = Palette[i].R;
                bytes[i * 3 + 1] = Palette[i].G;
                bytes[i * 3 + 2] = Palette[i].B;
            }
            return bytes;
        }
    }
}

/// <summary>
/// Deterministic median-cut color quantizer producing at most 256 colors for GIF frames.
/// </summary>
public static class GifQuantizer
{
    private sealed class ColorBox
    {
        public List<Rgb24> Colors { get; }
        public byte MinR, MaxR;
        public byte MinG, MaxG;
        public byte MinB, MaxB;

        public ColorBox(List<Rgb24> colors)
        {
            Colors = colors;
            ComputeBounds();
        }

        public void ComputeBounds()
        {
            MinR = MinG = MinB = 255;
            MaxR = MaxG = MaxB = 0;
            foreach (var c in Colors)
            {
                if (c.R < MinR) MinR = c.R;
                if (c.R > MaxR) MaxR = c.R;
                if (c.G < MinG) MinG = c.G;
                if (c.G > MaxG) MaxG = c.G;
                if (c.B < MinB) MinB = c.B;
                if (c.B > MaxB) MaxB = c.B;
            }
        }

        public int RangeR => MaxR - MinR;
        public int RangeG => MaxG - MinG;
        public int RangeB => MaxB - MinB;
        public int MaxRange => Math.Max(RangeR, Math.Max(RangeG, RangeB));

        public Rgb24 AverageColor()
        {
            if (Colors.Count == 0) return new Rgb24(0, 0, 0);
            long rSum = 0, gSum = 0, bSum = 0;
            foreach (var c in Colors)
            {
                rSum += c.R;
                gSum += c.G;
                bSum += c.B;
            }
            return new Rgb24((byte)(rSum / Colors.Count), (byte)(gSum / Colors.Count), (byte)(bSum / Colors.Count));
        }
    }

    public static QuantizedGifFrame Quantize(ReadOnlySpan<byte> bgraOrRgbaPixels, int width, int height, bool isBgra = false, int maxColors = 256)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        maxColors = Math.Clamp(maxColors, 2, 256);

        int pixelCount = width * height;
        var distinctColors = new HashSet<Rgb24>();
        var framePixels = new Rgb24[pixelCount];

        for (int i = 0; i < pixelCount; i++)
        {
            int offset = i * 4;
            byte r = isBgra ? bgraOrRgbaPixels[offset + 2] : bgraOrRgbaPixels[offset];
            byte g = bgraOrRgbaPixels[offset + 1];
            byte b = isBgra ? bgraOrRgbaPixels[offset] : bgraOrRgbaPixels[offset + 2];
            var color = new Rgb24(r, g, b);
            framePixels[i] = color;
            distinctColors.Add(color);
        }

        List<Rgb24> palette;
        if (distinctColors.Count <= maxColors)
        {
            palette = distinctColors.OrderBy(c => c).ToList();
        }
        else
        {
            var boxes = new List<ColorBox> { new(distinctColors.OrderBy(c => c).ToList()) };

            while (boxes.Count < maxColors)
            {
                // Find box with largest range (deterministic tie-breaking)
                int bestIndex = -1;
                int maxRange = -1;
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (boxes[i].Colors.Count > 1 && boxes[i].MaxRange > maxRange)
                    {
                        maxRange = boxes[i].MaxRange;
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0 || maxRange <= 0) break;

                var boxToSplit = boxes[bestIndex];
                boxes.RemoveAt(bestIndex);

                // Sort along primary axis with largest range
                int rRange = boxToSplit.RangeR;
                int gRange = boxToSplit.RangeG;
                int bRange = boxToSplit.RangeB;

                if (rRange >= gRange && rRange >= bRange)
                {
                    boxToSplit.Colors.Sort((a, b) => a.R != b.R ? a.R.CompareTo(b.R) : a.CompareTo(b));
                }
                else if (gRange >= rRange && gRange >= bRange)
                {
                    boxToSplit.Colors.Sort((a, b) => a.G != b.G ? a.G.CompareTo(b.G) : a.CompareTo(b));
                }
                else
                {
                    boxToSplit.Colors.Sort((a, b) => a.B != b.B ? a.B.CompareTo(b.B) : a.CompareTo(b));
                }

                int median = boxToSplit.Colors.Count / 2;
                var left = new ColorBox(boxToSplit.Colors.GetRange(0, median));
                var right = new ColorBox(boxToSplit.Colors.GetRange(median, boxToSplit.Colors.Count - median));

                boxes.Add(left);
                boxes.Add(right);
            }

            palette = boxes.Select(b => b.AverageColor()).Distinct().OrderBy(c => c).ToList();
        }

        // Map pixels to nearest palette index
        var indexed = new byte[pixelCount];
        var colorCache = new Dictionary<Rgb24, byte>();

        for (int i = 0; i < pixelCount; i++)
        {
            var p = framePixels[i];
            if (!colorCache.TryGetValue(p, out byte idx))
            {
                idx = FindNearestColorIndex(p, palette);
                colorCache[p] = idx;
            }
            indexed[i] = idx;
        }

        return new QuantizedGifFrame(palette, indexed, width, height);
    }

    private static byte FindNearestColorIndex(Rgb24 color, IReadOnlyList<Rgb24> palette)
    {
        int bestDist = int.MaxValue;
        byte bestIndex = 0;
        for (int i = 0; i < palette.Count; i++)
        {
            var c = palette[i];
            int dr = color.R - c.R;
            int dg = color.G - c.G;
            int db = color.B - c.B;
            int dist = dr * dr + dg * dg + db * db;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIndex = (byte)i;
                if (dist == 0) break;
            }
        }
        return bestIndex;
    }
}
