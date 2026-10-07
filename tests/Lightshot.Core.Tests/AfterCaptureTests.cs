// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Threading.Tasks;
using Lightshot.Core.Tests.FakeServices;
using Xunit;

namespace Lightshot.Core.Tests;

[Trait("Tier", "Unit")]
public class AfterCaptureTests
{
    private static FakeImageSource UnusedImageSource() =>
        new(Result<CapturedImage, ImageLoadError>.Failure(new ImageLoadError.UserCancelled()));

    private static AppCoordinator Coordinator(IImageSink sink, FakeSettingsStore settings, FakeCaptureUI? ui = null) => new(
        captureService: new FakeCaptureService(CoreTestFixtures.SampleImage()),
        overlay: new FakeOverlayController(region: new CaptureRegion.RectRegion(new Rect(120, 80, 640, 480))),
        imageSource: UnusedImageSource(),
        imageSink: sink,
        settings: settings,
        ui: ui ?? new FakeCaptureUI());

    [Fact]
    public async Task CopyOffLeavesTheClipboardAlone()
    {
        var sink = new FakeImageSink();
        var settings = new FakeSettingsStore
        {
            AfterCapture = new AfterCaptureSettings(CopyToClipboard: false)
        };
        var coordinator = Coordinator(sink, settings);

        await coordinator.CaptureAreaAsync();

        Assert.Empty(sink.Copied);
    }

    [Fact]
    public async Task SaveOnWritesTheCaptureToTheDefaultDestination()
    {
        var sink = new FakeImageSink();
        var settings = new FakeSettingsStore
        {
            AfterCapture = new AfterCaptureSettings(SaveToFile: true)
        };
        var coordinator = Coordinator(sink, settings);

        await coordinator.CaptureAreaAsync();

        Assert.Single(sink.Written);
    }

    [Fact]
    public async Task QuickAccessOffShowsNoCard()
    {
        var sink = new FakeImageSink();
        var ui = new FakeCaptureUI();
        var settings = new FakeSettingsStore
        {
            OpenInEditor = false,
            AfterCapture = new AfterCaptureSettings(ShowQuickAccess: false)
        };
        var coordinator = Coordinator(sink, settings, ui);

        await coordinator.CaptureAreaAsync();

        Assert.Empty(ui.QuickAccess);
        Assert.Empty(ui.OpenedImages);
    }

    [Fact]
    public async Task EditorOnSkipsQuickAccess()
    {
        var sink = new FakeImageSink();
        var ui = new FakeCaptureUI();
        var settings = new FakeSettingsStore
        {
            OpenInEditor = true,
            AfterCapture = new AfterCaptureSettings(ShowQuickAccess: true)
        };
        var coordinator = Coordinator(sink, settings, ui);

        await coordinator.CaptureAreaAsync();

        Assert.Single(ui.OpenedImages);
        Assert.Empty(ui.QuickAccess);
    }

    [Fact]
    public async Task CopyFailureStillOpensTheEditor()
    {
        var throwingSink = new ThrowingClipboardSink();
        var ui = new FakeCaptureUI();
        var settings = new FakeSettingsStore
        {
            OpenInEditor = true,
            AfterCapture = new AfterCaptureSettings(CopyToClipboard: true)
        };
        var coordinator = Coordinator(throwingSink, settings, ui);

        await coordinator.CaptureAreaAsync();

        Assert.Single(ui.OpenedImages);
    }

    private sealed class ThrowingClipboardSink : IImageSink
    {
        public void CopyToClipboard(RenderedImage image) => throw new InvalidOperationException("clipboard locked");
        public void CopyText(string text) { }
        public void Write(RenderedImage image, string path, ImageFormat format) { }
    }
}
