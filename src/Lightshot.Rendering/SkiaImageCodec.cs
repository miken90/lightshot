// Ported from LightshotKit/Sources/LightshotKit/Render.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Core;
using SkiaSharp;

namespace Lightshot.Rendering;

/// <summary>
/// Encodes and decodes images using SkiaSharp.
/// Supports PNG and quality-clamped JPEG encoding.
/// </summary>
public class SkiaImageCodec : IImageCodec
{
    public byte[] Encode(RenderedImage image, ImageFormat format)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));

        bool isPng = image.Data.Length >= 8 &&
                     image.Data[0] == 0x89 && image.Data[1] == 0x50 &&
                     image.Data[2] == 0x4E && image.Data[3] == 0x47 &&
                     image.Data[4] == 0x0D && image.Data[5] == 0x0A &&
                     image.Data[6] == 0x1A && image.Data[7] == 0x0A;

        switch (format)
        {
            case ImageFormat.Png:
                if (isPng)
                {
                    return image.Data;
                }

                if (image.PixelWidth > 0 && image.PixelHeight > 0 && image.Data.Length == image.PixelWidth * image.PixelHeight * 4)
                {
                    var info = new SKImageInfo(image.PixelWidth, image.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                    using var bitmap = new SKBitmap(info);
                    using (var pixmap = bitmap.PeekPixels())
                    {
                        image.Data.AsSpan().CopyTo(pixmap.GetPixelSpan());
                    }
                    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                    return data?.ToArray() ?? image.Data;
                }
                else
                {
                    using var ms = new MemoryStream(image.Data);
                    using var bitmap = SKBitmap.Decode(ms);
                    if (bitmap != null)
                    {
                        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                        return data?.ToArray() ?? image.Data;
                    }
                    return image.Data;
                }

            case ImageFormat.Jpeg jpeg:
                double clampedQuality = Math.Clamp(jpeg.Quality, 0.0, 1.0);
                int qualityInt = (int)Math.Round(clampedQuality * 100.0);

                if (image.PixelWidth > 0 && image.PixelHeight > 0 && image.Data.Length == image.PixelWidth * image.PixelHeight * 4)
                {
                    var info = new SKImageInfo(image.PixelWidth, image.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                    using var bitmap = new SKBitmap(info);
                    using (var pixmap = bitmap.PeekPixels())
                    {
                        image.Data.AsSpan().CopyTo(pixmap.GetPixelSpan());
                    }
                    using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, qualityInt);
                    return data?.ToArray() ?? image.Data;
                }
                else
                {
                    using var ms = new MemoryStream(image.Data);
                    using var bitmap = SKBitmap.Decode(ms);
                    if (bitmap != null)
                    {
                        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, qualityInt);
                        return data?.ToArray() ?? image.Data;
                    }
                    return image.Data;
                }

            default:
                return image.Data;
        }
    }

    public CapturedImage? Decode(byte[] data)
    {
        if (data == null || data.Length == 0) return null;

        try
        {
            using var ms = new MemoryStream(data);
            using var codec = SKCodec.Create(ms);
            if (codec == null)
            {
                return new CapturedImage(0, 0, data);
            }

            int width = codec.Info.Width;
            int height = codec.Info.Height;
            if (width <= 0 || height <= 0)
            {
                return new CapturedImage(0, 0, data);
            }

            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(info);
            var result = codec.GetPixels(info, bitmap.GetPixels());
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            {
                return new CapturedImage(width, height, data);
            }

            using var pixmap = bitmap.PeekPixels();
            return new CapturedImage(width, height, pixmap.GetPixelSpan().ToArray());
        }
        catch
        {
            return new CapturedImage(0, 0, data);
        }
    }
}
