// MIT License, Copyright (c) 2026 Viet Le

using System.IO;
using Lightshot.App.Views.VideoEditor;
using Lightshot.Core;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.App.Tests;

public class VideoEditorViewModelTests
{
    [Fact]
    [Unit]
    public void EstimatesSizeAndEnforcesMinimumTrim()
    {
        var sourceSize = new Size(1920, 1080);
        double duration = 30.0;
        long fileSize = 10_000_000;
        var vm = new VideoEditorViewModel("test.mp4", duration, sourceSize, fileSize, sourceFps: 30.0, audioTracks: 1);

        // 1. Enforces minimum trim (>= 0.5s)
        Assert.Equal(0.0, vm.TrimStart);
        Assert.Equal(30.0, vm.TrimEnd);
        Assert.Equal(30.0, vm.TrimLength);

        // Attempt trim smaller than 0.5s
        vm.SetTrim(5.0, 5.2);
        Assert.True(vm.TrimLength >= 0.5, $"Trim length {vm.TrimLength} must be >= 0.5");
        Assert.Equal(5.0, vm.TrimStart);
        Assert.Equal(5.5, vm.TrimEnd);

        // 2. Set valid trim and check size estimate matches SizeEstimator
        vm.SetTrim(2.0, 12.0); // 10 seconds
        Assert.Equal(2.0, vm.TrimStart);
        Assert.Equal(12.0, vm.TrimEnd);
        Assert.Equal(10.0, vm.TrimLength);

        var expectedSettings = new VideoEditSettings(
            new TrimRange(2.0, 12.0, 30.0),
            sourceSize,
            VideoBitRate.DefaultQuality,
            AudioEdit.Unchanged.Instance);
        double expectedEstimate = SizeEstimator.EstimatedBytes(expectedSettings, 30.0, 1);
        Assert.Equal(expectedEstimate, vm.EstimatedSizeBytes);

        // 3. Changing preset changes target dimensions and size estimate
        vm.SetPreset(DimensionPreset.P720);
        Assert.Equal(new Size(1280, 720), vm.TargetDimensions);
        var expected720Settings = new VideoEditSettings(
            new TrimRange(2.0, 12.0, 30.0),
            new Size(1280, 720),
            VideoBitRate.DefaultQuality,
            AudioEdit.Unchanged.Instance);
        Assert.Equal(SizeEstimator.EstimatedBytes(expected720Settings, 30.0, 1), vm.EstimatedSizeBytes);
        Assert.True(vm.EstimatedSizeBytes < expectedEstimate);

        // 4. Changing quality scales estimate
        double estBeforeQuality = vm.EstimatedSizeBytes;
        vm.SetQuality(0.2);
        Assert.True(vm.EstimatedSizeBytes < estBeforeQuality);

        // 5. Trim Only mode estimate
        vm.IsTrimOnly = true;
        double expectedTrimOnly = SizeEstimator.EstimatedTrimOnlyBytes(fileSize, new TrimRange(2.0, 12.0, 30.0));
        Assert.Equal(expectedTrimOnly, vm.EstimatedSizeBytes);
    }

    [Fact]
    [Unit]
    public void SnappedKeyFrameTimeIsMaintainedInTrimOnly()
    {
        var vm = new VideoEditorViewModel("sample.mp4", 20.0, new Size(1280, 720), 5_000_000);
        vm.IsTrimOnly = true;
        vm.SetTrim(3.7, 10.0);

        Assert.True(vm.SnappedKeyFrameTime <= vm.TrimStart);
    }
}
