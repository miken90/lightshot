// Ported from LightshotKit/Tests/LightshotKitTests/RecordingCoordinatorTests.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Core.Tests.FakeServices;
using Lightshot.Rendering;
using Lightshot.TestSupport;
using Xunit;

namespace Lightshot.Rendering.Tests;

[Trait("Tier", "Render")]
public class RecordingCoordinatorTests
{
    private const string DefaultTake = "/tmp/scratch/take.mp4";
    private static readonly CaptureRegion.DisplayRegion DisplayRegion = new(7);

    private sealed class CoordinatorTestUI : ICaptureUI
    {
        private readonly FakeCaptureUI _inner = new();

        public Action? GifCancel { get; set; }
        public int GifDismissals { get; private set; }
        public bool HoldCancelResolution { get; set; }
        public TaskCompletionSource<bool>? CancelResolutionGate { get; private set; }
        public bool ResolveCancelledGifConversionResult { get; set; } = true;

        public int GifConversionsPresented => _inner.GifConversionsPresented;
        public bool GifConversionDismissed => _inner.GifConversionDismissed;
        public List<double> GifConversionProgresses => _inner.GifConversionProgresses;
        public List<PendingRecording> PostRecordings => _inner.PostRecordings;
        public List<string> FinishedRecordings => _inner.FinishedRecordings;
        public List<RecordingError> RecordingFailures => _inner.RecordingFailures;

        public void PresentGifConversion(Action cancel)
        {
            _inner.PresentGifConversion(cancel);
            GifCancel = cancel;
        }

        public void UpdateGifConversion(double progress) => _inner.UpdateGifConversion(progress);

        public void DismissGifConversion()
        {
            _inner.DismissGifConversion();
            GifDismissals++;
        }

        public async Task<bool> ResolveCancelledGifConversionAsync()
        {
            if (HoldCancelResolution)
            {
                CancelResolutionGate = new TaskCompletionSource<bool>();
                return await CancelResolutionGate.Task;
            }
            return ResolveCancelledGifConversionResult;
        }

        public void OpenEditor(CapturedImage image) => _inner.OpenEditor(image);
        public void PresentQuickAccess(CapturedImage image) => _inner.PresentQuickAccess(image);
        public void PresentPermissionDenied(PermissionKind kind) => _inner.PresentPermissionDenied(kind);
        public void PresentCaptureFailure(CaptureError error) => _inner.PresentCaptureFailure(error);
        public void PresentImageLoadFailure(ImageLoadError error) => _inner.PresentImageLoadFailure(error);
        public void PresentRecordingFailure(RecordingError error) => _inner.PresentRecordingFailure(error);
        public void PresentRecordingState(RecordingSession session) => _inner.PresentRecordingState(session);
        public Task<bool> RunRecordingCountdownAsync(int seconds) => _inner.RunRecordingCountdownAsync(seconds);
        public Task<bool> ConfirmRecordingRestartAsync() => _inner.ConfirmRecordingRestartAsync();
        public Task<bool> ConfirmRecordingDiscardAsync() => _inner.ConfirmRecordingDiscardAsync();
        public Task<bool> ResolveMicrophoneDisconnectedAsync() => _inner.ResolveMicrophoneDisconnectedAsync();
        public void PresentRecordingFinished(string path) => _inner.PresentRecordingFinished(path);
        public void PresentPostRecordingOverlay(PendingRecording recording) => _inner.PresentPostRecordingOverlay(recording);
        public void OpenVideoEditor(string path, string? inputPath = null) => _inner.OpenVideoEditor(path, inputPath);
        public void PresentRecordingPreparation(Action cancel) => _inner.PresentRecordingPreparation(cancel);
        public void UpdateRecordingPreparation(double progress) => _inner.UpdateRecordingPreparation(progress);
        public void DismissRecordingPreparation() => _inner.DismissRecordingPreparation();
    }

    private sealed class Harness : IDisposable
    {
        public FakeRecordingService Service { get; }
        public FakeMediaSink Sink { get; } = new();
        public FakeGifEncoder Gif { get; } = new();
        public CoordinatorTestUI Ui { get; } = new();
        public HistoryStore? History { get; }
        public string HistoryDirectory { get; }
        public FakeSettingsStore Settings { get; } = new();
        public FakeCaptureService Capture { get; } = new();
        public FakeOverlayController Overlay { get; }
        public AppCoordinator Coordinator { get; }
        public double Now { get; set; } = 100;
        public List<double> Waits { get; } = [];

