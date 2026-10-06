// Ported from LightshotKit/Sources/LightshotKit/AVMediaMetadata.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Threading.Tasks;
using Lightshot.Core;
using SkiaSharp;
using Vortice.MediaFoundation;

namespace Lightshot.Platform.Windows.Media;

/// <summary>
/// Media Foundation implementation of IMediaMetadataSource: extracts video dimensions,
/// total duration, and a first-frame PNG thumbnail up to 320x320 pixels.
/// </summary>
public sealed class MfMediaMetadata : IMediaMetadataSource
{
    public const int ThumbnailMaxPixelSize = 320;

    public Task<VideoMetadata?> VideoMetadataAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Task.FromResult<VideoMetadata?>(null);
        }

        try
        {
            MediaFactory.MFStartup().CheckError();

            using var attrs = MediaFactory.MFCreateAttributes(1);
            attrs.Set(SourceReaderAttributeKeys.EnableVideoProcessing, true);
            using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, attrs);
            reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

            using var nativeType = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0);
            if (nativeType == null) return Task.FromResult<VideoMetadata?>(null);

            MediaFactory.MFGetAttributeSize(nativeType, MediaTypeAttributeKeys.FrameSize, out uint uWidth, out uint uHeight);
            int width = (int)uWidth;
            int height = (int)uHeight;
            if (width <= 0 || height <= 0) return Task.FromResult<VideoMetadata?>(null);

            double duration = GetDuration(reader);

            // Configure RGB32 output for thumbnail extraction
            using var rgbType = MediaFactory.MFCreateMediaType();
            rgbType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            rgbType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, rgbType);

            byte[]? thumbnailPng = null;
            IMFSample? sample = null;
            while (true)
            {
                sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out _);
                if (flags.HasFlag(SourceReaderFlag.EndOfStream) || sample != null)
                {
                    break;
                }
            }

            if (sample != null)
            {
                using (sample)
                {
                    using var buffer = sample.ConvertToContiguousBuffer();
                    buffer.Lock(out nint pData, out _, out int length);
                    try
                    {
                        if (length >= width * height * 4)
                        {
                            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                            using var srcBitmap = new SKBitmap();
                            srcBitmap.InstallPixels(info, pData, width * 4);

                            // Scale to thumbnailMaxPixelSize proportionally if necessary
                            if (width > ThumbnailMaxPixelSize || height > ThumbnailMaxPixelSize)
                            {
                                double scale = Math.Min((double)ThumbnailMaxPixelSize / width, (double)ThumbnailMaxPixelSize / height);
                                int thumbW = Math.Max(1, (int)Math.Round(width * scale));
                                int thumbH = Math.Max(1, (int)Math.Round(height * scale));

                                using var thumbBmp = srcBitmap.Resize(new SKImageInfo(thumbW, thumbH, SKColorType.Bgra8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                                if (thumbBmp != null)
                                {
                                    using var image = SKImage.FromBitmap(thumbBmp);
                                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                                    thumbnailPng = data.ToArray();
                                }
                            }
                            else
                            {
                                using var image = SKImage.FromBitmap(srcBitmap);
                                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                                thumbnailPng = data.ToArray();
                            }
                        }
                    }
                    finally
                    {
                        buffer.Unlock();
                    }
                }
            }

            if (thumbnailPng == null || thumbnailPng.Length == 0)
            {
                // Fallback 1x1 transparent PNG if decoding sample produced no thumbnail
                using var fallbackBmp = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
                using var image = SKImage.FromBitmap(fallbackBmp);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                thumbnailPng = data.ToArray();
            }

            var metadata = new VideoMetadata(width, height, duration, thumbnailPng);
            return Task.FromResult<VideoMetadata?>(metadata);
        }
        catch
        {
            return Task.FromResult<VideoMetadata?>(null);
        }
    }

    private static double GetDuration(IMFSourceReader reader)
    {
        try
        {
            var prop = reader.GetPresentationAttribute(SourceReaderIndex.MediaSource, PresentationDescriptionAttributeKeys.Duration);
            if (prop.Value is ulong u64) return u64 / 10_000_000.0;
            if (prop.Value is long s64) return s64 / 10_000_000.0;
            if (prop.Value != null) return Convert.ToDouble(prop.Value) / 10_000_000.0;
        }
        catch { }
        return 0;
    }
}
