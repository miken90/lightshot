using System;
using System.Collections.Generic;
using System.IO;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class Mp4RemuxerTests : IDisposable
{
    private readonly string _tempDir;

    public Mp4RemuxerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lightshot_remuxer_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        MediaFactory.MFStartup().CheckError();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }



    private static ID3D11Device CreateD3DDevice()
    {
        var result = D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
            out ID3D11Device? device);

        if (result.Failure || device == null)
        {
            D3D11.D3D11CreateDevice(
                null,
                DriverType.Warp,
                DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                out device).CheckError();
        }

        return device!;
    }

    /// <summary>
    /// Generates a synthetic 4-second recording take with 1 H.264 video stream and 2 AAC audio streams
    /// spanning 2 full GOPs (GOP = 2 s).
    /// Returns (strippedTakePath, unstrippedTakePath).
    /// </summary>
    private (string strippedPath, string unstrippedPath) CreateSyntheticTake()
    {
        string rawTakePath = Path.Combine(_tempDir, "synthetic_take.frag.mp4");
        string unstrippedCopyPath = Path.Combine(_tempDir, "synthetic_take_unstripped.frag.mp4");

        using var device = CreateD3DDevice();

        int width = 1280;
        int height = 720;
        int fps = 30;
        int totalFrames = 120; // 4.0 seconds at 30 fps

        var texDesc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.NV12,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.None
        };

        using var nv12Tex = device.CreateTexture2D(texDesc);
        using var writer = new MfFragmentedWriter(
            rawTakePath,
            device,
            width: width,
            height: height,
            fps: fps,
            audioTrackCount: 2,
            useHevc: false,
            bitRate: 4_000_000
        );

        long frameDurationHns = 10_000_000 / fps;
        for (int i = 0; i < totalFrames; i++)
        {
            long sampleTimeHns = i * frameDurationHns;
            writer.WriteVideoFrame(nv12Tex, sampleTimeHns, frameDurationHns);
        }

        // 2 AAC streams (Mic: 440 Hz, Loopback: 660 Hz), 4.0 seconds = 200 chunks of 20 ms
        int sampleRate = 48000;
        int chunkSamples = 960; // 20 ms
        long audioChunkDurationHns = 200_000;

        byte[] micChunk = new byte[chunkSamples * 4];
        byte[] loopbackChunk = new byte[chunkSamples * 4];

        for (int chunk = 0; chunk < 200; chunk++)
        {
            long audioTimeHns = chunk * audioChunkDurationHns;

            // Generate tone PCM
            for (int s = 0; s < chunkSamples; s++)
            {
                double t = (chunk * chunkSamples + s) / (double)sampleRate;
                short micVal = (short)(Math.Sin(2.0 * Math.PI * 440.0 * t) * 16000.0);
                short loopVal = (short)(Math.Sin(2.0 * Math.PI * 660.0 * t) * 16000.0);

                // Stereo 16-bit
                micChunk[4 * s + 0] = (byte)(micVal & 0xFF);
                micChunk[4 * s + 1] = (byte)((micVal >> 8) & 0xFF);
                micChunk[4 * s + 2] = (byte)(micVal & 0xFF);
                micChunk[4 * s + 3] = (byte)((micVal >> 8) & 0xFF);

                loopbackChunk[4 * s + 0] = (byte)(loopVal & 0xFF);
                loopbackChunk[4 * s + 1] = (byte)((loopVal >> 8) & 0xFF);
                loopbackChunk[4 * s + 2] = (byte)(loopVal & 0xFF);
                loopbackChunk[4 * s + 3] = (byte)((loopVal >> 8) & 0xFF);
            }

            writer.WriteAudioSample(writer.MicStreamIndex, micChunk, audioTimeHns, audioChunkDurationHns);
            writer.WriteAudioSample(writer.LoopbackStreamIndex, loopbackChunk, audioTimeHns, audioChunkDurationHns);
        }

        // Finalize writing without stripping to save the unstripped take copy
        writer.FinalizeWriting(stripMfra: false);

        // Copy unstripped take for negative control
        File.Copy(rawTakePath, unstrippedCopyPath, overwrite: true);

        // Strip the raw take
        MfFragmentedWriter.StripRandomAccessIndex(rawTakePath);

        return (rawTakePath, unstrippedCopyPath);
    }

    [Fact]
    [Media]
    public void OutputHasMoovBeforeMdatAndAllStreams()
    {
        var (strippedTake, _) = CreateSyntheticTake();
        string remuxedPath = Path.Combine(_tempDir, "remuxed_progressive.mp4");

        var remuxer = new Mp4Remuxer();
        var remuxResult = remuxer.Remux(strippedTake, remuxedPath);

        Assert.True(remuxResult.Success, $"Remuxing failed: {remuxResult.ErrorMessage}");
        Assert.Equal(3, remuxResult.StreamCount);
        Assert.True(remuxResult.DurationSeconds >= 3.9, $"Duration was {remuxResult.DurationSeconds}s");

        // 1. Box order verification: 'moov' before 'mdat'
        bool hasMoovBeforeMdat = Mp4Remuxer.HasMoovBeforeMdat(remuxedPath);
        Assert.True(hasMoovBeforeMdat, "Remuxed file must have 'moov' box placed before 'mdat'.");

        // 2. Validate all streams present (1 video + 2 audio) and duration match
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(remuxedPath, null);
        int videoCount = 0;
        int audioCount = 0;

        for (int i = 0; i < 8; i++)
        {
            try
            {
                using var nt = reader.GetNativeMediaType((SourceReaderIndex)i, 0);
                Guid major = nt.GetGUID(MediaTypeAttributeKeys.MajorType);
                if (major == MediaTypeGuids.Video) videoCount++;
                else if (major == MediaTypeGuids.Audio) audioCount++;
            }
            catch { break; }
        }

        Assert.Equal(1, videoCount);
        Assert.Equal(2, audioCount);

        bool isValid = Mp4Remuxer.ValidateRemux(strippedTake, remuxedPath, durationToleranceSec: 0.1);
        Assert.True(isValid, "Remuxed file must pass stream and duration validation against take.");
    }

    [Fact]
    [Media]
    public void DecodedTimestampsNeverStepBackward()
    {
        var (strippedTake, unstrippedTake) = CreateSyntheticTake();
        string remuxedPath = Path.Combine(_tempDir, "remuxed_timestamps.mp4");

        var remuxer = new Mp4Remuxer();
        var remuxResult = remuxer.Remux(strippedTake, remuxedPath);
        Assert.True(remuxResult.Success, remuxResult.ErrorMessage);

        // 1. Check decoded timestamps of remuxed progressive MP4
        using var remuxReader = MediaFactory.MFCreateSourceReaderFromURL(remuxedPath, null);
        remuxReader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        remuxReader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

        long lastRemuxTs = -1;
        int remuxFrames = 0;
        bool remuxSteppedBackward = false;

        while (true)
        {
            var sample = remuxReader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out long ts);
            if (flags.HasFlag(SourceReaderFlag.EndOfStream)) break;
            if (sample != null)
            {
                remuxFrames++;
                if (ts < lastRemuxTs)
                {
                    remuxSteppedBackward = true;
                }
                lastRemuxTs = ts;
                sample.Dispose();
            }
        }

        Assert.True(remuxFrames >= 100, $"Expected >= 100 frames, decoded {remuxFrames}");
        Assert.False(remuxSteppedBackward, "Decoded timestamps in remuxed MP4 must never step backward.");

        // 2. Negative control: Read unstripped take and check if backward jumps occur
        using var unstrippedReader = MediaFactory.MFCreateSourceReaderFromURL(unstrippedTake, null);
        unstrippedReader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        unstrippedReader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

        long lastUnstrippedTs = -1;
        int unstrippedFrames = 0;
        int unstrippedBackwardJumps = 0;

        while (true)
        {
            var sample = unstrippedReader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out long ts);
            if (flags.HasFlag(SourceReaderFlag.EndOfStream)) break;
            if (sample != null)
            {
                unstrippedFrames++;
                if (ts < lastUnstrippedTs)
                {
                    unstrippedBackwardJumps++;
                }
                lastUnstrippedTs = ts;
                sample.Dispose();
            }
        }

        // Gate amendment 3 reporting requirement:
        // "DecodedTimestampsNeverStepBackward gets a negative control: read the unstripped take too.
        // If the backward jumps do not reproduce on synthetic input, report that plainly; never tune the input to make it pass."
        Console.WriteLine($"[Negative Control] Unstripped take decoded {unstrippedFrames} frames, backward jumps: {unstrippedBackwardJumps}");
        // We assert unstripped frames were read, but we never fail if backward jumps don't reproduce on synthetic input
        Assert.True(unstrippedFrames > 0, "Unstripped take must be readable.");
    }

    [Fact]
    [Unit]
    public void PatchChunkOffsetsWalksBoxHierarchyAndThrowsOn32BitOverflow()
    {
        // 1. Construct minimal moov with trak > mdia > minf > stbl > stco
        uint nearMax = uint.MaxValue - 10;
        byte[] stco = MakeBox("stco", Combine(new byte[] { 0, 0, 0, 0 }, GetUInt32BE(1), GetUInt32BE(nearMax)));
        byte[] stbl = MakeBox("stbl", stco);
        byte[] minf = MakeBox("minf", stbl);
        byte[] mdia = MakeBox("mdia", minf);
        byte[] trak = MakeBox("trak", mdia);

        // Also add a sibling box "udta" containing fake "stco" pattern to verify hierarchy walking
        byte[] fakeStco = System.Text.Encoding.ASCII.GetBytes("stco_fake_payload_not_in_stbl");
        byte[] udta = MakeBox("udta", fakeStco);

        byte[] moov = MakeBox("moov", Combine(trak, udta));

        // Safe shift (5): nearMax + 5 <= uint.MaxValue -> should succeed
        byte[] moovSafe = (byte[])moov.Clone();
        Mp4Remuxer.PatchChunkOffsets(moovSafe, 5);

        int stcoEntryPos = FindStcoEntryPos(moovSafe);
        uint patchedVal = ReadUInt32BE(moovSafe, stcoEntryPos);
        Assert.Equal(nearMax + 5, patchedVal);

        // Verify fake stco in udta was untouched
        string udtaContent = System.Text.Encoding.ASCII.GetString(moovSafe, moovSafe.Length - fakeStco.Length, fakeStco.Length);
        Assert.Equal("stco_fake_payload_not_in_stbl", udtaContent);

        // Overflow shift (20): nearMax + 20 > uint.MaxValue -> must throw OverflowException
        var ex = Assert.Throws<OverflowException>(() => Mp4Remuxer.PatchChunkOffsets(moov, 20));
        Assert.Contains("exceeds uint.MaxValue", ex.Message);

        // 2. Verify 64-bit co64 handles offsets beyond 4GB
        ulong largeOffset = 0x1_0000_0000UL;
        byte[] co64 = MakeBox("co64", Combine(new byte[] { 0, 0, 0, 0 }, GetUInt32BE(1), GetUInt64BE(largeOffset)));
        byte[] moov64 = MakeBox("moov", MakeBox("trak", MakeBox("mdia", MakeBox("minf", MakeBox("stbl", co64)))));
        Mp4Remuxer.PatchChunkOffsets(moov64, 100);
        int co64EntryPos = FindCo64EntryPos(moov64);
        ulong patched64 = ReadUInt64BE(moov64, co64EntryPos);
        Assert.Equal(largeOffset + 100, patched64);
    }

    private static byte[] MakeBox(string tag, byte[] payload)
    {
        uint size = (uint)(8 + payload.Length);
        byte[] box = new byte[size];
        box[0] = (byte)((size >> 24) & 0xFF);
        box[1] = (byte)((size >> 16) & 0xFF);
        box[2] = (byte)((size >> 8) & 0xFF);
        box[3] = (byte)(size & 0xFF);
        System.Text.Encoding.ASCII.GetBytes(tag, 0, 4, box, 4);
        Buffer.BlockCopy(payload, 0, box, 8, payload.Length);
        return box;
    }

    private static byte[] Combine(params byte[][] arrays)
    {
        int total = arrays.Sum(a => a.Length);
        byte[] result = new byte[total];
        int offset = 0;
        foreach (var arr in arrays)
        {
            Buffer.BlockCopy(arr, 0, result, offset, arr.Length);
            offset += arr.Length;
        }
        return result;
    }

    private static byte[] GetUInt32BE(uint val)
    {
        return new byte[] { (byte)((val >> 24) & 0xFF), (byte)((val >> 16) & 0xFF), (byte)((val >> 8) & 0xFF), (byte)(val & 0xFF) };
    }

    private static byte[] GetUInt64BE(ulong val)
    {
        return new byte[]
        {
            (byte)((val >> 56) & 0xFF), (byte)((val >> 48) & 0xFF), (byte)((val >> 40) & 0xFF), (byte)((val >> 32) & 0xFF),
            (byte)((val >> 24) & 0xFF), (byte)((val >> 16) & 0xFF), (byte)((val >> 8) & 0xFF), (byte)(val & 0xFF)
        };
    }

    private static int FindStcoEntryPos(byte[] data)
    {
        for (int i = 0; i < data.Length - 16; i++)
        {
            if (data[i] == 's' && data[i + 1] == 't' && data[i + 2] == 'b' && data[i + 3] == 'l')
            {
                for (int j = i; j < data.Length - 16; j++)
                {
                    if (data[j] == 's' && data[j + 1] == 't' && data[j + 2] == 'c' && data[j + 3] == 'o')
                    {
                        return j + 12; // 4 tag + 4 ver/flags + 4 count = 12
                    }
                }
            }
        }
        throw new InvalidOperationException("stco not found");
    }

    private static int FindCo64EntryPos(byte[] data)
    {
        for (int i = 0; i < data.Length - 16; i++)
        {
            if (data[i] == 's' && data[i + 1] == 't' && data[i + 2] == 'b' && data[i + 3] == 'l')
            {
                for (int j = i; j < data.Length - 16; j++)
                {
                    if (data[j] == 'c' && data[j + 1] == 'o' && data[j + 2] == '6' && data[j + 3] == '4')
                    {
                        return j + 12; // 4 tag + 4 ver/flags + 4 count = 12
                    }
                }
            }
        }
        throw new InvalidOperationException("co64 not found");
    }

    private static uint ReadUInt32BE(byte[] data, int pos)
    {
        return (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);
    }

    private static ulong ReadUInt64BE(byte[] data, int pos)
    {
        return ((ulong)data[pos] << 56) | ((ulong)data[pos + 1] << 48) | ((ulong)data[pos + 2] << 40) | ((ulong)data[pos + 3] << 32) |
               ((ulong)data[pos + 4] << 24) | ((ulong)data[pos + 5] << 16) | ((ulong)data[pos + 6] << 8) | (ulong)data[pos + 7];
    }
}
