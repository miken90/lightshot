// Ported from LightshotKit/Sources/LightshotKit/VideoExporter.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Recording;
using Vortice.MediaFoundation;

namespace Lightshot.Platform.Windows.Media;

public record TrimResult(
    bool Success,
    double SnappedStartTime,
    double Duration,
    string? ErrorMessage = null
);

/// <summary>
/// Media Foundation video trimmer: performs a compressed-sample passthrough cut
/// without re-encoding, preserving all audio and video streams and snapping the start
/// point to the preceding keyframe (within &lt;= 2 seconds early).
/// </summary>
public sealed class MfVideoTrimmer
{
    public Task<TrimResult> TrimAsync(
        string inputPath,
        string outputPath,
        TrimRange range,
        Action<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            return Task.FromResult(new TrimResult(false, 0, 0, $"Input file not found: {inputPath}"));
        }

        string tempProgressive = outputPath + ".tmp.mp4";

        try
        {
            return Task.Run(() =>
            {
                MediaFactory.MFStartup().CheckError();

                // 1. First pass: find the preceding keyframe timestamp at or before range.Start
                double snappedStart = FindPrecedingKeyFrame(inputPath, range.Start);
                long snappedStartHns = (long)Math.Round(snappedStart * 10_000_000.0);
                long endHns = (long)Math.Round(range.End * 10_000_000.0);

                // 2. Second pass: passthrough remux from snappedStart to range.End
                if (File.Exists(outputPath)) File.Delete(outputPath);
                if (File.Exists(tempProgressive)) File.Delete(tempProgressive);

                using var reader = MediaFactory.MFCreateSourceReaderFromURL(inputPath, null);
                reader.SetStreamSelection(SourceReaderIndex.AllStreams, true);

                using var sinkAttrs = MediaFactory.MFCreateAttributes(1);
                sinkAttrs.Set(TranscodeAttributeKeys.TranscodeContainertype, TranscodeContainerTypeGuids.Mpeg4);

                using var writer = MediaFactory.MFCreateSinkWriterFromURL(tempProgressive, null, sinkAttrs);

                var map = new Dictionary<int, int>(); // readerStreamIndex -> writerStreamIndex
                var streamIndices = GetStreamIndices(reader);

                foreach (int ri in streamIndices)
                {
                    using var nt = reader.GetNativeMediaType((SourceReaderIndex)ri, 0);
                    reader.SetCurrentMediaType((SourceReaderIndex)ri, nt);
                    int wi = writer.AddStream(nt);
                    writer.SetInputMediaType(wi, nt, null);
                    map[ri] = wi;
                }

                if (map.Count == 0)
                {
                    return new TrimResult(false, 0, 0, "No audio or video streams found in input file.");
                }

                writer.BeginWriting();

                var done = new HashSet<int>();
                var lastTs = map.Keys.ToDictionary(k => k, _ => 0L);
                long maxTimestampHns = 0;
                double targetLengthSec = Math.Max(0.1, range.End - snappedStart);

                while (done.Count < map.Count)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    int ri = map.Keys.Where(k => !done.Contains(k)).OrderBy(k => lastTs[k]).First();
                    var sample = reader.ReadSample((SourceReaderIndex)ri, SourceReaderControlFlag.None, out _, out var flags, out long ts);

                    if (flags.HasFlag(SourceReaderFlag.EndOfStream))
                    {
                        done.Add(ri);
                        continue;
                    }

                    if (sample == null)
                    {
                        lastTs[ri] = Math.Max(lastTs[ri], ts);
                        continue;
                    }

                    using (sample)
                    {
                        lastTs[ri] = Math.Max(lastTs[ri], ts);

                        // Skip samples before the snapped keyframe
                        if (ts < snappedStartHns)
                        {
                            continue;
                        }

                        // Stop stream if past trim end
                        if (ts > endHns)
                        {
                            done.Add(ri);
                            continue;
                        }

                        long rebasedTs = Math.Max(0, ts - snappedStartHns);
                        sample.SampleTime = rebasedTs;

                        writer.WriteSample(map[ri], sample);
                        if (rebasedTs > maxTimestampHns)
                        {
                            maxTimestampHns = rebasedTs;
                        }

                        progress?.Invoke(Math.Min(1.0, (rebasedTs / 10_000_000.0) / targetLengthSec));
                    }
                }

                writer.Finalize();

                // Fast-start MP4 relocation
                Mp4Remuxer.RelocateMoovBeforeMdat(tempProgressive, outputPath);
                try { if (File.Exists(tempProgressive)) File.Delete(tempProgressive); } catch { }

                double actualDuration = maxTimestampHns / 10_000_000.0;
                progress?.Invoke(1.0);

                return new TrimResult(true, snappedStart, actualDuration);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempProgressive)) File.Delete(tempProgressive); } catch { }
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            return Task.FromResult(new TrimResult(false, 0, 0, ex.Message));
        }
    }

    private static double FindPrecedingKeyFrame(string inputPath, double requestedStart)
    {
        if (requestedStart <= 0) return 0.0;

        using var reader = MediaFactory.MFCreateSourceReaderFromURL(inputPath, null);
        reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

        using var nt = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0);
        reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, nt);

        long requestedHns = (long)Math.Round(requestedStart * 10_000_000.0);
        long lastKeyFrameHns = 0;

        while (true)
        {
            var sample = reader.ReadSample(
                SourceReaderIndex.FirstVideoStream,
                SourceReaderControlFlag.None,
                out _,
                out var flags,
                out long ts);

            if (flags.HasFlag(SourceReaderFlag.EndOfStream))
            {
                break;
            }

            if (sample == null)
            {
                continue;
            }

            using (sample)
            {
                long sampleTime = sample.SampleTime;
                if (sampleTime < 0) sampleTime = ts;

                if (sampleTime <= requestedHns && IsKeyFrame(sample))
                {
                    if (sampleTime > lastKeyFrameHns)
                    {
                        lastKeyFrameHns = sampleTime;
                    }
                }
                else if (sampleTime > requestedHns + 20_000_000)
                {
                    break;
                }
            }
        }

        return lastKeyFrameHns / 10_000_000.0;
    }

    private static bool IsKeyFrame(IMFSample sample)
    {
        try
        {
            return sample.GetUInt32(SampleAttributeKeys.CleanPoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static List<int> GetStreamIndices(IMFSourceReader reader)
    {
        var list = new List<int>();
        for (int i = 0; i < 16; i++)
        {
            try
            {
                using var nt = reader.GetNativeMediaType((SourceReaderIndex)i, 0);
                Guid major = nt.GetGUID(MediaTypeAttributeKeys.MajorType);
                if (major == MediaTypeGuids.Video || major == MediaTypeGuids.Audio)
                {
                    list.Add(i);
                }
            }
            catch
            {
                break;
            }
        }
        return list;
    }
}
