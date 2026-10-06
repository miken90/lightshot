// Ported from LightshotKit/Sources/LightshotKit/VideoExporter.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Recording;
using Vortice.MediaFoundation;

namespace Lightshot.Platform.Windows.Media;

public record TranscodeResult(
    bool Success,
    double Duration,
    string? ErrorMessage = null
);

/// <summary>
/// Media Foundation transcoder: re-encodes a video cut with dimension presets,
/// target bitrate per quality setting, and audio treatments (unchanged, muted, removed).
/// </summary>
public sealed class MfTranscoder
{
    public Task<TranscodeResult> TranscodeAsync(
        string inputPath,
        string outputPath,
        VideoEditSettings settings,
        double sourceFps = 30.0,
        Action<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            return Task.FromResult(new TranscodeResult(false, 0, $"Input file not found: {inputPath}"));
        }

        string tempProgressive = outputPath + ".tmp.mp4";

        try
        {
            return Task.Run(() =>
            {
                MediaFactory.MFStartup().CheckError();

                if (File.Exists(outputPath)) File.Delete(outputPath);
                if (File.Exists(tempProgressive)) File.Delete(tempProgressive);

                using var reader = MediaFactory.MFCreateSourceReaderFromURL(inputPath, null);
                reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
                reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

                using var nativeVideoType = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0);
                if (nativeVideoType == null)
                {
                    return new TranscodeResult(false, 0, "No video stream discovered in source file.");
                }

                MediaFactory.MFGetAttributeSize(nativeVideoType, MediaTypeAttributeKeys.FrameSize, out uint origWidth, out uint origHeight);
                int width = Math.Max(2, (int)Math.Round(settings.Dimensions.Width));
                int height = Math.Max(2, (int)Math.Round(settings.Dimensions.Height));
                // Round down to even
                width &= ~1;
                height &= ~1;

                double fps = sourceFps > 0 ? sourceFps : 30.0;
                double bitRate = VideoBitRate.VideoBitsPerSecond(new Size(width, height), fps, settings.Quality);

                // Configure sink writer
                using var sinkAttrs = MediaFactory.MFCreateAttributes(1);
                sinkAttrs.Set(TranscodeAttributeKeys.TranscodeContainertype, TranscodeContainerTypeGuids.Mpeg4);

                using var writer = MediaFactory.MFCreateSinkWriterFromURL(tempProgressive, null, sinkAttrs);

                // Output video type (H.264 encode)
                using var vOutType = MediaFactory.MFCreateMediaType();
                vOutType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                vOutType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
                MediaFactory.MFSetAttributeSize(vOutType, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height);
                MediaFactory.MFSetAttributeRatio(vOutType, MediaTypeAttributeKeys.FrameRate, (uint)Math.Round(fps), 1);
                MediaFactory.MFSetAttributeRatio(vOutType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
                vOutType.Set(MediaTypeAttributeKeys.AvgBitrate, (int)Math.Round(bitRate));
                vOutType.Set(MediaTypeAttributeKeys.InterlaceMode, (int)VideoInterlaceMode.Progressive);

                int writerVideoStreamIndex = writer.AddStream(vOutType);

                // Input video type to writer (RGB32)
                using var vInType = MediaFactory.MFCreateMediaType();
                vInType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                vInType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
                MediaFactory.MFSetAttributeSize(vInType, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height);
                MediaFactory.MFSetAttributeRatio(vInType, MediaTypeAttributeKeys.FrameRate, (uint)Math.Round(fps), 1);
                MediaFactory.MFSetAttributeRatio(vInType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
                vInType.Set(MediaTypeAttributeKeys.InterlaceMode, (int)VideoInterlaceMode.Progressive);

                writer.SetInputMediaType(writerVideoStreamIndex, vInType, null);

                // Reader output video type: decode to RGB32 scaled to target size
                using var readerVideoOutType = MediaFactory.MFCreateMediaType();
                readerVideoOutType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                readerVideoOutType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
                MediaFactory.MFSetAttributeSize(readerVideoOutType, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height);
                reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, readerVideoOutType);

                // Handle audio streams if not removed
                int? writerAudioStreamIndex = null;
                int readerAudioStreamIndex = -1;

                if (settings.Audio is not AudioEdit.Remove)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        try
                        {
                            using var nat = reader.GetNativeMediaType((SourceReaderIndex)i, 0);
                            if (nat.GetGUID(MediaTypeAttributeKeys.MajorType) == MediaTypeGuids.Audio)
                            {
                                readerAudioStreamIndex = i;
                                reader.SetStreamSelection((SourceReaderIndex)i, true);

                                // Passthrough or AAC copy
                                writerAudioStreamIndex = writer.AddStream(nat);
                                writer.SetInputMediaType(writerAudioStreamIndex.Value, nat, null);
                                reader.SetCurrentMediaType((SourceReaderIndex)i, nat);
                                break;
                            }
                        }
                        catch { break; }
                    }
                }

                writer.BeginWriting();

                long startHns = (long)Math.Round(settings.Trim.Start * 10_000_000.0);
                long endHns = (long)Math.Round(settings.Trim.End * 10_000_000.0);
                long maxTimestampHns = 0;
                double trimLengthSec = Math.Max(0.1, settings.Trim.Length);

                // Pump video
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
                        out long ts);

