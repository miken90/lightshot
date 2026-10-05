using System;
using System.IO;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace RecordingProbe;

public sealed class MfFragmentedWriter : IDisposable
{
    private readonly IMFDXGIDeviceManager _dxgiManager;
    private readonly IMFSinkWriter _writer;
    private readonly int _videoStreamIndex;
    private readonly int _micStreamIndex;
    private readonly int _loopbackStreamIndex;
    private readonly int _width;
    private readonly int _height;
    private readonly int _nv12ByteSize;
    private bool _writingStarted;
    private bool _finalized;
    private bool _disposed;

    public int VideoStreamIndex => _videoStreamIndex;
    public int MicStreamIndex => _micStreamIndex;
    public int LoopbackStreamIndex => _loopbackStreamIndex;

    public MfFragmentedWriter(
        string outputPath,
        ID3D11Device d3dDevice,
        int width = 2560,
        int height = 1440,
        int fps = 60,
        bool useHevc = false)
    {
        _width = width;
        _height = height;
        _nv12ByteSize = width * height * 3 / 2;

        if (File.Exists(outputPath)) File.Delete(outputPath);

        _dxgiManager = MediaFactory.MFCreateDXGIDeviceManager();
        _dxgiManager.ResetDevice(d3dDevice).CheckError();

        using var attrs = MediaFactory.MFCreateAttributes(6);
        attrs.Set(SinkWriterAttributeKeys.LowLatency, true);
        attrs.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, true);
        attrs.Set(SinkWriterAttributeKeys.D3DManager, _dxgiManager);
        attrs.Set(TranscodeAttributeKeys.TranscodeContainertype, TranscodeContainerTypeGuids.Fmpeg4);

        _writer = MediaFactory.MFCreateSinkWriterFromURL(outputPath, null, attrs);

        // 1. Video Stream
        var videoSubtype = useHevc ? VideoFormatGuids.Hevc : VideoFormatGuids.H264;
        using var vOutType = MediaFactory.MFCreateMediaType();
        vOutType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        vOutType.Set(MediaTypeAttributeKeys.Subtype, videoSubtype);
        vOutType.Set(MediaTypeAttributeKeys.AvgBitrate, 12_000_000);
        vOutType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
        MediaFactory.MFSetAttributeSize(vOutType, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height);
        MediaFactory.MFSetAttributeRatio(vOutType, MediaTypeAttributeKeys.FrameRate, (uint)fps, 1);
        MediaFactory.MFSetAttributeRatio(vOutType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
        _videoStreamIndex = _writer.AddStream(vOutType);

        using var vInType = MediaFactory.MFCreateMediaType();
        vInType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        vInType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
        vInType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
        MediaFactory.MFSetAttributeSize(vInType, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height);
        MediaFactory.MFSetAttributeRatio(vInType, MediaTypeAttributeKeys.FrameRate, (uint)fps, 1);
        MediaFactory.MFSetAttributeRatio(vInType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
        _writer.SetInputMediaType(_videoStreamIndex, vInType, null);

        // 2. Audio Track 1: Mic (AAC 48kHz, stereo, 128kbps)
        _micStreamIndex = ConfigureAudioStream(48000, 2, 128_000);

        // 3. Audio Track 2: Loopback (AAC 48kHz, stereo, 128kbps)
        _loopbackStreamIndex = ConfigureAudioStream(48000, 2, 128_000);

        _writer.BeginWriting();
        _writingStarted = true;
    }

    /// <summary>Encoder MFT the sink writer actually instantiated for the video stream (read after BeginWriting).</summary>
    public string EncoderTransformName
    {
        get
        {
            try
            {
                using var ex = _writer.QueryInterface<IMFSinkWriterEx>();
                ex.GetTransformForStream(_videoStreamIndex, 0, out Guid category, out IMFTransform? transform);
                using (transform)
                {
                    if (transform == null) return "unknown (no transform returned)";
                    string hw = "?";
                    try { hw = transform.Attributes.GetString(TransformAttributeKeys.MftEnumHardwareUrlAttribute); } catch { hw = "(no hardware url attribute)"; }
                    string name = "?";
                    try { name = transform.Attributes.GetString(TransformAttributeKeys.MftFriendlyNameAttribute); } catch { }
                    return $"friendlyName={name}; hardwareUrl={hw}";
                }
            }
            catch (Exception ex)
            {
                return $"unavailable ({ex.GetType().Name}: {ex.Message})";
            }
        }
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

    public void WriteVideoFrame(ID3D11Texture2D nv12Texture, long sampleTimeHns, long durationHns)
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

    public void WriteAudioSample(int streamIndex, byte[] pcmData, long sampleTimeHns, long durationHns)
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

    public void FinalizeWriting()
    {
        if (_finalized) return;
        _finalized = true;
        _writer.Finalize();
    }

    public static (string h264HwName, string hevcHwName) GetHardwareEncoders()
    {
        string h264 = "None";
        string hevc = "None";

        try
        {
            using var activates = MediaFactory.MFTEnumEx(
                TransformCategoryGuids.VideoEncoder,
                (uint)EnumFlag.EnumFlagAll,
                null,
                null);

            foreach (var act in activates)
            {
                string name = "";
                try { name = act.GetString(TransformAttributeKeys.MftFriendlyNameAttribute); } catch { }
                bool isHw = false;
                try
                {
                    string hwUrl = act.GetString(TransformAttributeKeys.MftEnumHardwareUrlAttribute);
                    isHw = !string.IsNullOrEmpty(hwUrl);
                }
                catch { }

                if (isHw)
                {
                    if (name.Contains("H.264", StringComparison.OrdinalIgnoreCase) || name.Contains("H264", StringComparison.OrdinalIgnoreCase) || name.Contains("AVC", StringComparison.OrdinalIgnoreCase))
                    {
                        if (h264 == "None" || name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                            h264 = name;
                    }
                    if (name.Contains("HEVC", StringComparison.OrdinalIgnoreCase) || name.Contains("H.265", StringComparison.OrdinalIgnoreCase))
                    {
                        if (hevc == "None" || name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                            hevc = name;
                    }
                }
            }
        }
        catch { }

        return (h264, hevc);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_finalized)
        {
            try { _writer.Finalize(); } catch { }
        }

        _writer.Dispose();
        _dxgiManager.Dispose();
    }
}
