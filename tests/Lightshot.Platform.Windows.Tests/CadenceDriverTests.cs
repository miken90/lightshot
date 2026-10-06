// Ported for Lightshot Windows Port (Phase 7 R2)
// MIT License, Copyright (c) 2026 Viet Le

using System.Collections.Generic;
using Lightshot.Platform.Windows.Recording;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Platform.Windows.Tests;

public class CadenceDriverTests
{
    private record FakeFrame(int Id, string Label);

    [Fact]
    [Unit]
    public void ConstantFpsFromIdleFakeSource()
    {
        int fps = 30;
        var driver = new CadenceDriver<FakeFrame>(fps);
        var delivered = new List<(FakeFrame Frame, long TimeHns, long DurationHns, bool IsRepeat)>();

        // Submit initial frame at t = 0
        driver.SubmitFrame(new FakeFrame(1, "InitialScreen"));

        // Source becomes completely idle for 1.0 second (10,000,000 HNS)
        long oneSecondHns = 10_000_000;
        int emitted = driver.EmitDueFrames(oneSecondHns, (frame, timeHns, durationHns, isRepeat) =>
        {
            delivered.Add((frame, timeHns, durationHns, isRepeat));
        });

        // Exactly 30 frames emitted for 1.0 second at 30 fps
        Assert.Equal(30, emitted);
        Assert.Equal(30, delivered.Count);
        Assert.Equal(30, driver.TotalFramesDelivered);
        Assert.Equal(29, driver.RepeatedFramesCount);

        // First frame is original, all remaining 29 frames are repeated
        Assert.False(delivered[0].IsRepeat);
        Assert.Equal(1, delivered[0].Frame.Id);
        Assert.Equal(0, delivered[0].TimeHns);

        for (int i = 1; i < 30; i++)
        {
            Assert.True(delivered[i].IsRepeat, $"Frame {i} should be marked as repeat");
            Assert.Equal(1, delivered[i].Frame.Id);
            long expectedTime = i * driver.FrameDurationHns;
            Assert.Equal(expectedTime, delivered[i].TimeHns);
            Assert.Equal(driver.FrameDurationHns, delivered[i].DurationHns);
        }
    }

    [Fact]
    [Unit]
    public void DeliversNewFramesWhenAvailableAndRepeatsWhenIdle()
    {
        int fps = 30;
        var driver = new CadenceDriver<FakeFrame>(fps);
        var delivered = new List<(FakeFrame Frame, long TimeHns, bool IsRepeat)>();

        // t = 0: Submit Frame A
        driver.SubmitFrame(new FakeFrame(1, "FrameA"));

        // Emit first half second (0..14 frames)
        long halfSecondHns = 5_000_000;
        int firstHalfEmitted = driver.EmitDueFrames(halfSecondHns, (frame, timeHns, _, isRepeat) =>
        {
            delivered.Add((frame, timeHns, isRepeat));
        });
        Assert.Equal(15, firstHalfEmitted);
        Assert.Equal(15, delivered.Count);
        Assert.False(delivered[0].IsRepeat);
        for (int i = 1; i < 15; i++)
        {
            Assert.True(delivered[i].IsRepeat);
            Assert.Equal(1, delivered[i].Frame.Id);
        }

        // t = 0.5s: Submit Frame B (screen changed)
        driver.SubmitFrame(new FakeFrame(2, "FrameB"));

        // Emit second half second (15..29 frames)
        long oneSecondHns = 10_000_000;
        int secondHalfEmitted = driver.EmitDueFrames(oneSecondHns, (frame, timeHns, _, isRepeat) =>
        {
            delivered.Add((frame, timeHns, isRepeat));
        });

        Assert.Equal(15, secondHalfEmitted);
        Assert.Equal(30, delivered.Count);
        // Frame 15 should be Frame B and NOT repeat
        Assert.Equal(2, delivered[15].Frame.Id);
        Assert.False(delivered[15].IsRepeat);

        // Frames 16..29 should be Frame B and repeat
        for (int i = 16; i < 30; i++)
        {
            Assert.Equal(2, delivered[i].Frame.Id);
            Assert.True(delivered[i].IsRepeat);
        }
    }

    [Fact]
    [Unit]
    public void NoEmissionBeforeFirstFrame()
    {
        var driver = new CadenceDriver<FakeFrame>(30);
        bool emitted = driver.TryEmitNextFrame((_, _, _, _) => { });
        Assert.False(emitted);
        Assert.Equal(0, driver.TotalFramesDelivered);
    }

    [Fact]
    [Unit]
    public void PlanBatchMatchesFrameCadencePlanner()
    {
        var driver = new CadenceDriver<string>(30);
        var sourceFrames = new List<(double Timestamp, string Frame)>
        {
            (0.0, "A"),
            (0.5, "B")
        };

        var planned = driver.PlanBatch(sourceFrames, 1.0);
        Assert.Equal(30, planned.Count);
        Assert.Equal("A", planned[0].Frame);
        Assert.False(planned[0].IsRepeat);
        Assert.Equal("A", planned[14].Frame);
        Assert.True(planned[14].IsRepeat);
        Assert.Equal("B", planned[15].Frame);
        Assert.False(planned[15].IsRepeat);
    }
}
