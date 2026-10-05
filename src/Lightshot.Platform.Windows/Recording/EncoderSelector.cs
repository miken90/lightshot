using System;
using System.Collections.Generic;
using System.Linq;
using Vortice.MediaFoundation;

namespace Lightshot.Platform.Windows.Recording;

public enum VideoCodec
{
    H264,
    Hevc
}

public record VideoEncoderDescriptor(
    string FriendlyName,
    VideoCodec Codec,
    bool IsHardware,
    Guid Clsid = default
);

public interface IMftVideoEncoderEnumerator
{
    IReadOnlyList<VideoEncoderDescriptor> EnumerateEncoders();
}

public sealed class SystemMftVideoEncoderEnumerator : IMftVideoEncoderEnumerator
{
    public IReadOnlyList<VideoEncoderDescriptor> EnumerateEncoders()
    {
        var list = new List<VideoEncoderDescriptor>();
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

                VideoCodec? codec = null;
                if (name.Contains("H.264", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("H264", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("AVC", StringComparison.OrdinalIgnoreCase))
                {
                    codec = VideoCodec.H264;
                }
                else if (name.Contains("HEVC", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("H.265", StringComparison.OrdinalIgnoreCase))
                {
                    codec = VideoCodec.Hevc;
                }

                if (codec.HasValue)
                {
                    Guid clsid = Guid.Empty;
                    try { clsid = act.GetGUID(TransformAttributeKeys.MftTransformClsidAttribute); } catch { }
                    list.Add(new VideoEncoderDescriptor(name, codec.Value, isHw, clsid));
                }
            }
        }
        catch
        {
            // Media Foundation enumeration error fallback
        }
        return list;
    }
}

public sealed class EncoderSelector
{
    private readonly IMftVideoEncoderEnumerator _enumerator;

    public EncoderSelector(IMftVideoEncoderEnumerator? enumerator = null)
    {
        _enumerator = enumerator ?? new SystemMftVideoEncoderEnumerator();
    }

    public VideoEncoderDescriptor SelectEncoder(bool preferHevc = false, bool forceSoftware = false)
    {
        var encoders = _enumerator.EnumerateEncoders();

        if (preferHevc && !forceSoftware)
        {
            var hwHevc = encoders.FirstOrDefault(e => e.Codec == VideoCodec.Hevc && e.IsHardware);
            if (hwHevc != null) return hwHevc;
        }

        if (!forceSoftware)
        {
            var hwH264 = encoders.FirstOrDefault(e => e.Codec == VideoCodec.H264 && e.IsHardware);
            if (hwH264 != null) return hwH264;
        }

        var swH264 = encoders.FirstOrDefault(e => e.Codec == VideoCodec.H264 && !e.IsHardware);
        if (swH264 != null) return swH264;

        // Default software descriptor fallback
        return new VideoEncoderDescriptor("Microsoft H264 Video Encoder MFT", VideoCodec.H264, IsHardware: false);
    }

    public (bool HasHardwareH264, bool HasHardwareHevc) ProbeHardwareCapabilities()
    {
        var encoders = _enumerator.EnumerateEncoders();
        bool hasHwH264 = encoders.Any(e => e.Codec == VideoCodec.H264 && e.IsHardware);
        bool hasHwHevc = encoders.Any(e => e.Codec == VideoCodec.Hevc && e.IsHardware);
        return (hasHwH264, hasHwHevc);
    }
}
