// Ported from LightshotKit/Tests/LightshotKitTests/AppCoordinatorTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Core.Tests.FakeServices;
using Xunit;

namespace Lightshot.Core.Tests;

[Trait("Tier", "Unit")]
public class AppCoordinatorTests
{
    private static CapturedImage SampleImage() => CoreTestFixtures.SampleImage();
    private static FrozenScreen SampleFrozenScreen() => CoreTestFixtures.SampleFrozenScreen();
    private static CapturedImage SampleWindowImage() => CoreTestFixtures.SampleWindowImage();

    private static CaptureRegion.RectRegion SampleRegion() =>
        new(new Rect(120, 80, 640, 480));

    private static CaptureRegion.WindowRegion SampleWindowRegion() =>
        new(4242, new Rect(200, 140, 800, 600));

    private static CapturedImage FrozenSampleWindowCapture()
    {
        var frozen = SampleFrozenScreen();
        frozen.SetWindowImages(new Dictionary<uint, CapturedImage> { [4242] = SampleWindowImage() });
        return frozen.ImageOf(SampleWindowRegion())!.Value;
    }

    private static FakeOverlayController UnusedOverlay() => new(region: null);

    private static FakeImageSource UnusedImageSource() =>
        new(Result<CapturedImage, ImageLoadError>.Failure(new ImageLoadError.UserCancelled()));

