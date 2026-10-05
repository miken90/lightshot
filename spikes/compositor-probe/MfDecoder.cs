using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace CompositorProbe;

/// <summary>One decoded frame still living in the decoder's D3D11 texture pool (NV12 array slice).</summary>
public sealed class DecodedFrame : IDisposable
{
    public IMFSample Sample { get; }
    public ID3D11Texture2D Texture { get; }
    public uint Slice { get; }
    public long PtsHns { get; }
    public int Index => (int)Math.Round(PtsHns / (10_000_000.0 / TestClipWriter.Fps));
    public long SeekId { get; init; }

    public DecodedFrame(IMFSample sample, ID3D11Texture2D texture, uint slice, long ptsHns)
    {
        Sample = sample; Texture = texture; Slice = slice; PtsHns = ptsHns;
    }

    public void Dispose()
    {
        Texture.Dispose();
        Sample.Dispose();
    }
}

/// <summary>
/// Media Foundation source reader with the D3D11 device manager attached, so the hardware decoder writes into
/// D3D11 textures. Frame-accurate seek: SetCurrentPosition lands on the preceding keyframe, the reader then
/// decodes forward to the requested frame (those intermediate frames are part of the seek latency).
/// </summary>
public sealed class MfDecoder : IDisposable
{
    private readonly IMFDXGIDeviceManager _manager;
    private readonly IMFSourceReader _reader;
    public int Width { get; }
    public int Height { get; }
    public long DurationHns { get; }
    public bool EndOfStream { get; private set; }
    // Timing of the most recent SeekTo, kept so a seek result can say where the time went.
    public double LastSetPositionMs, LastFirstFrameMs, LastDecodeMs;
    public int LastDecodedFrames;

    public MfDecoder(ID3D11Device device, string path)
    {
        _manager = MediaFactory.MFCreateDXGIDeviceManager();
        _manager.ResetDevice(device).CheckError();

        using var attrs = MediaFactory.MFCreateAttributes(4);
        attrs.Set(SourceReaderAttributeKeys.D3DManager, _manager);
        attrs.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, false);
        attrs.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, true);
        attrs.Set(SourceReaderAttributeKeys.DisableDxva, false);
        // MF_LOW_LATENCY: the decoder emits each frame as soon as it is decoded instead of holding a reorder queue.
        attrs.Set(new Guid("9c27891a-ed7a-40e1-88e8-b22727a024ee"), 1u);
        _reader = MediaFactory.MFCreateSourceReaderFromURL(path, attrs);

        using var type = MediaFactory.MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
        _reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, type);

        using var actual = _reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
        MediaFactory.MFGetAttributeSize(actual, MediaTypeAttributeKeys.FrameSize, out uint w, out uint h);
        Width = (int)w; Height = (int)h;
        DurationHns = Convert.ToInt64(_reader.GetPresentationAttribute(SourceReaderIndex.MediaSource, PresentationDescriptionAttributeKeys.Duration).Value);
    }

    /// <summary>Next frame in decode order, or null at end of stream.</summary>
    public DecodedFrame? ReadNext(long seekId = 0)
    {
        while (true)
        {
            var sample = _reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None,
                out _, out SourceReaderFlag flags, out long ts);
            if ((flags & SourceReaderFlag.EndOfStream) != 0) { EndOfStream = true; sample?.Dispose(); return null; }
            if (sample == null) continue;
            using var buf = sample.GetBufferByIndex(0);
            using var dxgi = buf.QueryInterface<IMFDXGIBuffer>();
            var tex = new ID3D11Texture2D(dxgi.GetResource(typeof(ID3D11Texture2D).GUID));
            return new DecodedFrame(sample, tex, dxgi.SubresourceIndex, ts) { SeekId = seekId };
        }
    }

    /// <summary>Seeks and returns the exact requested frame (pts == frame * 1/60 s), decoding forward from the keyframe.</summary>
    public DecodedFrame? SeekTo(int frameIndex, long seekId)
    {
        long target = (long)Math.Round(frameIndex * 10_000_000.0 / TestClipWriter.Fps);
        long q0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _reader.SetCurrentPosition(target);
        LastSetPositionMs = (System.Diagnostics.Stopwatch.GetTimestamp() - q0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        EndOfStream = false;
        long half = 10_000_000L / TestClipWriter.Fps / 2;
        int n = 0; double firstMs = 0;
        while (true)
        {
            var f = ReadNext(seekId);
            if (f == null) return null;
            if (n++ == 0) firstMs = (System.Diagnostics.Stopwatch.GetTimestamp() - q0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (f.PtsHns + half >= target)
            {
                LastFirstFrameMs = firstMs; LastDecodedFrames = n;
                LastDecodeMs = (System.Diagnostics.Stopwatch.GetTimestamp() - q0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                return f;
            }
            f.Dispose();
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
        _manager.Dispose();
    }
}