                    if (flags.HasFlag(SourceReaderFlag.EndOfStream) || sample == null)
                    {
                        break;
                    }

                    using (sample)
                    {
                        if (ts < startHns) continue;
                        if (ts > endHns) break;

                        long rebasedTs = Math.Max(0, ts - startHns);
                        sample.SampleTime = rebasedTs;

                        writer.WriteSample(writerVideoStreamIndex, sample);
                        if (rebasedTs > maxTimestampHns) maxTimestampHns = rebasedTs;

                        progress?.Invoke(Math.Min(1.0, (rebasedTs / 10_000_000.0) / trimLengthSec));
                    }
                }

                // Pump audio if enabled
                if (writerAudioStreamIndex.HasValue && readerAudioStreamIndex >= 0)
                {
                    while (true)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        var sample = reader.ReadSample(
                            (SourceReaderIndex)readerAudioStreamIndex,
                            SourceReaderControlFlag.None,
                            out _,
                            out var flags,
                            out long ts);

                        if (flags.HasFlag(SourceReaderFlag.EndOfStream) || sample == null)
                        {
                            break;
                        }

                        using (sample)
                        {
                            if (ts < startHns) continue;
                            if (ts > endHns) break;

                            long rebasedTs = Math.Max(0, ts - startHns);
                            sample.SampleTime = rebasedTs;

                            if (settings.Audio is AudioEdit.Mute)
                            {
                                // Silence sample buffer
                                using var buf = sample.ConvertToContiguousBuffer();
                                buf.Lock(out nint pData, out _, out int len);
                                try
                                {
                                    unsafe
                                    {
                                        byte* pBytes = (byte*)pData;
                                        for (int b = 0; b < len; b++) pBytes[b] = 0;
                                    }
                                }
                                finally { buf.Unlock(); }
                            }

                            writer.WriteSample(writerAudioStreamIndex.Value, sample);
                        }
                    }
                }

                writer.Finalize();

                // Fast-start relocation
                Mp4Remuxer.RelocateMoovBeforeMdat(tempProgressive, outputPath);
                try { if (File.Exists(tempProgressive)) File.Delete(tempProgressive); } catch { }

                double actualDuration = maxTimestampHns / 10_000_000.0;
                progress?.Invoke(1.0);

                return new TranscodeResult(true, actualDuration);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempProgressive)) File.Delete(tempProgressive); } catch { }
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            return Task.FromResult(new TranscodeResult(false, 0, ex.Message));
        }
    }
}
