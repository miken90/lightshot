using System;
using System.IO;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.MediaFoundation;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class MfFragmentedWriterTests : IDisposable
{
    private readonly string _tempDir;

    public MfFragmentedWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lightshot_mf_writer_test_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    [Unit]
    public void StripsOnlyValidTrailingMfra()
    {
        // Case 1: No mfra box (e.g. random payload or normal mdat)
        string pathNoMfra = Path.Combine(_tempDir, "case1_no_mfra.bin");
        byte[] payload1 = new byte[64];
        new Random(42).NextBytes(payload1);
        File.WriteAllBytes(pathNoMfra, payload1);

        bool stripped1 = MfFragmentedWriter.StripRandomAccessIndex(pathNoMfra);
        Assert.False(stripped1, "Strip should return false when no mfro/mfra box exists.");
        Assert.Equal(payload1, File.ReadAllBytes(pathNoMfra));

        // Case 2: mfro tag with out-of-range size (> file length or < 16)
        string pathOutOfRange = Path.Combine(_tempDir, "case2_out_of_range.bin");
        byte[] payload2 = new byte[64];
        byte[] invalidMfro = new byte[16];
        // mfro box size = 16
        invalidMfro[0] = 0; invalidMfro[1] = 0; invalidMfro[2] = 0; invalidMfro[3] = 16;
        // 'mfro'
        invalidMfro[4] = (byte)'m'; invalidMfro[5] = (byte)'f'; invalidMfro[6] = (byte)'r'; invalidMfro[7] = (byte)'o';
        // flags
        invalidMfro[8] = 0; invalidMfro[9] = 0; invalidMfro[10] = 0; invalidMfro[11] = 0;
        // mfraSize = 1000 (exceeds file length 80)
        invalidMfro[12] = 0; invalidMfro[13] = 0; invalidMfro[14] = 0x03; invalidMfro[15] = 0xE8;

        using (var fs = new FileStream(pathOutOfRange, FileMode.Create, FileAccess.Write))
        {
            fs.Write(payload2);
            fs.Write(invalidMfro);
        }
        long lenBefore2 = new FileInfo(pathOutOfRange).Length;
        bool stripped2 = MfFragmentedWriter.StripRandomAccessIndex(pathOutOfRange);
        Assert.False(stripped2, "Strip should return false when declared mfra size is out of range.");
        Assert.Equal(lenBefore2, new FileInfo(pathOutOfRange).Length);

        // Case 3: Mismatched sizes (mfro says mfra is 32 bytes, but mfra box header says 48 bytes)
        string pathMismatched = Path.Combine(_tempDir, "case3_mismatched.bin");
        byte[] payload3 = new byte[64];
        byte[] mfraHeaderMismatched = new byte[16];
        // declared mfra header size = 48
        mfraHeaderMismatched[0] = 0; mfraHeaderMismatched[1] = 0; mfraHeaderMismatched[2] = 0; mfraHeaderMismatched[3] = 48;
        // 'mfra'
        mfraHeaderMismatched[4] = (byte)'m'; mfraHeaderMismatched[5] = (byte)'f'; mfraHeaderMismatched[6] = (byte)'r'; mfraHeaderMismatched[7] = (byte)'a';

        byte[] mfroBox3 = new byte[16];
        // mfro size = 16
        mfroBox3[0] = 0; mfroBox3[1] = 0; mfroBox3[2] = 0; mfroBox3[3] = 16;
        mfroBox3[4] = (byte)'m'; mfroBox3[5] = (byte)'f'; mfroBox3[6] = (byte)'r'; mfroBox3[7] = (byte)'o';
        // mfraSize in mfro = 32 (disagrees with 48)
        mfroBox3[12] = 0; mfroBox3[13] = 0; mfroBox3[14] = 0; mfroBox3[15] = 32;

        using (var fs = new FileStream(pathMismatched, FileMode.Create, FileAccess.Write))
        {
            fs.Write(payload3);
            fs.Write(mfraHeaderMismatched);
            fs.Write(mfroBox3);
        }
        long lenBefore3 = new FileInfo(pathMismatched).Length;
        bool stripped3 = MfFragmentedWriter.StripRandomAccessIndex(pathMismatched);
        Assert.False(stripped3, "Strip should return false when mfra header size disagrees with mfro.");
        Assert.Equal(lenBefore3, new FileInfo(pathMismatched).Length);

        // Case 4: Valid pair: payload (64 bytes) + valid mfra (32 bytes total)
        string pathValid = Path.Combine(_tempDir, "case4_valid.bin");
        byte[] payload4 = new byte[64];
        new Random(99).NextBytes(payload4);

        byte[] validMfra = new byte[32];
        // mfra size = 32
        validMfra[0] = 0; validMfra[1] = 0; validMfra[2] = 0; validMfra[3] = 32;
        validMfra[4] = (byte)'m'; validMfra[5] = (byte)'f'; validMfra[6] = (byte)'r'; validMfra[7] = (byte)'a';

        // trailing mfro inside mfra: last 16 bytes of the 32 bytes
        validMfra[16] = 0; validMfra[17] = 0; validMfra[18] = 0; validMfra[19] = 16;
        validMfra[20] = (byte)'m'; validMfra[21] = (byte)'f'; validMfra[22] = (byte)'r'; validMfra[23] = (byte)'o';
        validMfra[28] = 0; validMfra[29] = 0; validMfra[30] = 0; validMfra[31] = 32; // matches mfra size 32

        using (var fs = new FileStream(pathValid, FileMode.Create, FileAccess.Write))
        {
            fs.Write(payload4);
            fs.Write(validMfra);
        }
        Assert.Equal(96, new FileInfo(pathValid).Length);

        bool stripped4 = MfFragmentedWriter.StripRandomAccessIndex(pathValid);
        Assert.True(stripped4, "Strip should return true for valid mfro/mfra pair.");
        Assert.Equal(64, new FileInfo(pathValid).Length);
        Assert.Equal(payload4, File.ReadAllBytes(pathValid));
    }

    [Fact]
    [Media]
    public void WritesPlayableFragmentedMp4()
    {
        string outputPath = Path.Combine(_tempDir, "test_take.frag.mp4");

        // Create D3D11 hardware or WARP device
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

        using (device)
        {
            int width = 1280;
            int height = 720;
            int fps = 30;

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

            using var nv12Tex = device!.CreateTexture2D(texDesc);
            using var writer = new MfFragmentedWriter(
                outputPath,
                device,
                width: width,
                height: height,
                fps: fps,
                audioTrackCount: 2,
                useHevc: false,
                bitRate: 4_000_000
            );

            // Write 1 second of video frames (30 frames at ~33.3ms intervals)
            long frameDurationHns = 10_000_000 / fps;
            for (int i = 0; i < fps; i++)
            {
                long sampleTimeHns = i * frameDurationHns;
                writer.WriteVideoFrame(nv12Tex, sampleTimeHns, frameDurationHns);
            }

            // Write 1 second of audio samples (PCM 48kHz, 16-bit, stereo: 48000 * 4 = 192000 bytes/sec)
            // Send in 20ms chunks (960 samples = 3840 bytes)
            byte[] silenceChunk = new byte[3840];
            long audioChunkDurationHns = 200_000; // 20 ms in HNS
            for (int i = 0; i < 50; i++)
            {
                long audioTimeHns = i * audioChunkDurationHns;
                writer.WriteAudioSample(writer.MicStreamIndex, silenceChunk, audioTimeHns, audioChunkDurationHns);
                writer.WriteAudioSample(writer.LoopbackStreamIndex, silenceChunk, audioTimeHns, audioChunkDurationHns);
            }

            writer.FinalizeWriting(stripMfra: true);
        }

        Assert.True(File.Exists(outputPath));
        Assert.True(new FileInfo(outputPath).Length > 10_000);

        // Validate playability by decoding with IMFSourceReader to EOF
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(outputPath, null);
        reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

        int videoFramesDecoded = 0;
        while (true)
        {
            var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out _);
            if (flags.HasFlag(SourceReaderFlag.EndOfStream)) break;
            if (sample != null)
            {
                videoFramesDecoded++;
                sample.Dispose();
            }
        }

        Assert.True(videoFramesDecoded > 0, "Fragmented MP4 must be decodable by Media Foundation SourceReader.");
    }
}
