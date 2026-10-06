using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Lightshot.Platform.Windows.Audio;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace Lightshot.Platform.Windows.Recording;

/// <summary>
/// Fragmented MP4 sink writer with D3D11 NV12 hardware encoding, per-stream AAC,
/// and trailing random access index (mfra) stripping.
/// </summary>
public sealed class MfFragmentedWriter : IDisposable
{
    private readonly IMFDXGIDeviceManager _dxgiManager;
    private readonly IMFSinkWriter _writer;
    private readonly int _videoStreamIndex;
    private readonly List<int> _audioStreamIndices = new();
    private readonly int _width;
    private readonly int _height;
    private readonly int _nv12ByteSize;
    private readonly string _outputPath;
    private bool _writingStarted;
    private bool _finalized;
    private bool _disposed;

    public int VideoStreamIndex => _videoStreamIndex;
    public IReadOnlyList<int> AudioStreamIndices => _audioStreamIndices;
    public int MicStreamIndex => _audioStreamIndices.Count > 0 ? _audioStreamIndices[0] : -1;
    public int LoopbackStreamIndex => _audioStreamIndices.Count > 1 ? _audioStreamIndices[1] : -1;

    public MfFragmentedWriter(
        string outputPath,
        ID3D11Device d3dDevice,
        int width = 1920,
        int height = 1080,
        int fps = 60,
        int audioTrackCount = 2,
        bool useHevc = false,
        int bitRate = 12_000_000,
        bool forceSoftware = false,
        IReadOnlyList<AudioTrackConfig>? audioTrackConfigs = null)
    {
        _outputPath = outputPath;
        _width = width;
        _height = height;
        _nv12ByteSize = width * height * 3 / 2;

        if (File.Exists(outputPath)) File.Delete(outputPath);

        _dxgiManager = MediaFactory.MFCreateDXGIDeviceManager();
        _dxgiManager.ResetDevice(d3dDevice).CheckError();

        using var attrs = MediaFactory.MFCreateAttributes(6);
        attrs.Set(SinkWriterAttributeKeys.LowLatency, true);
        attrs.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, !forceSoftware);
        attrs.Set(SinkWriterAttributeKeys.D3DManager, _dxgiManager);
        attrs.Set(TranscodeAttributeKeys.TranscodeContainertype, TranscodeContainerTypeGuids.Fmpeg4);

        _writer = MediaFactory.MFCreateSinkWriterFromURL(outputPath, null, attrs);

