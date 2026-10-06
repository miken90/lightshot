// Ported from LightshotKit/Sources/LightshotKit/GIFEncoder.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Core;
using SkiaSharp;
using Vortice.MediaFoundation;

namespace Lightshot.Platform.Windows.Media;

/// <summary>
/// Media Foundation GIF encoder: reads a finished MP4 using IMFSourceReader,
/// samples frames per Core GIFFramePlan, scales to target dimensions, posterises per quality,
/// optimizes transparency with frame differencing within tolerance 8/255, quantizes palettes
/// with GifQuantizer, and writes to a .partial file with GifWriter.
/// </summary>
public sealed class MfGifEncoder : IGifEncoding
{
    private const int TransparencyTolerance = 8;

    public async Task EncodeAsync(
        string videoPath,
        string outputPath,
        GIFSettings settings,
        Action<double> progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            throw new FileNotFoundException("Input video file not found.", videoPath);
        }

        string partialPath = outputPath + ".partial";

        // Clean any pre-existing output or partial files
        try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
        try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }

        string? outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        try
        {
            await Task.Run(() =>
            {
                MediaFactory.MFStartup().CheckError();

                using var attrs = MediaFactory.MFCreateAttributes(1);
                attrs.Set(SourceReaderAttributeKeys.EnableVideoProcessing, true);
                using var reader = MediaFactory.MFCreateSourceReaderFromURL(videoPath, attrs);
                reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
                reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

                using var nativeType = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0);
                if (nativeType == null)
                {
                    throw new InvalidOperationException("The recording has no video track.");
                }

                MediaFactory.MFGetAttributeSize(nativeType, MediaTypeAttributeKeys.FrameSize, out uint uWidth, out uint uHeight);
                int srcWidth = (int)uWidth;
                int srcHeight = (int)uHeight;
                if (srcWidth <= 0 || srcHeight <= 0)
                {
                    throw new InvalidOperationException("Invalid video dimensions.");
                }

                double duration = GetDuration(reader);
                var plan = new GIFFramePlan(duration, new Size(srcWidth, srcHeight), settings);

                int targetWidth = Math.Max(1, (int)Math.Round(plan.OutputSize.Width));
                int targetHeight = Math.Max(1, (int)Math.Round(plan.OutputSize.Height));
                ushort delayCs = (ushort)Math.Max(2, Math.Round(plan.FrameDelay * 100.0));

                // Configure reader to decode video frames to RGB32 (BGRA)
                using var rgbType = MediaFactory.MFCreateMediaType();
                rgbType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                rgbType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
                reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, rgbType);

                int bitsPerChannel = GIFFramePlan.BitsPerChannel(settings.Quality);
                byte mask = bitsPerChannel >= 8 ? (byte)0xFF : (byte)(0xFF << (8 - bitsPerChannel));
                byte half = (byte)((~mask & 0xFF) / 2 + 1);

                int pixelCount = targetWidth * targetHeight;
                byte[] currentRgba = new byte[pixelCount * 4];
                byte[]? previousShownRgb = null;
                byte[] frameOutputRgba = new byte[pixelCount * 4];

                FileStream? fs = null;
                GifWriter? writer = null;

                try
                {
                    fs = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    writer = new GifWriter(fs, targetWidth, targetHeight, loopCount: 0, leaveOpen: false);

                    int? lastFilled = null;
                    QuantizedGifFrame? lastQuantized = null;

                    while (true)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }

                        var sample = reader.ReadSample(
                            SourceReaderIndex.FirstVideoStream,
                            SourceReaderControlFlag.None,
                            out _,
                            out var flags,
                            out long timestampHns);

                        if (flags.HasFlag(SourceReaderFlag.EndOfStream) || sample == null)
                        {
                            break;
                        }

                        using (sample)
                        {
                            double timeSec = timestampHns / 10_000_000.0;
                            int? nextIndex = plan.OutputIndex(timeSec, lastFilled);
                            if (!nextIndex.HasValue)
                            {
                                continue;
                            }

                            using var buffer = sample.ConvertToContiguousBuffer();
                            buffer.Lock(out nint pData, out _, out int length);
                            try
                            {
                                if (length < srcWidth * srcHeight * 4) continue;

                                var srcInfo = new SKImageInfo(srcWidth, srcHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                                using var srcBitmap = new SKBitmap();
                                srcBitmap.InstallPixels(srcInfo, pData, srcWidth * 4);

                                var dstInfo = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                                using var dstBitmap = new SKBitmap(dstInfo);
                                srcBitmap.ScalePixels(dstBitmap, new SKSamplingOptions(SKFilterMode.Linear));

                                dstBitmap.GetPixelSpan().CopyTo(currentRgba.AsSpan());
                            }
                            finally
                            {
                                buffer.Unlock();
                            }

                            // Posterize channels per quality setting
                            if (mask != 0xFF)
                            {
                                for (int i = 0; i < pixelCount; i++)
                                {
                                    int off = i * 4;
                                    currentRgba[off] = Step(currentRgba[off], mask, half);
                                    currentRgba[off + 1] = Step(currentRgba[off + 1], mask, half);
                                    currentRgba[off + 2] = Step(currentRgba[off + 2], mask, half);
                                }
                            }

                            // Frame differencing & transparency optimization
                            if (settings.Optimize && previousShownRgb != null)
                            {
                                for (int i = 0; i < pixelCount; i++)
                                {
                                    int off = i * 4;
                                    byte r = currentRgba[off];
                                    byte g = currentRgba[off + 1];
                                    byte b = currentRgba[off + 2];

                                    int sOff = i * 3;
                                    byte sr = previousShownRgb[sOff];
                                    byte sg = previousShownRgb[sOff + 1];
                                    byte sb = previousShownRgb[sOff + 2];

                                    if (IsClose(r, sr) && IsClose(g, sg) && IsClose(b, sb))
                                    {
                                        // Unchanged pixel: write transparent
                                        frameOutputRgba[off] = 0;
                                        frameOutputRgba[off + 1] = 0;
                                        frameOutputRgba[off + 2] = 0;
                                        frameOutputRgba[off + 3] = 0;
                                    }
                                    else
                                    {
                                        frameOutputRgba[off] = r;
                                        frameOutputRgba[off + 1] = g;
                                        frameOutputRgba[off + 2] = b;
                                        frameOutputRgba[off + 3] = 255;

                                        previousShownRgb[sOff] = r;
                                        previousShownRgb[sOff + 1] = g;
                                        previousShownRgb[sOff + 2] = b;
                                    }
                                }
                            }
                            else
                            {
                                Buffer.BlockCopy(currentRgba, 0, frameOutputRgba, 0, currentRgba.Length);
                                if (settings.Optimize)
                                {
                                    previousShownRgb = new byte[pixelCount * 3];
                                    for (int i = 0; i < pixelCount; i++)
                                    {
                                        int off = i * 4;
                                        int sOff = i * 3;
                                        previousShownRgb[sOff] = currentRgba[off];
                                        previousShownRgb[sOff + 1] = currentRgba[off + 1];
                                        previousShownRgb[sOff + 2] = currentRgba[off + 2];
                                    }
                                }
                            }

                            var quantized = GifQuantizer.Quantize(frameOutputRgba, targetWidth, targetHeight, isBgra: false, maxColors: 256);
                            writer.WriteFrame(quantized, delayCs, restoreToBackground: !settings.Optimize);

                            lastFilled = nextIndex.Value;
                            lastQuantized = quantized;

                            progress(Math.Min(1.0, (double)(lastFilled.Value + 1) / plan.FrameCount));
                        }
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    if (lastQuantized == null)
                    {
                        throw new InvalidOperationException("The recording produced no decodable video frames.");
                    }

                    // Pad remaining promised frames if source ended early
                    int filled = lastFilled ?? 0;
                    for (int f = filled + 1; f < plan.FrameCount; f++)
                    {
                        writer.WriteFrame(lastQuantized, delayCs, restoreToBackground: !settings.Optimize);
                        progress(Math.Min(1.0, (double)(f + 1) / plan.FrameCount));
                    }

                    writer.Dispose();
                    writer = null;
                    fs.Dispose();
                    fs = null;

                    // Atomically move partial file to final destination
                    if (File.Exists(outputPath)) File.Delete(outputPath);
                    File.Move(partialPath, outputPath);
                    progress(1.0);
                }
                finally
                {
                    writer?.Dispose();
                    fs?.Dispose();

                    if (cancellationToken.IsCancellationRequested || !File.Exists(outputPath))
                    {
                        try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
                    }
                }
            }, cancellationToken);
        }
        catch
        {
            try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
            throw;
        }
    }

    private static byte Step(byte value, byte mask, byte half)
    {
        int sum = value + half;
        return (byte)(Math.Min(255, sum) & mask);
    }

    private static bool IsClose(byte a, byte b) => Math.Abs((int)a - (int)b) <= TransparencyTolerance;

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
