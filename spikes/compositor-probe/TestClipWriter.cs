using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace CompositorProbe;

/// <summary>
/// Writes a deterministic 1440p60 H.264 MP4 (hardware encoder through the MF sink writer, as in recording-probe).
/// Every frame carries its frame number as FrameStamp cells and a moving square, so a decoded frame can be identified.
/// </summary>
public static class TestClipWriter
{
    public const int Width = 2560, Height = 1440, Fps = 60;

    public static string Write(string path, int frames)
    {
        if (File.Exists(path)) File.Delete(path);
        int ySize = Width * Height, size = ySize * 3 / 2;

        // Static planes: a diagonal luma gradient with bands (content for blur and scale to chew on) and a colour ramp.
        var yBase = new byte[ySize];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int v = 40 + (x * 90 / Width) + (y * 60 / Height) + (((x + y) / 96) % 2 == 0 ? 0 : 30);
                yBase[y * Width + x] = (byte)v;
            }
        var uvBase = new byte[ySize / 2];
        for (int y = 0; y < Height / 2; y++)
            for (int x = 0; x < Width / 2; x++)
            {
                uvBase[y * Width + x * 2] = (byte)(90 + x * 80 / (Width / 2));      // U
                uvBase[y * Width + x * 2 + 1] = (byte)(170 - y * 70 / (Height / 2)); // V
            }

        using var attrs = MediaFactory.MFCreateAttributes(4);
        attrs.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, true);
        attrs.Set(SinkWriterAttributeKeys.DisableThrottling, true);
        using var writer = MediaFactory.MFCreateSinkWriterFromURL(path, null, attrs);

        using var outType = MediaFactory.MFCreateMediaType();
        outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        outType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
        outType.Set(MediaTypeAttributeKeys.AvgBitrate, 24_000_000);
        outType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
        outType.Set(MediaTypeAttributeKeys.MaxKeyframeSpacing, 30u);
        MediaFactory.MFSetAttributeSize(outType, MediaTypeAttributeKeys.FrameSize, Width, Height);
        MediaFactory.MFSetAttributeRatio(outType, MediaTypeAttributeKeys.FrameRate, Fps, 1);
        MediaFactory.MFSetAttributeRatio(outType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
        int stream = writer.AddStream(outType);

        using var inType = MediaFactory.MFCreateMediaType();
        inType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        inType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
        inType.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
        MediaFactory.MFSetAttributeSize(inType, MediaTypeAttributeKeys.FrameSize, Width, Height);
        MediaFactory.MFSetAttributeRatio(inType, MediaTypeAttributeKeys.FrameRate, Fps, 1);
        MediaFactory.MFSetAttributeRatio(inType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1);
        // The encoder ignores the media type keyframe hint, so the GOP length goes in as an encoder property (CODECAPI_AVEncMPVGOPSize).
        using var encParams = MediaFactory.MFCreateAttributes(1);
        encParams.Set(new Guid("95f31b26-95a4-41aa-9303-246a7fc6eef1"), 30u);
        writer.SetInputMediaType(stream, inType, encParams);
        writer.BeginWriting();

        var frame = new byte[size];
        long dur = 10_000_000L / Fps;
        for (int n = 0; n < frames; n++)
        {
            Buffer.BlockCopy(yBase, 0, frame, 0, ySize);
            Buffer.BlockCopy(uvBase, 0, frame, ySize, ySize / 2);

            // Moving 160 px square: bright, bounces horizontally with the frame number.
            int sx = 200 + (n * 7) % 2000, sy = 500 + (int)(300 * Math.Sin(n * 0.05));
            for (int y = sy; y < sy + 160; y++)
                for (int x = sx; x < sx + 160; x++)
                    frame[y * Width + x] = 220;

            // Stamp: black plate, then white cells for set bits.
            for (int y = FrameStamp.SrcY0 - 6; y < FrameStamp.SrcY0 + FrameStamp.SrcCell + 6; y++)
                for (int x = FrameStamp.SrcX0 - 6; x < FrameStamp.SrcX0 + FrameStamp.Bits * FrameStamp.SrcPitch + 2; x++)
                    frame[y * Width + x] = 16;
            for (int b = 0; b < FrameStamp.Bits; b++)
            {
                if (((n >> b) & 1) == 0) continue;
                int x0 = FrameStamp.SrcX0 + b * FrameStamp.SrcPitch;
                for (int y = FrameStamp.SrcY0; y < FrameStamp.SrcY0 + FrameStamp.SrcCell; y++)
                    for (int x = x0; x < x0 + FrameStamp.SrcCell; x++)
                        frame[y * Width + x] = 235;
            }

            using var buffer = MediaFactory.MFCreateMemoryBuffer(size);
            buffer.Lock(out IntPtr p, out _, out _);
            Marshal.Copy(frame, 0, p, size);
            buffer.Unlock();
            buffer.CurrentLength = size;
            using var sample = MediaFactory.MFCreateSample();
            sample.AddBuffer(buffer);
            sample.SampleTime = n * dur;
            sample.SampleDuration = dur;
            writer.WriteSample(stream, sample);
        }
        writer.Finalize();
        return path;
    }
}