        // 1. Video Stream Configuration (GOP 2 s)
        var videoSubtype = useHevc ? VideoFormatGuids.Hevc : VideoFormatGuids.H264;
        using var vOutType = MediaFactory.MFCreateMediaType();
        vOutType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        vOutType.Set(MediaTypeAttributeKeys.Subtype, videoSubtype);
        vOutType.Set(MediaTypeAttributeKeys.AvgBitrate, bitRate);
        vOutType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
        MediaFactory.MFSetAttributeSize(vOutType, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height);
        MediaFactory.MFSetAttributeRatio(vOutType, MediaTypeAttributeKeys.FrameRate, (uint)fps, 1);
        MediaFactory.MFSetAttributeRatio(vOutType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
        vOutType.Set(MediaTypeAttributeKeys.MaxKeyframeSpacing, (uint)(fps * 2));
        _videoStreamIndex = _writer.AddStream(vOutType);

        using var vInType = MediaFactory.MFCreateMediaType();
        vInType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        vInType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
        vInType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
        MediaFactory.MFSetAttributeSize(vInType, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height);
        MediaFactory.MFSetAttributeRatio(vInType, MediaTypeAttributeKeys.FrameRate, (uint)fps, 1);
        MediaFactory.MFSetAttributeRatio(vInType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
        _writer.SetInputMediaType(_videoStreamIndex, vInType, null);

        // 2. Audio Streams Configuration (AAC 48kHz, 96kbps mono or 160kbps stereo)
        if (audioTrackConfigs != null && audioTrackConfigs.Count > 0)
        {
            foreach (var cfg in audioTrackConfigs)
            {
                _audioStreamIndices.Add(ConfigureAudioStream(cfg.SampleRate, cfg.Channels, cfg.Bitrate));
            }
        }
        else
        {
            for (int i = 0; i < audioTrackCount; i++)
            {
                _audioStreamIndices.Add(ConfigureAudioStream(48000, 2, 160_000));
            }
        }

        _writer.BeginWriting();
        _writingStarted = true;
    }

    private int ConfigureAudioStream(int sampleRate, int channels, int bitrate)
    {
        using var aOutType = MediaFactory.MFCreateMediaType();
        aOutType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        aOutType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
        aOutType.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16);
        aOutType.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)sampleRate);
        aOutType.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)channels);
        aOutType.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(bitrate / 8));
        int streamIndex = _writer.AddStream(aOutType);

        using var aInType = MediaFactory.MFCreateMediaType();
        aInType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        aInType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
        aInType.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16);
        aInType.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)sampleRate);
        aInType.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)channels);
        aInType.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)(channels * 2));
        aInType.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(sampleRate * channels * 2));
        _writer.SetInputMediaType(streamIndex, aInType, null);

        return streamIndex;
    }

    private readonly object _lock = new();

    public void WriteVideoFrame(ID3D11Texture2D nv12Texture, long sampleTimeHns, long durationHns)
    {
        lock (_lock)
        {
            if (_finalized || !_writingStarted) return;
            if (sampleTimeHns < 0) sampleTimeHns = 0;

            using var buffer = MediaFactory.MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID, nv12Texture, 0, false);
            buffer.CurrentLength = _nv12ByteSize;

            using var sample = MediaFactory.MFCreateSample();
            sample.AddBuffer(buffer);
            sample.SampleTime = sampleTimeHns;
            sample.SampleDuration = durationHns;

            _writer.WriteSample(_videoStreamIndex, sample);
        }
    }

    public void WriteAudioSample(int streamIndex, byte[] pcmData, long sampleTimeHns, long durationHns)
    {
        lock (_lock)
        {
            if (_finalized || !_writingStarted || pcmData.Length == 0) return;
            if (sampleTimeHns < 0) sampleTimeHns = 0;

            using var buffer = MediaFactory.MFCreateMemoryBuffer(pcmData.Length);
            buffer.Lock(out IntPtr pData, out _, out _);
            Marshal.Copy(pcmData, 0, pData, pcmData.Length);
            buffer.Unlock();
            buffer.CurrentLength = pcmData.Length;

            using var sample = MediaFactory.MFCreateSample();
            sample.AddBuffer(buffer);
            sample.SampleTime = sampleTimeHns;
            sample.SampleDuration = durationHns;

            _writer.WriteSample(streamIndex, sample);
        }
    }

    public void FinalizeWriting(bool stripMfra = true)
    {
        lock (_lock)
        {
            if (_finalized) return;
            _finalized = true;
            _writer.Finalize();
            _writer.Dispose();
            if (stripMfra)
            {
                StripRandomAccessIndex(_outputPath);
            }
        }
    }

    /// <summary>
    /// Strips trailing mfra/mfro box pair when valid.
    /// Gate condition: mfro must be 16 bytes, tag 'mfro', size range valid,
    /// mfra box at -size must have tag 'mfra' and its own size field must match mfro size.
    /// </summary>
    public static bool StripRandomAccessIndex(string path)
    {
        if (!File.Exists(path)) return false;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        if (fs.Length < 16) return false;

        var tail = new byte[16];
        fs.Seek(-16, SeekOrigin.End);
        fs.ReadExactly(tail);

        // 1. mfro box must declare size 16
        uint mfroBoxSize = (uint)(tail[0] << 24 | tail[1] << 16 | tail[2] << 8 | tail[3]);
        if (mfroBoxSize != 16) return false;

        // 2. Tag must be 'mfro'
        if (tail[4] != 'm' || tail[5] != 'f' || tail[6] != 'r' || tail[7] != 'o') return false;

        // 3. mfra size from mfro
        long mfraSize = (uint)(tail[12] << 24 | tail[13] << 16 | tail[14] << 8 | tail[15]);
        if (mfraSize < 16 || mfraSize > fs.Length) return false;

        // 4. Inspect mfra header at (end - mfraSize)
        var head = new byte[8];
        fs.Seek(-mfraSize, SeekOrigin.End);
        fs.ReadExactly(head);

        if (head[4] != 'm' || head[5] != 'f' || head[6] != 'r' || head[7] != 'a') return false;

        uint mfraHeaderSize = (uint)(head[0] << 24 | head[1] << 16 | head[2] << 8 | head[3]);
        if (mfraHeaderSize != mfraSize) return false;

        // Valid pair: truncate exactly mfraSize bytes
        fs.SetLength(fs.Length - mfraSize);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            if (!_finalized)
            {
                try { _writer.Finalize(); } catch { }
                try { _writer.Dispose(); } catch { }
            }
        }

        _dxgiManager.Dispose();
    }
}
