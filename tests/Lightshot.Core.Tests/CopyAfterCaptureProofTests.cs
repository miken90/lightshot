// MIT License, Copyright (c) 2026 Viet Le

using System.Threading.Tasks;
using Lightshot.Core.Tests.FakeServices;
using Xunit;

namespace Lightshot.Core.Tests;

// Proof for the user report "Copy to clipboard right after capture does not happen": Settings shows
// "Copy image to clipboard" checked by default, so a completed capture must reach the clipboard.
[Trait("Tier", "Unit")]
public class CopyAfterCaptureProofTests
{
    private static FakeImageSource UnusedImageSource() =>
        new(Result<CapturedImage, ImageLoadError>.Failure(new ImageLoadError.UserCancelled()));

    private static AppCoordinator Coordinator(FakeImageSink sink, FakeSettingsStore settings) => new(
        captureService: new FakeCaptureService(CoreTestFixtures.SampleImage()),
        overlay: new FakeOverlayController(region: new CaptureRegion.RectRegion(new Rect(120, 80, 640, 480))),
        imageSource: UnusedImageSource(),
        imageSink: sink,
        settings: settings,
        ui: new FakeCaptureUI());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AreaCaptureCopiesTheImageToTheClipboard(bool openInEditor)
    {
        var sink = new FakeImageSink();
        var coordinator = Coordinator(sink, new FakeSettingsStore { OpenInEditor = openInEditor });

        await coordinator.CaptureAreaAsync();

        Assert.Single(sink.Copied);
    }

    [Fact]
    public async Task FullscreenCaptureCopiesTheImageToTheClipboard()
    {
        var sink = new FakeImageSink();
        var coordinator = Coordinator(sink, new FakeSettingsStore());

        await coordinator.CaptureFullscreenAsync();

        Assert.Single(sink.Copied);
    }
}
