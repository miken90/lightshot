// Ported from LightshotKit/Sources/LightshotKit/HistoryStore.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using Lightshot.Core;
using SkiaSharp;

namespace Lightshot.Rendering;

/// <summary>
/// Generates thumbnails and extracts GIF metadata using SkiaSharp.
/// </summary>
public class SkiaThumbnailer : IThumbnailer
{
    public const int DefaultThumbnailSize = 320;

    public byte[]? CreateThumbnail(byte[] imageData, int maxPixelSize)
    {
        if (imageData == null || imageData.Length == 0 || maxPixelSize <= 0)
        {
            return null;
        }

        try
        {
            using var ms = new MemoryStream(imageData);
            using var original = SKBitmap.Decode(ms);
            if (original == null || original.Width <= 0 || original.Height <= 0)
            {
                return null;
            }

            int maxDim = Math.Max(original.Width, original.Height);
            if (maxDim <= maxPixelSize)
            {
                using var pngData = original.Encode(SKEncodedImageFormat.Png, 100);
                return pngData?.ToArray();
            }

            double scale = (double)maxPixelSize / maxDim;
            int targetWidth = Math.Max(1, (int)Math.Round(original.Width * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(original.Height * scale));

            var info = new SKImageInfo(targetWidth, targetHeight, original.ColorType, original.AlphaType);
            using var resized = original.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            if (resized == null)
            {
                return null;
            }

            using var data = resized.Encode(SKEncodedImageFormat.Png, 100);
            return data?.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public GifMetadata? ReadGifMetadata(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            {
                return null;
            }

            int width = codec.Info.Width;
            int height = codec.Info.Height;
            var frameInfos = codec.FrameInfo;

            double duration = 0.0;
            if (frameInfos != null && frameInfos.Length > 0)
            {
                foreach (var fi in frameInfos)
                {
                    // A 0 delay is treated as 0.1s by browsers and ImageIO
                    double frameSec = fi.Duration > 0 ? fi.Duration / 1000.0 : 0.1;
                    duration += frameSec;
                }
            }
            else
            {
                duration = 0.1;
            }

            // Extract frame 0 for the thumbnail
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var frame0 = new SKBitmap(info);
            var result = codec.GetPixels(info, frame0.GetPixels());

            byte[]? thumbBytes = null;
            if (result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput)
            {
                int maxDim = Math.Max(width, height);
                if (maxDim <= DefaultThumbnailSize)
                {
                    using var enc = frame0.Encode(SKEncodedImageFormat.Png, 100);
                    thumbBytes = enc?.ToArray();
                }
                else
                {
                    double scale = (double)DefaultThumbnailSize / maxDim;
                    int tw = Math.Max(1, (int)Math.Round(width * scale));
                    int th = Math.Max(1, (int)Math.Round(height * scale));
                    var targetInfo = new SKImageInfo(tw, th, SKColorType.Rgba8888, SKAlphaType.Premul);
                    using var resized = frame0.Resize(targetInfo, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                    using var enc = (resized ?? frame0).Encode(SKEncodedImageFormat.Png, 100);
                    thumbBytes = enc?.ToArray();
                }
            }

            thumbBytes ??= Array.Empty<byte>();
            return new GifMetadata(width, height, duration, thumbBytes);
        }
        catch
        {
            return null;
        }
    }
}