        public Harness(
            FakeRecordingService? service = null,
            bool withHistory = false,
            VideoMetadata? metadata = null,
            IMediaMetadataSource? metadataSource = null)
        {
            Service = service ?? new FakeRecordingService();
            HistoryDirectory = Path.Combine(Path.GetTempPath(), $"coord-render-hist-{Guid.NewGuid():N}").Replace('\\', '/');
            var thumbnailer = new SkiaThumbnailer();
            History = withHistory ? new HistoryStore(HistoryDirectory, thumbnailer: thumbnailer) : null;
            Overlay = new FakeOverlayController(recordingChoice: new RecordingChoice(DisplayRegion, RecordingOutputKind.Video));

            Settings.SaveLocation = "/tmp/movies";
            Settings.FilenamePattern = "Recording %Y";
            Settings.RecordingDefaults = Settings.RecordingDefaults with
            {
                CountdownEnabled = false,
                AfterRecording = AfterRecordingAction.SaveSilently
            };

            var meta = metadataSource ?? (withHistory ? new FakeMediaMetadataSource { Result = metadata ?? new VideoMetadata(1280, 720, 30.0, new byte[] { 1, 2, 3 }) } : null);

            Coordinator = new AppCoordinator(
                captureService: Capture,
                overlay: Overlay,
                imageSource: new FakeImageSource(Result<CapturedImage, ImageLoadError>.Failure(new ImageLoadError.UserCancelled())),
                imageSink: new FakeImageSink(),
                settings: Settings,
                history: History,
                recordingService: Service,
                mediaSink: Sink,
                gifEncoder: Gif,
                mediaMetadata: meta,
                scratchDirectory: Path.GetTempPath().Replace('\\', '/'),
                sleep: seconds =>
                {
                    Waits.Add(seconds);
                    Now += seconds;
                    return Task.CompletedTask;
                },
                clock: () => Now,
                ui: Ui
            );
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(HistoryDirectory))
                {
                    Directory.Delete(HistoryDirectory, true);
                }
            }
            catch { }
        }
    }

    private static string MaterialisedTake()
    {
        var path = Path.Combine(Path.GetTempPath(), $"take-{Guid.NewGuid():N}.mp4").Replace('\\', '/');
        File.WriteAllBytes(path, [9, 9, 9]);
        return path;
    }

    private static void CreateSmallGifFixture(string path, int width, int height, double delaySec)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            rgba[i * 4] = 255;
            rgba[i * 4 + 3] = 255;
        }
        var frame = GifQuantizer.Quantize(rgba, width, height, isBgra: false, maxColors: 256);
        using var fs = File.Create(path);
        using var writer = new GifWriter(fs, width, height, loopCount: 0, leaveOpen: false);
        writer.WriteFrame(frame, delayCentiseconds: (ushort)Math.Round(delaySec * 100));
    }

    private static async Task FinishedGifTakeAsync(Harness h)
    {
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with
        {
            AfterRecording = AfterRecordingAction.ShowOverlay
        };
        await h.Coordinator.StartRecordingAsync(DisplayRegion, RecordingOutputKind.Gif);
        h.Now = 130;
        await h.Coordinator.StopRecordingAsync();
    }

    [Fact]
    [Render]
    public async Task AGIFTakeIsConvertedThenRoutedAsAGIFAndTheVideoIsDeleted()
    {
        using var h = new Harness();
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with
        {
            Gif = new GIFSettings(10, 0.5, 320, false)
        };
        await FinishedGifTakeAsync(h);

        Assert.Single(h.Gif.Encodes);
        Assert.Equal(DefaultTake, h.Gif.Encodes[0].Video);
        Assert.Equal(Path.ChangeExtension(DefaultTake, "gif").Replace('\\', '/'), h.Gif.Encodes[0].Output);
        Assert.Equal(h.Settings.RecordingDefaults.Gif, h.Gif.Encodes[0].Settings);
        Assert.Equal(1, h.Ui.GifConversionsPresented);
        Assert.True(h.Ui.GifConversionDismissed);
        Assert.Contains(DefaultTake, h.Sink.Deleted);
        Assert.Single(h.Ui.PostRecordings);
        Assert.Equal(RecordingOutputKind.Gif, h.Ui.PostRecordings[0].Kind);
        Assert.Equal(30.0, h.Ui.PostRecordings[0].Duration);
        Assert.Empty(h.Ui.RecordingFailures);
    }

    [Fact]
    [Render]
    public async Task ProgressReachesThePopupOnTheMainActor()
    {
        using var h = new Harness();
        await FinishedGifTakeAsync(h);
        Assert.Contains(0.5, h.Ui.GifConversionProgresses);
    }

    [Fact]
    [Render]
    public async Task CancellingOffersTheVideoInsteadOrDeletesTheTake()
    {
        using var h = new Harness();
        h.Gif.Holds = true;

        var take1 = Task.Run(async () => await FinishedGifTakeAsync(h), TestContext.Current.CancellationToken);
        while (h.Ui.GifCancel == null) await Task.Yield();

        // Mid-conversion the recorder is busy: a new take is refused
        await h.Coordinator.StartRecordingAsync(DisplayRegion, RecordingOutputKind.Video);
        Assert.Single(h.Service.Starts);

        h.Ui.GifCancel(); // Cancel in the popup
        await take1;

        Assert.Equal(RecordingOutputKind.Video, h.Ui.PostRecordings[^1].Kind); // Keep the video instead
        Assert.Empty(h.Sink.Deleted);

        // Second run: KeepVideoOnCancel = false
        h.Ui.ResolveCancelledGifConversionResult = false;
        h.Ui.GifCancel = null;
        var take2 = Task.Run(async () => await FinishedGifTakeAsync(h), TestContext.Current.CancellationToken);
        while (h.Ui.GifCancel == null) await Task.Yield();
        h.Ui.GifCancel();
        await take2;

        Assert.Single(h.Ui.PostRecordings); // nothing new routed
        Assert.Contains(DefaultTake, h.Sink.Deleted);
        Assert.Equal(2, h.Ui.GifDismissals);
    }

    [Fact]
    [Render]
    public async Task AFailedConversionSurfacesAndFallsBackToTheVideo()
    {
        using var h = new Harness();
        h.Gif.Fails = true;
        await FinishedGifTakeAsync(h);

        Assert.Single(h.Ui.RecordingFailures);
        Assert.Equal(RecordingOutputKind.Video, h.Ui.PostRecordings[^1].Kind);
        Assert.Empty(h.Sink.Deleted);
    }

    [Fact]
    [Render]
    public async Task AFinishedGIFJoinsHistoryAndSilentSaveCopiesItOut()
    {
        var take = MaterialisedTake();
        var gifPath = Path.ChangeExtension(take, "gif");
        CreateSmallGifFixture(gifPath, 4, 4, 0.2);

        using var h = new Harness(
            service: new FakeRecordingService((take, null)),
            withHistory: true);

        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with
        {
            AfterRecording = AfterRecordingAction.SaveSilently
        };

        await h.Coordinator.StartRecordingAsync(DisplayRegion, RecordingOutputKind.Gif);
        h.Now = 130;
        await h.Coordinator.StopRecordingAsync();

        var record = h.History!.All().FirstOrDefault();
        Assert.NotNull(record);
        Assert.Equal(CaptureKind.Gif, record.Kind);
        Assert.Equal(4, record.PixelWidth);
        Assert.True(Math.Abs((record.Duration ?? 0) - 0.2) < 0.01);
        Assert.Single(h.Sink.Copies);
        Assert.Equal(record.FileUrl.Replace('\\', '/'), h.Sink.Copies[0].Source);
        Assert.Empty(h.Sink.Saves);
        Assert.Single(h.Ui.FinishedRecordings);
    }

    [Fact]
    [Render]
    public async Task AGIFConversionAndTheKeepTheVideoQuestionAreWork()
    {
        using var h = new Harness();
        h.Gif.Holds = true;
        h.Ui.HoldCancelResolution = true;

        var finishing = Task.Run(async () => await FinishedGifTakeAsync(h), TestContext.Current.CancellationToken);
        while (h.Ui.GifCancel == null) await Task.Yield();

        Assert.True(h.Coordinator.HasWorkInProgress); // converting

        h.Ui.GifCancel();
        while (h.Ui.CancelResolutionGate == null) await Task.Yield();

        Assert.True(h.Coordinator.HasWorkInProgress); // waiting on user's answer

        h.Ui.CancelResolutionGate.TrySetResult(true);
        await finishing;

        Assert.Equal(RecordingOutputKind.Video, h.Ui.PostRecordings[^1].Kind);
        Assert.False(h.Coordinator.HasWorkInProgress);
    }
}
