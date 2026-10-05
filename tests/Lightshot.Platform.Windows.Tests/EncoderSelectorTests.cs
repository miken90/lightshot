using System.Collections.Generic;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class EncoderSelectorTests
{
    private class FakeMftEnumerator : IMftVideoEncoderEnumerator
    {
        private readonly List<VideoEncoderDescriptor> _encoders;

        public FakeMftEnumerator(params VideoEncoderDescriptor[] encoders)
        {
            _encoders = new List<VideoEncoderDescriptor>(encoders);
        }

        public IReadOnlyList<VideoEncoderDescriptor> EnumerateEncoders() => _encoders;
    }

    [Fact]
    [Unit]
    public void FallsBackToSoftwareH264()
    {
        // Fake list containing only a software H.264 encoder (no hardware encoders)
        var fakeEnum = new FakeMftEnumerator(
            new VideoEncoderDescriptor("Microsoft H264 Video Encoder MFT", VideoCodec.H264, IsHardware: false)
        );

        var selector = new EncoderSelector(fakeEnum);

        // When requesting H.264 or HEVC without hardware, it must fall back to software H.264
        var encoderH264 = selector.SelectEncoder(preferHevc: false);
        Assert.Equal(VideoCodec.H264, encoderH264.Codec);
        Assert.False(encoderH264.IsHardware);
        Assert.Equal("Microsoft H264 Video Encoder MFT", encoderH264.FriendlyName);

        var encoderHevc = selector.SelectEncoder(preferHevc: true);
        Assert.Equal(VideoCodec.H264, encoderHevc.Codec);
        Assert.False(encoderHevc.IsHardware);
    }

    [Fact]
    [Unit]
    public void SelectsHardwareWhenAvailable()
    {
        var fakeEnum = new FakeMftEnumerator(
            new VideoEncoderDescriptor("NVIDIA H.264 Encoder MFT", VideoCodec.H264, IsHardware: true),
            new VideoEncoderDescriptor("NVIDIA HEVC Encoder MFT", VideoCodec.Hevc, IsHardware: true),
            new VideoEncoderDescriptor("Microsoft H264 Video Encoder MFT", VideoCodec.H264, IsHardware: false)
        );

        var selector = new EncoderSelector(fakeEnum);

        var h264 = selector.SelectEncoder(preferHevc: false);
        Assert.True(h264.IsHardware);
        Assert.Equal(VideoCodec.H264, h264.Codec);
        Assert.Equal("NVIDIA H.264 Encoder MFT", h264.FriendlyName);

        var hevc = selector.SelectEncoder(preferHevc: true);
        Assert.True(hevc.IsHardware);
        Assert.Equal(VideoCodec.Hevc, hevc.Codec);
        Assert.Equal("NVIDIA HEVC Encoder MFT", hevc.FriendlyName);

        // Forcing software ignores hardware transforms
        var swForced = selector.SelectEncoder(preferHevc: false, forceSoftware: true);
        Assert.False(swForced.IsHardware);
        Assert.Equal(VideoCodec.H264, swForced.Codec);
    }
}
