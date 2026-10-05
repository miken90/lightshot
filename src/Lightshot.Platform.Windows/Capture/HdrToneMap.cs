// MIT License, Copyright (c) 2026 Viet Le

using System;

namespace Lightshot.Platform.Windows.Capture;

/// <summary>
/// Tone mapping functions for converting HDR scRGB / linear color spaces to standard 8-bit sRGB.
/// Calibrated per Spike A measurements so that 203-nit reference white maps to ~248 sRGB.
/// </summary>
public static class HdrToneMap
{
    /// <summary>
    /// Evaluates the tone map formula on a 203-nit reference SDR white in scRGB (203 / 80 = 2.5375).
    /// Used for validation against the [235, 255] acceptance bar.
    /// </summary>
    public static float ToneMap203NitToSdr()
    {
        // 203 nits in scRGB (1.0 = 80 nits reference white): scRGB = 203 / 80 = 2.5375
        float scRgbLinear = 203.0f / 80.0f;
        // Standard Reinhard / ACES-like curve calibrated so 203 nit reference white maps to ~248 (0.941)
        // L_out = L_in / (L_in + 0.16)
        float normalized = scRgbLinear / (scRgbLinear + 0.16f);
        // Apply sRGB transfer function (gamma ~2.2 approximation)
        float srgb = MathF.Pow(Math.Clamp(normalized, 0f, 1f), 1f / 2.2f);
        return Math.Clamp(srgb * 255.0f, 0f, 255.0f);
    }

    /// <summary>
    /// Tone-maps a single linear channel value in scRGB space to an 8-bit sRGB byte [0, 255].
    /// </summary>
    public static byte ToneMapLinearChannelToSdr(float linearVal)
    {
        if (linearVal <= 0f) return 0;
        float normalized = linearVal / (linearVal + 0.16f);
        float srgb = MathF.Pow(Math.Clamp(normalized, 0f, 1f), 1f / 2.2f);
        return (byte)Math.Clamp(MathF.Round(srgb * 255.0f), 0, 255);
    }

    /// <summary>
    /// Tone-maps an array of RGBA half-float (16-bit float) or float pixels to BGRA32 sRGB.
    /// </summary>
    public static void ToneMapRgbFloatToBgra32(ReadOnlySpan<float> srcRgbFloat, Span<byte> dstBgra, int width, int height)
    {
        int pixelCount = width * height;
        for (int i = 0; i < pixelCount; i++)
        {
            int srcIdx = i * 4;
            int dstIdx = i * 4;

            float r = srcRgbFloat[srcIdx];
            float g = srcRgbFloat[srcIdx + 1];
            float b = srcRgbFloat[srcIdx + 2];
            float a = srcRgbFloat[srcIdx + 3];

            dstBgra[dstIdx] = ToneMapLinearChannelToSdr(b);
            dstBgra[dstIdx + 1] = ToneMapLinearChannelToSdr(g);
            dstBgra[dstIdx + 2] = ToneMapLinearChannelToSdr(r);
            dstBgra[dstIdx + 3] = (byte)Math.Clamp(MathF.Round(Math.Clamp(a, 0f, 1f) * 255.0f), 0, 255);
        }
    }
}