    private static (HistoryStore Store, string Dir) TempHistory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coord-history-" + Guid.NewGuid().ToString("N"));
        return (new HistoryStore(dir), dir);
    }

    private static AppCoordinator FrozenCoordinator(
        FakeCaptureService capture,
        FakeOverlayController overlay,
        FakeSettingsStore? settings = null,
        HistoryStore? history = null,
        FakeImageSink? sink = null,
        FakeDelaySpy? delays = null,
        FakeCaptureUI? ui = null)
    {
        return new AppCoordinator(
            captureService: capture,
            overlay: overlay,
            imageSource: UnusedImageSource(),
            imageSink: sink ?? new FakeImageSink(),
            settings: settings ?? new FakeSettingsStore(),
            history: history,
            sleep: delays != null ? (s => delays.SleepAsync(s)) : null,
            ui: ui ?? new FakeCaptureUI()
        );
    }

    private static RenderedImage Render(AnnotationDocument document)
    {
        return new RenderedImage(document.Image.PixelWidth, document.Image.PixelHeight, document.Image.Data.ToArray());
    }

    // MARK: - Capture routing

    [Fact]
    public async Task SuccessfulCaptureReachesEditorWithTheImage()
    {
        var image = SampleImage();
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(image),
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync();

        Assert.Equal([image], ui.OpenedImages);
        Assert.Equal(0, ui.PermissionDeniedCount);
        Assert.Empty(ui.Failures);
    }

    [Fact]
    public async Task PermissionDeniedRoutesToRecoveryNeverABlankEditor()
    {
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.PermissionDenied())),
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync();

        Assert.Equal(1, ui.PermissionDeniedCount);
        Assert.Equal([PermissionKind.ScreenRecording], ui.DeniedKinds);
        Assert.Empty(ui.OpenedImages);
        Assert.Empty(ui.Failures);
    }

    [Fact]
    public async Task UserCancelledIsASilentNoOp()
    {
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.UserCancelled())),
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync();

        Assert.Empty(ui.OpenedImages);
        Assert.Equal(0, ui.PermissionDeniedCount);
        Assert.Empty(ui.Failures);
    }

    [Fact]
    public async Task OtherFailuresSurfaceADistinctMessage()
    {
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.NoDisplayAvailable())),
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync();

        Assert.Equal([new CaptureError.NoDisplayAvailable()], ui.Failures);
        Assert.Empty(ui.OpenedImages);
        Assert.Equal(0, ui.PermissionDeniedCount);
    }

    // MARK: - Permission onboarding & recovery

    [Fact]
    public async Task FirstRunPromptsForAuthorizationBeforeCapturing()
    {
        var image = SampleImage();
        var ui = new FakeCaptureUI();
        var capture = new FakeCaptureService(image, status: CaptureAuthorizationStatus.NotDetermined, requestResult: CaptureAuthorizationStatus.Authorized);
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync();

        Assert.Equal(1, capture.RequestAuthorizationCount);
        Assert.Equal([image], ui.OpenedImages);
        Assert.Equal(0, ui.PermissionDeniedCount);
    }

    [Fact]
    public async Task AlreadyAuthorizedNeverRePrompts()
    {
        var ui = new FakeCaptureUI();
        var capture = new FakeCaptureService(SampleImage(), status: CaptureAuthorizationStatus.Authorized);
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync();

        Assert.Equal(0, capture.RequestAuthorizationCount);
        Assert.Single(ui.OpenedImages);
    }

    [Fact]
    public async Task StandingDenialDoesNotRePromptAndTheCaptureRoutesToRecovery()
    {
        var ui = new FakeCaptureUI();
        var capture = new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.PermissionDenied()), status: CaptureAuthorizationStatus.Denied);
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync();

        Assert.Equal(0, capture.RequestAuthorizationCount);
        Assert.Equal(1, ui.PermissionDeniedCount);
        Assert.Equal([PermissionKind.ScreenRecording], ui.DeniedKinds);
        Assert.Empty(ui.OpenedImages);
    }

    [Fact]
    public async Task PermissionRevokedAfterTheAdvisoryCheckStillRoutesToRecovery()
    {
        var ui = new FakeCaptureUI();
        var capture = new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.PermissionDenied()), status: CaptureAuthorizationStatus.Authorized);
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync();

        Assert.Equal(0, capture.RequestAuthorizationCount);
        Assert.Equal(1, ui.PermissionDeniedCount);
        Assert.Empty(ui.OpenedImages);
    }

    [Fact]
    public async Task AreaCaptureFirstRunPromptsThenRunsTheOverlay()
    {
        var ui = new FakeCaptureUI();
        var capture = new FakeCaptureService(SampleImage(), status: CaptureAuthorizationStatus.NotDetermined, requestResult: CaptureAuthorizationStatus.Authorized);
        var overlay = new FakeOverlayController(region: SampleRegion());
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: overlay,
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureAreaAsync();

        Assert.Equal(1, capture.RequestAuthorizationCount);
        Assert.Equal(1, overlay.CallCount);
        Assert.Single(ui.OpenedImages);
    }

    [Fact]
    public async Task FirstRunFullscreenStopsAtTheSystemPromptInsteadOfStackingTheRecoveryAlert()
    {
        var ui = new FakeCaptureUI();
        var capture = new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.PermissionDenied()), status: CaptureAuthorizationStatus.NotDetermined, requestResult: CaptureAuthorizationStatus.Denied);
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync();

        Assert.Equal(1, capture.RequestAuthorizationCount);
        Assert.Empty(capture.CapturedDisplays);
        Assert.Equal(0, ui.PermissionDeniedCount);
    }

    [Fact]
    public async Task FirstRunAreaStopsAtTheSystemPromptInsteadOfCoveringItWithTheOverlay()
    {
        var ui = new FakeCaptureUI();
        var capture = new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.PermissionDenied()), status: CaptureAuthorizationStatus.NotDetermined, requestResult: CaptureAuthorizationStatus.Denied);
        var overlay = new FakeOverlayController(region: SampleRegion());
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: overlay,
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureAreaAsync();

        Assert.Equal(1, capture.RequestAuthorizationCount);
        Assert.Equal(0, overlay.CallCount);
        Assert.Empty(capture.CapturedRegions);
        Assert.Equal(0, ui.PermissionDeniedCount);
    }

    [Fact]
    public async Task FirstRunWindowStopsAtTheSystemPromptInsteadOfCoveringItWithTheOverlay()
    {
        var ui = new FakeCaptureUI();
        var capture = new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.PermissionDenied()), status: CaptureAuthorizationStatus.NotDetermined, requestResult: CaptureAuthorizationStatus.Denied);
        var overlay = new FakeOverlayController(windowRegion: SampleWindowRegion());
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: overlay,
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureWindowAsync();

        Assert.Equal(1, capture.RequestAuthorizationCount);
        Assert.Equal(0, overlay.WindowCallCount);
        Assert.Empty(capture.CapturedRegions);
        Assert.Equal(0, ui.PermissionDeniedCount);
    }

    // MARK: - Output

    [Fact]
    public void CopyPlacesTheRenderedImageOnTheClipboard()
    {
        var document = new AnnotationDocument(SampleImage());
        var sink = new FakeImageSink();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(document.BaseImage),
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: sink,
            settings: new FakeSettingsStore(),
            ui: new FakeCaptureUI()
        );

        coordinator.CopyToClipboard(document);

        Assert.Single(sink.Copied);
        Assert.Equal(Render(document), sink.Copied[0]);
        Assert.Empty(sink.Written);
    }

    // MARK: - Area capture routing

    [Fact]
    public async Task AreaCaptureResolvesRegionThenCapturesItAndOpensTheEditor()
    {
        var image = SampleImage();
        var region = SampleRegion();
        var ui = new FakeCaptureUI();
        var capture = new FakeCaptureService(image);
        var overlay = new FakeOverlayController(region: region);
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: overlay,
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureAreaAsync();

        Assert.Equal(1, overlay.CallCount);
        Assert.Empty(capture.CapturedRegions);
        Assert.Equal([SampleFrozenScreen().ImageOf(region)!.Value], ui.OpenedImages);
        Assert.Empty(ui.QuickAccess);
        Assert.Empty(ui.Failures);
    }

    [Fact]
    public async Task AreaCaptureShowsTheToolbarWhenOpenInEditorIsOff()
    {
        var image = SampleImage();
        var region = SampleRegion();
        var ui = new FakeCaptureUI();
        var settings = new FakeSettingsStore { OpenInEditor = false };
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(image),
            overlay: new FakeOverlayController(region: region),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: settings,
            ui: ui
        );

        await coordinator.CaptureAreaAsync();

        Assert.Single(ui.QuickAccess);
        Assert.Equal(SampleFrozenScreen().ImageOf(region)!.Value, ui.QuickAccess[0]);
        Assert.Empty(ui.OpenedImages);
    }

    [Fact]
    public async Task AreaCaptureTriggeredWhileTheOverlayIsOpenIsIgnored()
    {
        var ui = new FakeCaptureUI();
        var overlay = new FakeOverlayController(region: SampleRegion())
        {
            PendingRegion = new TaskCompletionSource<CaptureRegion?>()
        };
        var capture = new FakeCaptureService(SampleImage());
        var coordinator = FrozenCoordinator(capture, overlay, ui: ui);

        var first = coordinator.CaptureAreaAsync();
        // Ignored triggers return at once; a stacked second flow would wait on the open overlay instead.
        await Task.WhenAll(
                coordinator.CaptureAreaAsync(),
                coordinator.CaptureWindowAsync(),
                coordinator.CaptureFullscreenAsync())
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(1, overlay.CallCount);
        Assert.Equal(0, overlay.WindowCallCount);
        Assert.Empty(capture.CapturedDisplays);
        Assert.Empty(ui.OpenedImages);

        overlay.PendingRegion.SetResult(SampleRegion());
        await first;
        Assert.Single(ui.OpenedImages);

        overlay.PendingRegion = null;
        await coordinator.CaptureAreaAsync();
        Assert.Equal(2, overlay.CallCount);
        Assert.Equal(2, ui.OpenedImages.Count);
    }

    [Fact]
    public async Task OpenInEditorIsReadAtCaptureTimeSoAChangeAppliesToTheNextCapture()
    {
        var ui = new FakeCaptureUI();
        var settings = new FakeSettingsStore();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(SampleImage()),
            overlay: new FakeOverlayController(region: SampleRegion()),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: settings,
            ui: ui
        );

        await coordinator.CaptureAreaAsync();
        settings.OpenInEditor = false;
        await coordinator.CaptureAreaAsync();

        Assert.Single(ui.OpenedImages);
        Assert.Single(ui.QuickAccess);
    }

    [Fact]
    public async Task FullscreenFollowsOpenInEditorButOpenFileAlwaysOpensTheEditor()
    {
        var image = SampleImage();
        var ui = new FakeCaptureUI();
        var settings = new FakeSettingsStore { OpenInEditor = false };
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(image),
            overlay: UnusedOverlay(),
            imageSource: new FakeImageSource(image),
            imageSink: new FakeImageSink(),
            settings: settings,
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync();
        coordinator.OpenFile();

        Assert.Equal([image], ui.QuickAccess);
        Assert.Equal([image], ui.OpenedImages);
    }

    [Fact]
    public async Task TheSelfTimerAndRepeatLastCapturePathsAlsoEndInQuickAccess()
    {
        var image = SampleImage();
        var ui = new FakeCaptureUI();
        var settings = new FakeSettingsStore
        {
            OpenInEditor = false,
            CaptureDelay = 3
        };
        var delays = new FakeDelaySpy();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(image),
            overlay: new FakeOverlayController(region: SampleRegion()),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: settings,
            sleep: s => delays.SleepAsync(s),
            ui: ui
        );

        await coordinator.CaptureAreaAsync();
        await coordinator.CaptureFullscreenAsync();
        await coordinator.RepeatLastCaptureAsync();

        Assert.Equal([3.0, 3.0, 3.0], delays.Waits);
        Assert.Equal([image, image, image], ui.QuickAccess);
        Assert.Empty(ui.OpenedImages);
    }

    [Fact]
    public async Task FullscreenOpensTheEditorWhenOpenInEditorIsOn()
    {
        var image = SampleImage();
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(image),
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync();

        Assert.Equal([image], ui.OpenedImages);
        Assert.Empty(ui.QuickAccess);
    }

    [Fact]
    public async Task EscapeInTheOverlayCancelsWithNoCapture()
    {
        var ui = new FakeCaptureUI();
        var capture = new FakeCaptureService(SampleImage());
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: new FakeOverlayController(region: null),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureAreaAsync();

        Assert.Empty(capture.CapturedRegions);
        Assert.Empty(ui.QuickAccess);
        Assert.Empty(ui.OpenedImages);
        Assert.Equal(0, ui.PermissionDeniedCount);
        Assert.Empty(ui.Failures);
    }

    [Fact]
    public async Task AreaCapturePermissionDeniedRoutesToRecoveryNotAToolbar()
    {
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.PermissionDenied())),
            overlay: new FakeOverlayController(region: SampleRegion()),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureAreaAsync();

        Assert.Equal(1, ui.PermissionDeniedCount);
        Assert.Empty(ui.QuickAccess);
        Assert.Empty(ui.OpenedImages);
        Assert.Empty(ui.Failures);
    }

    [Fact]
    public async Task AreaCaptureUserCancelledIsASilentNoOp()
    {
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.UserCancelled())),
            overlay: new FakeOverlayController(region: SampleRegion()),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureAreaAsync();

        Assert.Empty(ui.QuickAccess);
        Assert.Empty(ui.OpenedImages);
        Assert.Equal(0, ui.PermissionDeniedCount);
        Assert.Empty(ui.Failures);
    }

    [Fact]
    public async Task AreaCaptureOtherFailureSurfacesADistinctMessage()
    {
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.NoDisplayAvailable())),
            overlay: new FakeOverlayController(region: SampleRegion()),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureAreaAsync();

        Assert.Equal([new CaptureError.NoDisplayAvailable()], ui.Failures);
        Assert.Empty(ui.QuickAccess);
        Assert.Empty(ui.OpenedImages);
        Assert.Equal(0, ui.PermissionDeniedCount);
    }

    // MARK: - Window capture routing

    [Fact]
    public async Task WindowCaptureResolvesWindowThenCapturesItAndOpensTheEditor()
    {
        var image = SampleImage();
        var region = SampleWindowRegion();
        var ui = new FakeCaptureUI();
        var capture = new FakeCaptureService(image);
        var overlay = new FakeOverlayController(windowRegion: region);
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: overlay,
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureWindowAsync();

        Assert.Equal(1, overlay.WindowCallCount);
        Assert.Equal(0, overlay.CallCount);
        Assert.Empty(capture.CapturedRegions);
        Assert.Equal([FrozenSampleWindowCapture()], ui.OpenedImages);
        Assert.Empty(ui.QuickAccess);
        Assert.Empty(ui.Failures);
    }

    [Fact]
    public async Task WindowCaptureShowsTheToolbarWhenOpenInEditorIsOff()
    {
        var image = SampleImage();
        var region = SampleWindowRegion();
        var ui = new FakeCaptureUI();
        var settings = new FakeSettingsStore { OpenInEditor = false };
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(image),
            overlay: new FakeOverlayController(windowRegion: region),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: settings,
            ui: ui
        );

        await coordinator.CaptureWindowAsync();

        Assert.Single(ui.QuickAccess);
        Assert.Equal(FrozenSampleWindowCapture(), ui.QuickAccess[0]);
        Assert.Empty(ui.OpenedImages);
    }

    [Fact]
    public async Task WindowCaptureFirstRunPromptsThenRunsTheOverlay()
    {
        var ui = new FakeCaptureUI();
        var capture = new FakeCaptureService(SampleImage(), status: CaptureAuthorizationStatus.NotDetermined, requestResult: CaptureAuthorizationStatus.Authorized);
        var overlay = new FakeOverlayController(windowRegion: SampleWindowRegion());
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: overlay,
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureWindowAsync();

        Assert.Equal(1, capture.RequestAuthorizationCount);
        Assert.Equal(1, overlay.WindowCallCount);
        Assert.Single(ui.OpenedImages);
    }

    [Fact]
    public async Task EscapeInWindowModeCancelsWithNoCapture()
    {
        var ui = new FakeCaptureUI();
        var capture = new FakeCaptureService(SampleImage());
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: new FakeOverlayController(windowRegion: null),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureWindowAsync();

        Assert.Empty(capture.CapturedRegions);
        Assert.Empty(ui.QuickAccess);
        Assert.Empty(ui.OpenedImages);
        Assert.Equal(0, ui.PermissionDeniedCount);
        Assert.Empty(ui.Failures);
    }

    [Fact]
    public async Task WindowCapturePermissionDeniedRoutesToRecoveryNotAToolbar()
    {
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.PermissionDenied())),
            overlay: new FakeOverlayController(windowRegion: SampleWindowRegion()),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureWindowAsync();

        Assert.Equal(1, ui.PermissionDeniedCount);
        Assert.Empty(ui.QuickAccess);
        Assert.Empty(ui.OpenedImages);
        Assert.Empty(ui.Failures);
    }

    [Fact]
    public async Task WindowCaptureUserCancelledIsASilentNoOp()
    {
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.UserCancelled())),
            overlay: new FakeOverlayController(windowRegion: SampleWindowRegion()),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureWindowAsync();

        Assert.Empty(ui.QuickAccess);
        Assert.Empty(ui.OpenedImages);
        Assert.Equal(0, ui.PermissionDeniedCount);
        Assert.Empty(ui.Failures);
    }

    [Fact]
    public async Task WindowCaptureOtherFailureSurfacesADistinctMessage()
    {
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.NoDisplayAvailable())),
            overlay: new FakeOverlayController(windowRegion: SampleWindowRegion()),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureWindowAsync();

        Assert.Equal([new CaptureError.NoDisplayAvailable()], ui.Failures);
        Assert.Empty(ui.QuickAccess);
        Assert.Empty(ui.OpenedImages);
        Assert.Equal(0, ui.PermissionDeniedCount);
    }

    // MARK: - Open existing file routing

    [Fact]
    public void OpenFileLoadsTheImageIntoTheEditorViaOpenEditor()
    {
        var image = SampleImage();
        var ui = new FakeCaptureUI();
        var source = new FakeImageSource(image);
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.NoDisplayAvailable())),
            overlay: UnusedOverlay(),
            imageSource: source,
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        coordinator.OpenFile();

        Assert.Equal(1, source.OpenCount);
        Assert.Equal([image], ui.OpenedImages);
        Assert.Empty(ui.ImageLoadFailures);
        Assert.Equal(0, ui.PermissionDeniedCount);
    }

    [Fact]
    public void OpenFileCancelledPanelIsASilentNoOp()
    {
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(SampleImage()),
            overlay: UnusedOverlay(),
            imageSource: new FakeImageSource(Result<CapturedImage, ImageLoadError>.Failure(new ImageLoadError.UserCancelled())),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        coordinator.OpenFile();

        Assert.Empty(ui.OpenedImages);
        Assert.Empty(ui.ImageLoadFailures);
        Assert.Equal(0, ui.PermissionDeniedCount);
    }

    [Fact]
    public void OpenFileUnreadableSurfacesADistinctFailureNotABlankEditor()
    {
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(SampleImage()),
            overlay: UnusedOverlay(),
            imageSource: new FakeImageSource(Result<CapturedImage, ImageLoadError>.Failure(new ImageLoadError.Unreadable())),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        coordinator.OpenFile();

        Assert.Equal([new ImageLoadError.Unreadable()], ui.ImageLoadFailures);
        Assert.Empty(ui.OpenedImages);
    }

    [Fact]
    public void OpenFileUnsupportedFormatSurfacesADistinctFailure()
    {
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(SampleImage()),
            overlay: UnusedOverlay(),
            imageSource: new FakeImageSource(Result<CapturedImage, ImageLoadError>.Failure(new ImageLoadError.UnsupportedFormat())),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        coordinator.OpenFile();

        Assert.Equal([new ImageLoadError.UnsupportedFormat()], ui.ImageLoadFailures);
        Assert.Empty(ui.OpenedImages);
    }

    // MARK: - Output (save & drag)

    [Fact]
    public void SaveWritesTheRenderedImageWithTheExactFormat()
    {
        var document = new AnnotationDocument(SampleImage());
        var sink = new FakeImageSink();
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(document.BaseImage),
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: sink,
            settings: new FakeSettingsStore(),
            ui: new FakeCaptureUI()
        );
        var path = "/tmp/out.jpg";

        coordinator.Save(document, path, new ImageFormat.Jpeg(0.42));

        Assert.Single(sink.Written);
        Assert.Equal(Render(document), sink.Written[0].Image);
        Assert.Equal(path, sink.Written[0].Path);
        Assert.Equal(new ImageFormat.Jpeg(0.42), sink.Written[0].Format);
    }

    [Fact]
    public void DefaultSaveUsesTheConfiguredLocationPatternAndFormat()
    {
        var document = new AnnotationDocument(SampleImage());
        var sink = new FakeImageSink();
        var settings = new FakeSettingsStore
        {
            DefaultFormat = new ImageFormat.Jpeg(0.8),
            SaveLocation = "/tmp/shots",
            FilenamePattern = "shot-%Y"
        };
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(document.BaseImage),
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: sink,
            settings: settings,
            ui: new FakeCaptureUI()
        );
        var date = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        var written = coordinator.Save(document, date);

        Assert.Equal("shot-2026.jpg", Path.GetFileName(written));
        Assert.Equal("/tmp/shots", Path.GetDirectoryName(written)?.Replace('\\', '/'));
        Assert.Equal(new ImageFormat.Jpeg(0.8), sink.Written[0].Format);
    }

    [Fact]
    public void DragItemCarriesTheRenderedBytesInTheDefaultFormat()
    {
        var document = new AnnotationDocument(SampleImage());
        var settings = new FakeSettingsStore
        {
            DefaultFormat = new ImageFormat.Png(),
            FilenamePattern = "drag-%Y"
        };
        var coordinator = new AppCoordinator(
            captureService: new FakeCaptureService(document.BaseImage),
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: settings,
            ui: new FakeCaptureUI()
        );
        var date = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        var item = coordinator.DragItem(document, date);

        Assert.Equal(new ImageFormat.Png(), item.Format);
        Assert.Equal("drag-2026", item.SuggestedName);
        Assert.Equal(Render(document).Data, item.Data);
    }

    // MARK: - History recording

    [Fact]
    public async Task FullscreenCaptureIsRecordedInHistory()
    {
        var image = SampleImage();
        var ui = new FakeCaptureUI();
        var (history, dir) = TempHistory();
        try
        {
            var coordinator = new AppCoordinator(
                captureService: new FakeCaptureService(image),
                overlay: UnusedOverlay(),
                imageSource: UnusedImageSource(),
                imageSink: new FakeImageSink(),
                settings: new FakeSettingsStore(),
                history: history,
                ui: ui
            );

            await coordinator.CaptureFullscreenAsync();

            var records = history.All();
            Assert.Single(records);
            Assert.Equal(CaptureSource.Fullscreen, records[0].Source);
            Assert.Equal(image, history.CapturedImage(records[0])!.Value);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task AreaCaptureIsRecordedInHistory()
    {
        var ui = new FakeCaptureUI();
        var (history, dir) = TempHistory();
        try
        {
            var coordinator = new AppCoordinator(
                captureService: new FakeCaptureService(SampleImage()),
                overlay: new FakeOverlayController(region: SampleRegion()),
                imageSource: UnusedImageSource(),
                imageSink: new FakeImageSink(),
                settings: new FakeSettingsStore(),
                history: history,
                ui: ui
            );

            await coordinator.CaptureAreaAsync();

            var records = history.All();
            Assert.Single(records);
            Assert.Equal(CaptureSource.Area, records[0].Source);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task WindowCaptureIsRecordedInHistory()
    {
        var ui = new FakeCaptureUI();
        var (history, dir) = TempHistory();
        try
        {
            var coordinator = new AppCoordinator(
                captureService: new FakeCaptureService(SampleImage()),
                overlay: new FakeOverlayController(windowRegion: SampleWindowRegion()),
                imageSource: UnusedImageSource(),
                imageSink: new FakeImageSink(),
                settings: new FakeSettingsStore(),
                history: history,
                ui: ui
            );

            await coordinator.CaptureWindowAsync();

            var records = history.All();
            Assert.Single(records);
            Assert.Equal(CaptureSource.Window, records[0].Source);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void OpeningAFileIsNotRecordedInHistory()
    {
        var ui = new FakeCaptureUI();
        var (history, dir) = TempHistory();
        try
        {
            var coordinator = new AppCoordinator(
                captureService: new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.UserCancelled())),
                overlay: UnusedOverlay(),
                imageSource: new FakeImageSource(SampleImage()),
                imageSink: new FakeImageSink(),
                settings: new FakeSettingsStore(),
                history: history,
                ui: ui
            );

            coordinator.OpenFile();

            Assert.Equal([SampleImage()], ui.OpenedImages);
            Assert.Empty(history.All());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task AFailedCaptureRecordsNothing()
    {
        var ui = new FakeCaptureUI();
        var (history, dir) = TempHistory();
        try
        {
            var coordinator = new AppCoordinator(
                captureService: new FakeCaptureService(Result<CapturedImage, CaptureError>.Failure(new CaptureError.PermissionDenied())),
                overlay: UnusedOverlay(),
                imageSource: UnusedImageSource(),
                imageSink: new FakeImageSink(),
                settings: new FakeSettingsStore(),
                history: history,
                ui: ui
            );

            await coordinator.CaptureFullscreenAsync();

            Assert.Empty(history.All());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // MARK: - Self-timer / delayed capture

    [Fact]
    public async Task FullscreenWaitsTheConfiguredSelfTimerBeforeCapturing()
    {
        var settings = new FakeSettingsStore { CaptureDelay = 3 };
        var capture = new FakeCaptureService(SampleImage());
        var delay = new FakeDelaySpy();
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: settings,
            sleep: s => delay.SleepAsync(s),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync();

        Assert.Equal([3.0], delay.Waits);
        Assert.Single(capture.CapturedDisplays);
    }

    [Fact]
    public async Task AreaWaitsTheSelfTimerAfterResolvingTheRegion()
    {
        var settings = new FakeSettingsStore { CaptureDelay = 5 };
        var capture = new FakeCaptureService(SampleImage());
        var overlay = new FakeOverlayController(region: SampleRegion());
        var delay = new FakeDelaySpy();
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: overlay,
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: settings,
            sleep: s => delay.SleepAsync(s),
            ui: ui
        );

        await coordinator.CaptureAreaAsync();

        Assert.Equal(1, overlay.CallCount);
        Assert.Equal([5.0], delay.Waits);
        Assert.Equal([SampleRegion()], capture.CapturedRegions);
    }

    [Fact]
    public async Task WindowWaitsTheSelfTimerAfterPickingTheWindow()
    {
        var settings = new FakeSettingsStore { CaptureDelay = 2 };
        var capture = new FakeCaptureService(SampleImage());
        var overlay = new FakeOverlayController(windowRegion: SampleWindowRegion());
        var delay = new FakeDelaySpy();
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: overlay,
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: settings,
            sleep: s => delay.SleepAsync(s),
            ui: ui
        );

        await coordinator.CaptureWindowAsync();

        Assert.Equal([2.0], delay.Waits);
        Assert.Equal([SampleWindowRegion()], capture.CapturedRegions);
    }

    [Fact]
    public async Task AZeroSelfTimerSkipsTheWaitEntirely()
    {
        var settings = new FakeSettingsStore { CaptureDelay = 0 };
        var capture = new FakeCaptureService(SampleImage());
        var delay = new FakeDelaySpy();
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: settings,
            sleep: s => delay.SleepAsync(s),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync();

        Assert.Empty(delay.Waits);
        Assert.Single(capture.CapturedDisplays);
    }

    // MARK: - Display selection

    [Fact]
    public async Task FullscreenTargetsTheChosenDisplay()
    {
        var capture = new FakeCaptureService(SampleImage());
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync(42);

        Assert.Equal([(uint?)42], capture.CapturedDisplays);
    }

    [Fact]
    public async Task FullscreenDefaultsToThePrimaryDisplay()
    {
        var capture = new FakeCaptureService(SampleImage());
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync();

        Assert.Equal([(uint?)null], capture.CapturedDisplays);
    }

    // MARK: - Repeat last capture mode

    [Fact]
    public async Task RepeatWithNoPriorCaptureIsASilentNoOp()
    {
        var capture = new FakeCaptureService(SampleImage());
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.RepeatLastCaptureAsync();

        Assert.Empty(capture.CapturedDisplays);
        Assert.Empty(capture.CapturedRegions);
        Assert.Empty(ui.OpenedImages);
    }

    [Fact]
    public async Task RepeatReplaysFullscreenIncludingTheChosenDisplay()
    {
        var capture = new FakeCaptureService(SampleImage());
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: UnusedOverlay(),
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync(7);
        await coordinator.RepeatLastCaptureAsync();

        Assert.Equal([(uint?)7, (uint?)7], capture.CapturedDisplays);
    }

    [Fact]
    public async Task RepeatReplaysAreaThroughTheOverlayAgain()
    {
        var capture = new FakeCaptureService(SampleImage());
        var overlay = new FakeOverlayController(region: SampleRegion());
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: overlay,
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureAreaAsync();
        await coordinator.RepeatLastCaptureAsync();

        Assert.Equal(2, overlay.CallCount);
        Assert.Equal(2, capture.FreezeCount);
    }

    [Fact]
    public async Task RepeatUsesTheMostRecentModeNotAnEarlierOne()
    {
        var capture = new FakeCaptureService(SampleImage());
        var overlay = new FakeOverlayController(windowRegion: SampleWindowRegion());
        var ui = new FakeCaptureUI();
        var coordinator = new AppCoordinator(
            captureService: capture,
            overlay: overlay,
            imageSource: UnusedImageSource(),
            imageSink: new FakeImageSink(),
            settings: new FakeSettingsStore(),
            ui: ui
        );

        await coordinator.CaptureFullscreenAsync();
        await coordinator.CaptureWindowAsync();
        await coordinator.RepeatLastCaptureAsync();

        Assert.Equal(2, overlay.WindowCallCount);
        Assert.Equal([(uint?)null], capture.CapturedDisplays);
    }

    // MARK: - Freeze screen

    [Fact]
    public async Task AreaCaptureSelectsOverTheFrozenScreenAndOpensItsCrop()
    {
        var capture = new FakeCaptureService(SampleImage());
        var overlay = new FakeOverlayController(region: SampleRegion());
        var ui = new FakeCaptureUI();
        var coordinator = FrozenCoordinator(capture, overlay, ui: ui);

        await coordinator.CaptureAreaAsync();

        Assert.Equal(1, capture.FreezeCount);
        Assert.Equal([SampleFrozenScreen()], overlay.Backdrops);
        Assert.Empty(capture.CapturedRegions);
        var opened = Assert.Single(ui.OpenedImages);
        Assert.Equal(SampleFrozenScreen().ImageOf(SampleRegion()), opened);
        Assert.Equal(1280, opened.PixelWidth);
        Assert.Equal(960, opened.PixelHeight);
    }

    [Fact]
    public async Task AreaCaptureOfTheFrozenScreenShowsAQuickAccessCardAndIsRecordedInHistory()
    {
        var settings = new FakeSettingsStore { OpenInEditor = false };
        var (history, dir) = TempHistory();
        try
        {
            var overlay = new FakeOverlayController(region: SampleRegion());
            var ui = new FakeCaptureUI();
            var coordinator = FrozenCoordinator(
                new FakeCaptureService(SampleImage()),
                overlay,
                settings: settings,
                history: history,
                ui: ui
            );

            await coordinator.CaptureAreaAsync();

            var crop = SampleFrozenScreen().ImageOf(SampleRegion())!.Value;
            Assert.Equal(crop, ui.QuickAccess[0]);
            var records = history.All();
            Assert.Equal([1280], records.Select(r => r.PixelWidth));
            Assert.Equal(crop, history.CapturedImage(records[0])!.Value);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task AFailedFreezeRoutesLikeACaptureFailureAndNeverShowsTheOverlay()
    {
        var testCases = new (CaptureError Error, bool ExpectDenied, bool ExpectFailure)[]
        {
            (new CaptureError.PermissionDenied(), true, false),
            (new CaptureError.SystemFailure("boom"), false, true),
            (new CaptureError.UserCancelled(), false, false)
        };

        foreach (var (error, expectDenied, expectFailure) in testCases)
        {
            var overlay = new FakeOverlayController(region: SampleRegion(), windowRegion: SampleWindowRegion());
            var ui = new FakeCaptureUI();
            var capture = new FakeCaptureService(SampleImage(), freeze: Result<FrozenScreen, CaptureError>.Failure(error));
            var coordinator = FrozenCoordinator(capture, overlay, ui: ui);

            await coordinator.CaptureAreaAsync();
            await coordinator.CaptureWindowAsync();

            Assert.Empty(overlay.Backdrops);
            Assert.Empty(capture.CapturedRegions);
            Assert.Empty(ui.OpenedImages);
            if (expectDenied)
            {
                Assert.Equal([PermissionKind.ScreenRecording, PermissionKind.ScreenRecording], ui.DeniedKinds);
            }
            else
            {
                Assert.Empty(ui.DeniedKinds);
            }
            Assert.Equal(expectFailure ? 2 : 0, ui.Failures.Count);
        }
    }

    [Fact]
    public async Task ASelectionOffTheFrozenScreenIsAFailureNotABlankEditor()
    {
        var overlay = new FakeOverlayController(region: new CaptureRegion.RectRegion(new Rect(5000, 5000, 100, 100)));
        var ui = new FakeCaptureUI();
        var coordinator = FrozenCoordinator(
            new FakeCaptureService(SampleImage()),
            overlay,
            ui: ui
        );

        await coordinator.CaptureAreaAsync();

        Assert.Empty(ui.OpenedImages);
        Assert.Single(ui.Failures);
    }

    [Fact]
    public async Task EscapeOverTheFrozenScreenCapturesNothing()
    {
        var capture = new FakeCaptureService(SampleImage());
        var ui = new FakeCaptureUI();
        var (history, dir) = TempHistory();
        try
        {
            var coordinator = FrozenCoordinator(
                capture,
                new FakeOverlayController(region: null),
                history: history,
                ui: ui
            );

            await coordinator.CaptureAreaAsync();

            Assert.Empty(capture.CapturedRegions);
            Assert.Empty(ui.OpenedImages);
            Assert.Empty(ui.Failures);
            Assert.Empty(history.All());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task AreaCaptureWithASelfTimerSkipsTheFreezeAndCapturesLive()
    {
        var settings = new FakeSettingsStore { CaptureDelay = 3 };
        var capture = new FakeCaptureService(SampleImage());
        var overlay = new FakeOverlayController(region: SampleRegion());
        var delays = new FakeDelaySpy();
        var ui = new FakeCaptureUI();
        var coordinator = FrozenCoordinator(
            capture,
            overlay,
            settings: settings,
            delays: delays,
            ui: ui
        );

        await coordinator.CaptureAreaAsync();

        Assert.Equal(0, capture.FreezeCount);
        Assert.Equal([null], overlay.Backdrops);
        Assert.Equal([3.0], delays.Waits);
        Assert.Equal([SampleRegion()], capture.CapturedRegions);
        Assert.Equal([SampleImage()], ui.OpenedImages);
    }

    [Fact]
    public async Task WindowCaptureGivesThePickedWindowAsItWasAtTheFreeze()
    {
        var capture = new FakeCaptureService(SampleImage());
        var overlay = new FakeOverlayController(windowRegion: SampleWindowRegion());
        var ui = new FakeCaptureUI();
        var coordinator = FrozenCoordinator(capture, overlay, ui: ui);

        await coordinator.CaptureWindowAsync();

        Assert.Equal(1, capture.WindowImagesRequestCount);
        Assert.Equal([SampleFrozenScreen()], overlay.Backdrops);
        Assert.Empty(capture.CapturedRegions);
        var opened = Assert.Single(ui.OpenedImages);
        Assert.Equal(FrozenSampleWindowCapture(), opened);
        Assert.Equal(1600, opened.PixelWidth);
    }

    [Fact]
    public async Task AWindowWithNoFrozenImageIsCapturedLive()
    {
        var capture = new FakeCaptureService(SampleImage());
        capture.WindowImages = [];
        var ui = new FakeCaptureUI();
        var coordinator = FrozenCoordinator(
            capture,
            new FakeOverlayController(windowRegion: SampleWindowRegion()),
            ui: ui
        );

        await coordinator.CaptureWindowAsync();

        Assert.Equal([SampleWindowRegion()], capture.CapturedRegions);
        Assert.Equal([SampleImage()], ui.OpenedImages);
    }

    [Fact]
    public async Task WindowCaptureWithASelfTimerFreezesThePickerButCapturesLiveAfterTheWait()
    {
        var settings = new FakeSettingsStore { CaptureDelay = 2 };
        var capture = new FakeCaptureService(SampleImage());
        var overlay = new FakeOverlayController(windowRegion: SampleWindowRegion());
        var delays = new FakeDelaySpy();
        var ui = new FakeCaptureUI();
        var coordinator = FrozenCoordinator(
            capture,
            overlay,
            settings: settings,
            delays: delays,
            ui: ui
        );

        await coordinator.CaptureWindowAsync();

        Assert.Equal(0, capture.WindowImagesRequestCount);
        Assert.Equal([SampleFrozenScreen()], overlay.Backdrops);
        Assert.Equal([2.0], delays.Waits);
        Assert.Equal([SampleWindowRegion()], capture.CapturedRegions);
        Assert.Equal([SampleImage()], ui.OpenedImages);
    }

    [Fact]
    public async Task RepeatingAnAreaCaptureFreezesAfresh()
    {
        var capture = new FakeCaptureService(SampleImage());
        var ui = new FakeCaptureUI();
        var coordinator = FrozenCoordinator(capture, new FakeOverlayController(region: SampleRegion()), ui: ui);

        await coordinator.CaptureAreaAsync();
        await coordinator.RepeatLastCaptureAsync();

        Assert.Equal(2, capture.FreezeCount);
        Assert.Equal(2, ui.OpenedImages.Count);
    }

    [Fact]
    public async Task FullscreenNeverFreezes()
    {
        var capture = new FakeCaptureService(SampleImage());
        var ui = new FakeCaptureUI();
        var coordinator = FrozenCoordinator(capture, UnusedOverlay(), ui: ui);

        await coordinator.CaptureFullscreenAsync();

        Assert.Equal(0, capture.FreezeCount);
    }

    // MARK: - Adjust area before capture

    [Fact]
    public async Task AreaCaptureCapturesOnReleaseByDefault()
    {
        var overlay = new FakeOverlayController(region: SampleRegion());
        var ui = new FakeCaptureUI();
        var coordinator = FrozenCoordinator(new FakeCaptureService(SampleImage()), overlay, ui: ui);

        await coordinator.CaptureAreaAsync();

        Assert.Equal([false], overlay.Adjustables);
    }

    [Fact]
    public async Task WithTheSettingOnAreaCaptureAsksForAnAdjustableSelectionAndCapturesItAsUsual()
    {
        var settings = new FakeSettingsStore { AdjustAreaBeforeCapture = true };
        var (history, dir) = TempHistory();
        try
        {
            var capture = new FakeCaptureService(SampleImage());
            var overlay = new FakeOverlayController(region: SampleRegion());
            var ui = new FakeCaptureUI();
            var coordinator = FrozenCoordinator(capture, overlay, settings: settings, history: history, ui: ui);

            await coordinator.CaptureAreaAsync();

            Assert.Equal([true], overlay.Adjustables);
            Assert.Equal([SampleFrozenScreen()], overlay.Backdrops);
            Assert.Empty(capture.CapturedRegions);
            var crop = SampleFrozenScreen().ImageOf(SampleRegion())!.Value;
            Assert.Equal([crop], ui.OpenedImages);
            Assert.Equal([1280], history.All().Select(r => r.PixelWidth));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task WithTheSettingOnAnAdjustedAreaStillFollowsAfterCaptureToQuickAccess()
    {
        var settings = new FakeSettingsStore
        {
            AdjustAreaBeforeCapture = true,
            OpenInEditor = false
        };
        var overlay = new FakeOverlayController(region: SampleRegion());
        var ui = new FakeCaptureUI();
        var coordinator = FrozenCoordinator(new FakeCaptureService(SampleImage()), overlay, settings: settings, ui: ui);

        await coordinator.CaptureAreaAsync();

        Assert.Equal([true], overlay.Adjustables);
        Assert.Equal([SampleFrozenScreen().ImageOf(SampleRegion())!.Value], ui.QuickAccess);
        Assert.Empty(ui.OpenedImages);
    }

    [Fact]
    public async Task WithTheSettingOnASelfTimerAdjustsOverTheLiveScreenThenWaits()
    {
        var settings = new FakeSettingsStore
        {
            AdjustAreaBeforeCapture = true,
            CaptureDelay = 3
        };
        var capture = new FakeCaptureService(SampleImage());
        var overlay = new FakeOverlayController(region: SampleRegion());
        var delays = new FakeDelaySpy();
        var ui = new FakeCaptureUI();
        var coordinator = FrozenCoordinator(capture, overlay, settings: settings, delays: delays, ui: ui);

        await coordinator.CaptureAreaAsync();

        Assert.Equal([true], overlay.Adjustables);
        Assert.Equal([null], overlay.Backdrops);
        Assert.Equal([3.0], delays.Waits);
        Assert.Equal([SampleRegion()], capture.CapturedRegions);
        Assert.Equal([SampleImage()], ui.OpenedImages);
    }

    [Fact]
    public async Task WithTheSettingOnRepeatLastCaptureAdjustsToo()
    {
        var settings = new FakeSettingsStore { AdjustAreaBeforeCapture = true };
        var overlay = new FakeOverlayController(region: SampleRegion());
        var ui = new FakeCaptureUI();
        var coordinator = FrozenCoordinator(new FakeCaptureService(SampleImage()), overlay, settings: settings, ui: ui);

        await coordinator.CaptureAreaAsync();
        await coordinator.RepeatLastCaptureAsync();

        Assert.Equal([true, true], overlay.Adjustables);
    }

    [Fact]
    public async Task TheSettingIsReadLiveOnEachCapture()
    {
        var settings = new FakeSettingsStore();
        var overlay = new FakeOverlayController(region: SampleRegion());
        var ui = new FakeCaptureUI();
        var coordinator = FrozenCoordinator(new FakeCaptureService(SampleImage()), overlay, settings: settings, ui: ui);

        await coordinator.CaptureAreaAsync();
        settings.AdjustAreaBeforeCapture = true;
        await coordinator.CaptureAreaAsync();

        Assert.Equal([false, true], overlay.Adjustables);
    }

    [Fact]
    public async Task WithTheSettingOnCancellingCapturesNothing()
    {
        var settings = new FakeSettingsStore { AdjustAreaBeforeCapture = true };
        var (history, dir) = TempHistory();
        try
        {
            var capture = new FakeCaptureService(SampleImage());
            var ui = new FakeCaptureUI();
            var coordinator = FrozenCoordinator(capture, new FakeOverlayController(region: null), settings: settings, history: history, ui: ui);

            await coordinator.CaptureAreaAsync();

            Assert.Empty(capture.CapturedRegions);
            Assert.Empty(ui.OpenedImages);
            Assert.Empty(ui.QuickAccess);
            Assert.Empty(ui.Failures);
            Assert.Empty(history.All());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
