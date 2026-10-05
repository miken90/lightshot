using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Vortice.MediaFoundation;

namespace Lightshot.Platform.Windows.Recording;

public record RemuxResult(
    bool Success,
    int StreamCount,
    double DurationSeconds,
    string? ErrorMessage = null
);

public interface IRemuxer
{
    RemuxResult Remux(string inputPath, string outputPath);
}

/// <summary>
/// Passthrough MP4 remuxer that places the 'moov' box before 'mdat' for fast-start playback.
/// Preserves all video and audio tracks without re-encoding.
/// </summary>
public sealed class Mp4Remuxer : IRemuxer
{
    public static readonly Guid Mpeg4SinkMoovBeforeMdat = new("f672e3ac-e1e6-4f10-b5ec-5f3b30828816");

    public RemuxResult Remux(string inputPath, string outputPath)
    {
        if (!File.Exists(inputPath))
        {
            return new RemuxResult(false, 0, 0, $"Input file not found: {inputPath}");
        }

        string tempProgressive = outputPath + ".tmp.mp4";

        try
        {
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
                return new RemuxResult(false, 0, 0, "No audio or video streams discovered in take.");
            }

            writer.BeginWriting();

            var done = new HashSet<int>();
            var lastTs = map.Keys.ToDictionary(k => k, _ => 0L);
            long maxTimestampHns = 0;

            while (done.Count < map.Count)
            {
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

                writer.WriteSample(map[ri], sample);
                lastTs[ri] = Math.Max(lastTs[ri], ts);
                if (ts > maxTimestampHns) maxTimestampHns = ts;
                sample.Dispose();
            }

            writer.Finalize();

            // Relocate 'moov' before 'mdat' for fast-start playback
            RelocateMoovBeforeMdat(tempProgressive, outputPath);
            try { File.Delete(tempProgressive); } catch { }

            double durationSec = maxTimestampHns / 10_000_000.0;
            return new RemuxResult(true, map.Count, durationSec);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempProgressive)) File.Delete(tempProgressive); } catch { }
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            return new RemuxResult(false, 0, 0, $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Fast-start MP4 relocation: places 'moov' before 'mdat' and adjusts chunk offsets (stco/co64) by moovSize.
    /// </summary>
    public static void RelocateMoovBeforeMdat(string inputPath, string outputPath)
    {
        using var fsIn = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        long len = fsIn.Length;

        long moovPos = -1;
        long moovSize = 0;
        long mdatPos = -1;
        long mdatSize = 0;
        long pos = 0;
        var hdr = new byte[8];

        var boxes = new List<(string tag, long start, long size)>();

        while (pos + 8 <= len)
        {
            fsIn.Seek(pos, SeekOrigin.Begin);
            if (fsIn.Read(hdr, 0, 8) < 8) break;

            uint bSize = (uint)((hdr[0] << 24) | (hdr[1] << 16) | (hdr[2] << 8) | hdr[3]);
            string tag = Encoding.ASCII.GetString(hdr, 4, 4);

            long actualSize = bSize;
            if (bSize == 1)
            {
                var ext = new byte[8];
                if (fsIn.Read(ext, 0, 8) < 8) break;
                actualSize = (long)(((ulong)ext[0] << 56) | ((ulong)ext[1] << 48) | ((ulong)ext[2] << 40) | ((ulong)ext[3] << 32) |
                                    ((ulong)ext[4] << 24) | ((ulong)ext[5] << 16) | ((ulong)ext[6] << 8) | ext[7]);
            }
            else if (bSize == 0)
            {
                actualSize = len - pos;
            }

            boxes.Add((tag, pos, actualSize));

            if (tag == "moov" && moovPos < 0) { moovPos = pos; moovSize = actualSize; }
            if (tag == "mdat" && mdatPos < 0) { mdatPos = pos; mdatSize = actualSize; }

            pos += actualSize;
        }

        // If moov is already before mdat or one is missing, just copy
        if (moovPos < 0 || mdatPos < 0 || moovPos < mdatPos)
        {
            File.Copy(inputPath, outputPath, overwrite: true);
            return;
        }

        // Read moov box into memory and patch stco/co64 offsets
        byte[] moovBytes = new byte[moovSize];
        fsIn.Seek(moovPos, SeekOrigin.Begin);
        fsIn.ReadExactly(moovBytes);

        PatchChunkOffsets(moovBytes, (uint)moovSize);

        // Write output: prefix boxes (e.g. ftyp) -> moov -> mdat -> any suffix boxes
        using var fsOut = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);

        // 1. Boxes before mdat (e.g. ftyp, uuid)
        foreach (var (tag, bStart, bSize) in boxes)
        {
            if (bStart < mdatPos && tag != "moov")
            {
                CopyStreamRange(fsIn, fsOut, bStart, bSize);
            }
        }

        // 2. Patched moov box
        fsOut.Write(moovBytes, 0, moovBytes.Length);

        // 3. mdat box
        CopyStreamRange(fsIn, fsOut, mdatPos, mdatSize);

        // 4. Any remaining boxes (between mdat and moov, or after moov)
        foreach (var (tag, bStart, bSize) in boxes)
        {
            if (tag != "moov" && tag != "mdat" && bStart > mdatPos)
            {
                CopyStreamRange(fsIn, fsOut, bStart, bSize);
            }
        }
    }

    private static void CopyStreamRange(Stream source, Stream destination, long offset, long length)
    {
        source.Seek(offset, SeekOrigin.Begin);
        byte[] buffer = new byte[64 * 1024];
        long remaining = length;
        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = source.Read(buffer, 0, toRead);
            if (read <= 0) break;
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    /// <summary>
    /// Traverses the box hierarchy (moov > trak > mdia > minf > stbl) and patches chunk offsets in 'stco' and 'co64'.
    /// Throws OverflowException if any 32-bit stco offset would exceed uint.MaxValue after shifting.
    /// </summary>
    public static void PatchChunkOffsets(byte[] moov, uint shift)
    {
        int moovHdr = ReadHeaderSize(moov, 0);
        WalkContainer(moov, moovHdr, moov.Length, "trak", trakStart =>
        {
            int trakHdr = ReadHeaderSize(moov, trakStart);
            int trakEnd = trakStart + (int)ReadBoxSize(moov, trakStart);
            WalkContainer(moov, trakStart + trakHdr, trakEnd, "mdia", mdiaStart =>
            {
                int mdiaHdr = ReadHeaderSize(moov, mdiaStart);
                int mdiaEnd = mdiaStart + (int)ReadBoxSize(moov, mdiaStart);
                WalkContainer(moov, mdiaStart + mdiaHdr, mdiaEnd, "minf", minfStart =>
                {
                    int minfHdr = ReadHeaderSize(moov, minfStart);
                    int minfEnd = minfStart + (int)ReadBoxSize(moov, minfStart);
                    WalkContainer(moov, minfStart + minfHdr, minfEnd, "stbl", stblStart =>
                    {
                        int stblHdr = ReadHeaderSize(moov, stblStart);
                        int stblEnd = stblStart + (int)ReadBoxSize(moov, stblStart);
                        PatchStblChunkOffsets(moov, stblStart + stblHdr, stblEnd, shift);
                    });
                });
            });
        });
    }

    private static void WalkContainer(byte[] data, int start, int end, string targetTag, Action<int> onBoxFound)
    {
        int pos = start;
        while (pos + 8 <= end)
        {
            long boxSize = ReadBoxSize(data, pos);
            string tag = ReadAscii(data, pos + 4, 4);

            if (tag == targetTag)
            {
                onBoxFound(pos);
            }

            if (boxSize <= 0 || pos + boxSize > end)
            {
                break;
            }

            pos += (int)boxSize;
        }
    }

    private static void PatchStblChunkOffsets(byte[] data, int start, int end, uint shift)
    {
        int pos = start;
        while (pos + 8 <= end)
        {
            long boxSize = ReadBoxSize(data, pos);
            string tag = ReadAscii(data, pos + 4, 4);

            if (tag == "stco")
            {
                // FullBox: 4 bytes size, 4 bytes tag ('stco'), 1 byte version, 3 bytes flags, 4 bytes entry_count
                int entryCountPos = pos + 12;
                if (entryCountPos + 4 <= end)
                {
                    uint count = ReadUInt32BE(data, entryCountPos);
                    int entriesPos = entryCountPos + 4;
                    for (int i = 0; i < count; i++)
                    {
                        int offsetPos = entriesPos + i * 4;
                        if (offsetPos + 4 > end) break;

                        uint oldOff = ReadUInt32BE(data, offsetPos);
                        ulong sum = (ulong)oldOff + (ulong)shift;
                        if (sum > uint.MaxValue)
                        {
                            throw new OverflowException(
                                $"Chunk offset 0x{oldOff:X8} + shift {shift} (0x{sum:X}) exceeds uint.MaxValue in 32-bit stco table. " +
                                "Remux cannot proceed without 64-bit co64 box; take preserved intact.");
                        }

                        WriteUInt32BE(data, offsetPos, (uint)sum);
                    }
                }
            }
            else if (tag == "co64")
            {
                // FullBox: 4 bytes size, 4 bytes tag ('co64'), 1 byte version, 3 bytes flags, 4 bytes entry_count
                int entryCountPos = pos + 12;
                if (entryCountPos + 4 <= end)
                {
                    uint count = ReadUInt32BE(data, entryCountPos);
                    int entriesPos = entryCountPos + 4;
                    for (int i = 0; i < count; i++)
                    {
                        int offsetPos = entriesPos + i * 8;
                        if (offsetPos + 8 > end) break;

                        ulong oldOff = ReadUInt64BE(data, offsetPos);
                        ulong newOff = checked(oldOff + (ulong)shift);
                        WriteUInt64BE(data, offsetPos, newOff);
                    }
                }
            }

            if (boxSize <= 0 || pos + boxSize > end)
            {
                break;
            }

            pos += (int)boxSize;
        }
    }

    private static long ReadBoxSize(byte[] data, int pos)
    {
        if (pos + 4 > data.Length) return 0;
        uint sz = ReadUInt32BE(data, pos);
        if (sz == 1)
        {
            if (pos + 16 > data.Length) return 0;
            return (long)ReadUInt64BE(data, pos + 8);
        }
        if (sz == 0)
        {
            return data.Length - pos;
        }
        return sz;
    }

    private static int ReadHeaderSize(byte[] data, int pos)
    {
        if (pos + 4 > data.Length) return 8;
        uint sz = ReadUInt32BE(data, pos);
        return sz == 1 ? 16 : 8;
    }

    private static uint ReadUInt32BE(byte[] data, int pos)
    {
        return (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);
    }

    private static void WriteUInt32BE(byte[] data, int pos, uint val)
    {
        data[pos] = (byte)((val >> 24) & 0xFF);
        data[pos + 1] = (byte)((val >> 16) & 0xFF);
        data[pos + 2] = (byte)((val >> 8) & 0xFF);
        data[pos + 3] = (byte)(val & 0xFF);
    }

    private static ulong ReadUInt64BE(byte[] data, int pos)
    {
        return ((ulong)data[pos] << 56) |
               ((ulong)data[pos + 1] << 48) |
               ((ulong)data[pos + 2] << 40) |
               ((ulong)data[pos + 3] << 32) |
               ((ulong)data[pos + 4] << 24) |
               ((ulong)data[pos + 5] << 16) |
               ((ulong)data[pos + 6] << 8) |
               (ulong)data[pos + 7];
    }

    private static void WriteUInt64BE(byte[] data, int pos, ulong val)
    {
        data[pos] = (byte)((val >> 56) & 0xFF);
        data[pos + 1] = (byte)((val >> 48) & 0xFF);
        data[pos + 2] = (byte)((val >> 40) & 0xFF);
        data[pos + 3] = (byte)((val >> 32) & 0xFF);
        data[pos + 4] = (byte)((val >> 24) & 0xFF);
        data[pos + 5] = (byte)((val >> 16) & 0xFF);
        data[pos + 6] = (byte)((val >> 8) & 0xFF);
        data[pos + 7] = (byte)(val & 0xFF);
    }

    private static string ReadAscii(byte[] data, int pos, int length)
    {
        return Encoding.ASCII.GetString(data, pos, length);
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

    /// <summary>Checks whether top-level 'moov' box precedes 'mdat' box in the MP4 container.</summary>
    public static bool HasMoovBeforeMdat(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        long moovPos = -1;
        long mdatPos = -1;
        long pos = 0;
        long len = fs.Length;
        var hdr = new byte[8];

        while (pos + 8 <= len)
        {
            fs.Seek(pos, SeekOrigin.Begin);
            if (fs.Read(hdr, 0, 8) < 8) break;

            uint boxSize = (uint)((hdr[0] << 24) | (hdr[1] << 16) | (hdr[2] << 8) | hdr[3]);
            string type = Encoding.ASCII.GetString(hdr, 4, 4);

            if (type == "moov" && moovPos < 0) moovPos = pos;
            if (type == "mdat" && mdatPos < 0) mdatPos = pos;

            if (boxSize == 0) break;
            if (boxSize == 1)
            {
                var ext = new byte[8];
                if (fs.Read(ext, 0, 8) < 8) break;
                ulong largeSize = ((ulong)ext[0] << 56) | ((ulong)ext[1] << 48) | ((ulong)ext[2] << 40) | ((ulong)ext[3] << 32) |
                                  ((ulong)ext[4] << 24) | ((ulong)ext[5] << 16) | ((ulong)ext[6] << 8) | ext[7];
                pos += (long)largeSize;
            }
            else
            {
                pos += boxSize;
            }
        }

        return moovPos >= 0 && mdatPos >= 0 && moovPos < mdatPos;
    }

    /// <summary>Validates that remux output preserves streams, duration (within tolerance), and FastStart order.</summary>
    public static bool ValidateRemux(string takePath, string remuxPath, double durationToleranceSec = 0.1)
    {
        if (!HasMoovBeforeMdat(remuxPath)) return false;

        try
        {
            using var takeReader = MediaFactory.MFCreateSourceReaderFromURL(takePath, null);
            using var remuxReader = MediaFactory.MFCreateSourceReaderFromURL(remuxPath, null);

            var takeStreams = GetStreamIndices(takeReader);
            var remuxStreams = GetStreamIndices(remuxReader);

            if (takeStreams.Count != remuxStreams.Count || remuxStreams.Count == 0) return false;

            double takeDur = GetDuration(takeReader);
            double remuxDur = GetDuration(remuxReader);

            return Math.Abs(takeDur - remuxDur) <= durationToleranceSec;
        }
        catch
        {
            return false;
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
