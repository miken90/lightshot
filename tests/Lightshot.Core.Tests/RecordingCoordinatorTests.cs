// Ported from LightshotKit/Tests/LightshotKitTests/RecordingCoordinatorTests.swift
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
public class RecordingCoordinatorTests
{
    private const string DefaultTake = "/tmp/scratch/take.mp4";
    private static readonly CaptureRegion.DisplayRegion DisplayRegion = new(7);

    private sealed class Harness : IDisposable
    {
        public FakeRecordingService Service { get; }
        public FakeMediaSink Sink { get; } = new();
        public FakeGifEncoder Gif { get; } = new();
        public FakeCaptureUI Ui { get; } = new();
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
            HistoryDirectory = Path.Combine(Path.GetTempPath(), $"coordinator-history-{Guid.NewGuid():N}").Replace('\\', '/');
            History = withHistory ? new HistoryStore(HistoryDirectory) : null;
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

    private static async Task FinishedTakeAsync(Harness h, AfterRecordingAction after)
    {
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with { AfterRecording = after };
        await h.Coordinator.ToggleRecordingAsync();
        h.Now = 130;
        await h.Coordinator.ToggleRecordingAsync();
    }

    // MARK: - Start / stop (stories 1, 2, 6)

    [Fact]
    public async Task ToggleStartsAVideoRecordingOfTheDisplayAndReportsTheState()
    {
        using var h = new Harness();
        await h.Coordinator.ToggleRecordingAsync();

        Assert.True(h.Coordinator.IsRecording);
        Assert.Single(h.Service.Starts);
        Assert.Equal(DisplayRegion, h.Service.Starts[0].Options.Region);
        Assert.Equal(RecordingOutputKind.Video, h.Service.Starts[0].Options.Output.Kind);
        Assert.Equal(".mp4", Path.GetExtension(h.Service.Starts[0].OutputPath));
        Assert.Equal([RecordingSession.State.RecordingState], h.Ui.States);
        Assert.Empty(h.Waits); // no countdown configured
    }

    [Fact]
    public async Task ATakeHidesWhatTheSettingsAskFor()
    {
        using var h = new Harness();
        h.Settings.HideDesktopIcons = true;
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with { HideNotifications = true };
        await h.Coordinator.ToggleRecordingAsync();

        Assert.True(h.Service.Starts[0].Options.HideDesktopIcons);
        Assert.True(h.Service.Starts[0].Options.HideNotifications);
    }

    [Fact]
    public async Task ATakeHidesNothingByDefault()
    {
        using var h = new Harness();
        await h.Coordinator.ToggleRecordingAsync();

        Assert.False(h.Service.Starts[0].Options.HideDesktopIcons);
        Assert.False(h.Service.Starts[0].Options.HideNotifications);
    }

    [Fact]
    public async Task ToggleAgainStopsFinishesAndSavesToTheDefaultDestination()
    {
        using var h = new Harness();
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with { AfterRecording = AfterRecordingAction.SaveSilently };
        await h.Coordinator.ToggleRecordingAsync();
        h.Now = 130;
        await h.Coordinator.ToggleRecordingAsync();

        Assert.False(h.Coordinator.IsRecording);
        Assert.Equal(1, h.Service.StopCount);
        Assert.Equal(new RecordingSession.State.Finished(DefaultTake), h.Coordinator.RecordingSession.CurrentState);
        Assert.Equal(30, h.Coordinator.RecordingElapsed);
        Assert.Single(h.Sink.Saves);
        Assert.Equal(DefaultTake, h.Sink.Saves[0].Source);

        var destination = h.Sink.Saves[0].Destination;
        Assert.Equal("/tmp/movies", Path.GetDirectoryName(destination)?.Replace('\\', '/'));
        Assert.Equal(".mp4", Path.GetExtension(destination));
        Assert.StartsWith("Recording ", Path.GetFileName(destination));
        Assert.Equal(h.Sink.Saves.Select(s => s.Destination), h.Ui.FinishedRecordings);
        Assert.Empty(h.Ui.PostRecordings);
        Assert.Empty(h.Ui.VideoEditorOpens);
        Assert.Equal([RecordingSession.State.RecordingState, RecordingSession.State.StoppingState, new RecordingSession.State.Finished(DefaultTake)], h.Ui.States);
    }

    [Fact]
    public async Task TheDefaultShowsTheOverlayAndKeepsTheTakeInScratchUntilItDecides()
    {
        using var h = new Harness();
        await FinishedTakeAsync(h, AfterRecordingAction.ShowOverlay);
        Assert.Empty(h.Sink.Saves); // nothing moved yet
        Assert.Empty(h.Ui.FinishedRecordings);
        Assert.Equal([new PendingRecording(DefaultTake, RecordingOutputKind.Video, 30)], h.Ui.PostRecordings);
        Assert.Equal(DefaultTake, h.Coordinator.PendingRecording?.File);
    }

    [Fact]
    public async Task OpenEditorSavesFirstThenOpensTheEditor()
    {
        using var h = new Harness();
        await FinishedTakeAsync(h, AfterRecordingAction.OpenEditor);
        Assert.Single(h.Sink.Saves);
        Assert.Equal(h.Sink.Saves.Select(s => (s.Destination, (string?)null)), h.Ui.VideoEditorOpens);
        Assert.Empty(h.Ui.PostRecordings);
        Assert.Empty(h.Ui.FinishedRecordings);
    }

    [Fact]
    public async Task TheOverlaysActionsCopySaveRenameAndDismiss()
    {
        using var h = new Harness();
        await FinishedTakeAsync(h, AfterRecordingAction.ShowOverlay);

        // Copy file saves first and copies the saved file's reference, never the scratch one.
        var copied = h.Coordinator.CopyPendingRecordingFile("clip");
        Assert.Equal("/tmp/movies/clip.mp4", copied);
        Assert.Equal(["/tmp/movies/clip.mp4"], h.Sink.Copied);
        Assert.Null(h.Coordinator.PendingRecording);

        // Rename then Save: the name replaces the pattern, the extension stays the file's own;
        // separators cannot escape the save folder.
        await FinishedTakeAsync(h, AfterRecordingAction.ShowOverlay);
        var saved = h.Coordinator.SavePendingRecording("../demo:take/1");
        Assert.Equal("/tmp/movies/-demo-take-1.mp4", saved);
        Assert.Equal([DefaultTake, DefaultTake], h.Sink.Saves.Select(s => s.Source));
        Assert.Null(h.Coordinator.PendingRecording);

        // A dismissal keeps the file — under the pattern, or the name typed so far.
        await FinishedTakeAsync(h, AfterRecordingAction.ShowOverlay);
        h.Coordinator.DismissPendingRecording();
        Assert.StartsWith("Recording ", Path.GetFileName(h.Sink.Saves[^1].Destination));

        await FinishedTakeAsync(h, AfterRecordingAction.ShowOverlay);
        h.Coordinator.DismissPendingRecording("typed");
        Assert.Equal("typed.mp4", Path.GetFileName(h.Sink.Saves[^1].Destination));
        Assert.Null(h.Coordinator.PendingRecording);

        // Nothing pending: the actions are no-ops.
        Assert.Null(h.Coordinator.CopyPendingRecordingFile());
        h.Coordinator.DismissPendingRecording();
        Assert.Single(h.Sink.Copied);
        Assert.Equal(4, h.Sink.Saves.Count);
    }

    [Fact]
    public async Task AGIFNeverGoesToTheVideoEditor()
    {
        using var h = new Harness();
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with { AfterRecording = AfterRecordingAction.OpenEditor };
        await h.Coordinator.StartRecordingAsync(DisplayRegion, RecordingOutputKind.Gif);
        await h.Coordinator.StopRecordingAsync();

        Assert.Empty(h.Ui.VideoEditorOpens);
        Assert.Single(h.Ui.FinishedRecordings);
        Assert.Equal(".gif", Path.GetExtension(h.Ui.FinishedRecordings[0]));
    }

    [Fact]
    public async Task AVideoTakeNeverTouchesTheEncoder()
    {
        using var h = new Harness();
        await FinishedTakeAsync(h, AfterRecordingAction.ShowOverlay);
        Assert.Empty(h.Gif.Encodes);
        Assert.Equal(0, h.Ui.GifConversionsPresented);
    }

    [Fact]
    public async Task OpenEditorFromTheOverlaySavesThenOpensTheEditor()
    {
        using var h = new Harness();
        await FinishedTakeAsync(h, AfterRecordingAction.ShowOverlay);
        var saved = h.Coordinator.OpenPendingRecordingInEditor("cut me");
        Assert.Equal("cut me.mp4", Path.GetFileName(saved));
        Assert.Equal([(saved!, (string?)null)], h.Ui.VideoEditorOpens);
        Assert.Null(h.Coordinator.PendingRecording);
        Assert.Null(h.Coordinator.OpenPendingRecordingInEditor()); // nothing pending
    }

    [Fact]
    public async Task AFinishedVideoJoinsHistoryAndTheOverlayGetsHistorysFile()
    {
        var take = MaterialisedTake();
        using var h = new Harness(service: new FakeRecordingService((take, null)), withHistory: true);
        await FinishedTakeAsync(h, AfterRecordingAction.ShowOverlay);

        var records = h.History!.All();
        Assert.Single(records);
        var record = records[0];
        Assert.Equal(CaptureKind.Video, record.Kind);
        Assert.Equal(CaptureSource.Recording, record.Source);
        Assert.Equal(30.0, record.Duration);
        Assert.Equal(1280, record.PixelWidth);
        Assert.Equal(720, record.PixelHeight);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(record.ThumbnailUrl));
        Assert.False(File.Exists(take)); // moved into history

        var lastOverlay = h.Ui.PostRecordings[^1];
        Assert.Equal(record.FileUrl, lastOverlay.File);
        Assert.IsType<RecordingOrigin.FreshInHistory>(lastOverlay.Origin);
        Assert.Equal(record.Id, ((RecordingOrigin.FreshInHistory)lastOverlay.Origin).Id);
        Assert.StartsWith("Recording ", lastOverlay.SuggestedName);

        // Save copies history's file out — history keeps the original.
        var saved = h.Coordinator.SavePendingRecording("kept");
        Assert.Equal("kept.mp4", Path.GetFileName(saved));
        Assert.Equal([record.FileUrl], h.Sink.Copies.Select(c => c.Source));
        Assert.Empty(h.Sink.Saves);
        Assert.NotNull(h.History.Record(record.Id));
    }

    [Fact]
    public async Task UnreadableMetadataLeavesTheTakeInScratchAndRoutesItAnyway()
    {
        var take = MaterialisedTake();
        using var h = new Harness(service: new FakeRecordingService((take, null)), withHistory: true, metadataSource: new FakeMediaMetadataSource { Result = null });
        await FinishedTakeAsync(h, AfterRecordingAction.ShowOverlay);

        Assert.Empty(h.History!.All());
        Assert.Equal(take, h.Ui.PostRecordings[^1].File);
        Assert.IsType<RecordingOrigin.Scratch>(h.Ui.PostRecordings[^1].Origin);
        Assert.True(File.Exists(take));
        try { File.Delete(take); } catch { }
    }

    [Fact]
    public async Task DeletingAHistoryOwnedTakeRemovesItsRecord()
    {
        var take = MaterialisedTake();
        using var h = new Harness(service: new FakeRecordingService((take, null)), withHistory: true);
        await FinishedTakeAsync(h, AfterRecordingAction.ShowOverlay);

        var record = h.History!.All()[0];
        Assert.True(h.Coordinator.DeletePendingRecording());
        Assert.Empty(h.History!.All());
        Assert.False(File.Exists(record.FileUrl));
        Assert.Empty(h.Sink.Trashed); // history's file, not the Trash
    }

    [Fact]
    public void ReopeningFromHistoryRoutesByKindAndADismissedGIFStaysPut()
    {
        using var h = new Harness(withHistory: true);
        var video = new CaptureRecord(
            Guid.NewGuid(), DateTime.UtcNow, CaptureSource.Recording, CaptureKind.Video,
            1, 1, 3.0, "/tmp/h/v.mp4", "/tmp/h/v.png");
        h.Coordinator.ReopenRecording(video);

        // A video is copied out first; the editor gets the copy, never history's file.
        Assert.Equal([video.FileUrl], h.Sink.Copies.Select(c => c.Source));
        Assert.Equal(h.Sink.Copies.Select(c => (c.Destination, (string?)null)), h.Ui.VideoEditorOpens);
        Assert.Equal(".mp4", Path.GetExtension(h.Ui.VideoEditorOpens[0].Path));
        Assert.Null(h.Coordinator.PendingRecording);

        var gif = new CaptureRecord(
            Guid.NewGuid(), DateTime.UtcNow, CaptureSource.Recording, CaptureKind.Gif,
            1, 1, 2.0, "/tmp/h/g.gif", "/tmp/h/g.png");
        h.Coordinator.ReopenRecording(gif);
        Assert.IsType<RecordingOrigin.HistoryItem>(h.Ui.PostRecordings[^1].Origin);
        Assert.Equal(gif.Id, ((RecordingOrigin.HistoryItem)h.Ui.PostRecordings[^1].Origin).Id);
        Assert.False(h.Ui.PostRecordings[^1].IsNew);

        h.Coordinator.DismissPendingRecording();
        Assert.Single(h.Sink.Copies);
        Assert.Empty(h.Sink.Saves);
        Assert.Null(h.Coordinator.PendingRecording); // already kept

        h.Coordinator.ReopenRecording(gif);
        Assert.Equal("again.gif", Path.GetFileName(h.Coordinator.SavePendingRecording("again")));
        Assert.Equal(2, h.Sink.Copies.Count); // Save copies it out

        var shot = new CaptureRecord(
            Guid.NewGuid(), DateTime.UtcNow, CaptureSource.Area, CaptureKind.Screenshot,
            1, 1, null, "/tmp/h/s.png", "/tmp/h/s.png");
        h.Coordinator.ReopenRecording(shot);
        Assert.Equal(2, h.Ui.PostRecordings.Count);
        Assert.Single(h.Ui.VideoEditorOpens); // not the coordinator's job
    }

    [Fact]
    public async Task ARecoveredTakeJoinsHistoryToo()
    {
        var take = MaterialisedTake();
        using var h = new Harness(withHistory: true);
        var recovered = await h.Coordinator.ArchiveRecoveredRecordingAsync(take);

        Assert.Equal(RecordingOutputKind.Video, recovered.Kind);
        Assert.Equal(30.0, recovered.Duration);
        Assert.Single(h.History!.All());
        Assert.IsType<RecordingOrigin.FreshInHistory>(recovered.Origin);
        Assert.Equal(h.History.All()[0].Id, ((RecordingOrigin.FreshInHistory)recovered.Origin).Id);
        Assert.False(File.Exists(take));
    }

    [Fact]
    public async Task ReopeningAGIFKeepsAFreshTakeFirst()
    {
        var take = MaterialisedTake();
        using var h = new Harness(service: new FakeRecordingService((take, null)), withHistory: true);
        await FinishedTakeAsync(h, AfterRecordingAction.ShowOverlay);

        var gif = new CaptureRecord(
            Guid.NewGuid(), DateTime.UtcNow, CaptureSource.Recording, CaptureKind.Gif,
            1, 1, 2.0, "/tmp/h/g.gif", "/tmp/h/g.png");
        h.Coordinator.ReopenRecording(gif);

        Assert.Single(h.Sink.Copies); // the fresh take was saved, not dropped
        Assert.IsType<RecordingOrigin.HistoryItem>(h.Coordinator.PendingRecording?.Origin);
        Assert.Equal(gif.Id, ((RecordingOrigin.HistoryItem)h.Coordinator.PendingRecording!.Origin).Id);
    }

    [Fact]
    public async Task ANewTakeKeepsThePendingOneBeforeItStarts()
    {
        using var h = new Harness();
        await FinishedTakeAsync(h, AfterRecordingAction.ShowOverlay);
        Assert.Empty(h.Sink.Saves);

        await h.Coordinator.ToggleRecordingAsync(); // the app moves on
        Assert.Single(h.Sink.Saves);
        Assert.Equal(DefaultTake, h.Sink.Saves[0].Source);
        Assert.True(h.Coordinator.IsRecording);
        Assert.Null(h.Coordinator.PendingRecording);
    }

    [Fact]
    public async Task DeleteTrashesTheTakeAndAFailureKeepsItPending()
    {
        using var h = new Harness();
        await FinishedTakeAsync(h, AfterRecordingAction.ShowOverlay);
        h.Sink.TrashFails = true;
        Assert.False(h.Coordinator.DeletePendingRecording());
        Assert.Empty(h.Sink.Trashed);
        Assert.NotNull(h.Coordinator.PendingRecording);
        Assert.Single(h.Ui.RecordingFailures);

        h.Sink.TrashFails = false;
        Assert.True(h.Coordinator.DeletePendingRecording());
        Assert.Equal([DefaultTake], h.Sink.Trashed);
        Assert.Null(h.Coordinator.PendingRecording);
        Assert.Empty(h.Sink.Saves);
    }

    [Fact]
    public async Task AFailedSaveFromTheOverlayKeepsTheTakePending()
    {
        using var h = new Harness();
        await FinishedTakeAsync(h, AfterRecordingAction.ShowOverlay);
        h.Sink.SaveFails = true;
        Assert.Null(h.Coordinator.SavePendingRecording());
        Assert.NotNull(h.Coordinator.PendingRecording);
        Assert.Single(h.Ui.RecordingFailures);

        h.Sink.SaveFails = false;
        Assert.NotNull(h.Coordinator.SavePendingRecording());
    }

    [Fact]
    public void AnEmptyOrDottedRenameFallsBackToThePattern()
    {
        var s = new FakeSettingsStore();
        s.SaveLocation = "/tmp/movies";
        s.FilenamePattern = "Recording %Y";

        var dest1 = s.RecordingDestination("   ", "mp4");
        Assert.StartsWith("Recording ", Path.GetFileName(dest1));

        var dest2 = s.RecordingDestination("...", "mp4");
        Assert.StartsWith("Recording ", Path.GetFileName(dest2));

        var dest3 = s.RecordingDestination(".hidden", "mov");
        Assert.Equal("hidden.mov", Path.GetFileName(dest3));

        var dest4 = s.RecordingDestination("name.", "mp4");
        Assert.Equal("name.mp4", Path.GetFileName(dest4));

        Assert.Equal("a-b-c", FilenameFormatter.Sanitized("a/b:c"));
    }

    [Fact]
    public async Task ASecondStartWhileActiveAndAStopWhileIdleAreNoOps()
    {
        using var h = new Harness();
        await h.Coordinator.StopRecordingAsync();
        Assert.Equal(0, h.Service.StopCount);

        await h.Coordinator.StartRecordingAsync(DisplayRegion);
        await h.Coordinator.StartRecordingAsync(DisplayRegion);
        Assert.Single(h.Service.Starts);
    }

    [Fact]
    public async Task ElapsedTimeFollowsTheClockWhileRecording()
    {
        using var h = new Harness();
        await h.Coordinator.StartRecordingAsync(DisplayRegion);
        h.Now = 112.5;
        Assert.Equal(12.5, h.Coordinator.RecordingElapsed);
    }

    [Fact]
    public async Task AStopWhileStartIsStillInFlightIsIgnored()
    {
        using var h = new Harness();
        h.Service.HoldStart = true;
        var starting = Task.Run(async () => await h.Coordinator.StartRecordingAsync(DisplayRegion), TestContext.Current.CancellationToken);
        while (h.Service.StartGate == null) await Task.Yield();

        await h.Coordinator.ToggleRecordingAsync(); // arrives mid-start
        Assert.Equal(0, h.Service.StopCount);
        Assert.True(h.Coordinator.IsRecording);

        h.Service.StartGate.TrySetResult(true);
        await starting;
        Assert.Equal(RecordingSession.State.RecordingState, h.Coordinator.RecordingSession.CurrentState);

        await h.Coordinator.StopRecordingAsync(); // a normal stop still works afterwards
        Assert.Equal(1, h.Service.StopCount);
    }

    [Fact]
    public async Task AStopDuringTheCountdownDiscardsAndDeletesTheScratchFile()
    {
        using var h = new Harness();
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with { CountdownEnabled = true, CountdownSeconds = 3 };
        h.Ui.HoldCountdown = true;
        var starting = Task.Run(async () => await h.Coordinator.StartRecordingAsync(DisplayRegion), TestContext.Current.CancellationToken);
        while (h.Ui.CountdownGate == null) await Task.Yield();
        Assert.Equal(RecordingSession.State.CountdownState, h.Coordinator.RecordingSession.CurrentState);

        await h.Coordinator.StopRecordingAsync(); // hotkey during the countdown
        h.Ui.CountdownGate.TrySetResult(true);
        await starting;

        Assert.Equal(1, h.Service.CancelCount);
        Assert.Equal(0, h.Service.StopCount);
        Assert.Empty(h.Service.Starts); // the stream never started
        Assert.Equal(RecordingSession.State.IdleState, h.Coordinator.RecordingSession.CurrentState);
        Assert.Equal(RecordingSession.State.IdleState, h.Ui.States[^1]);
    }

    [Fact]
    public async Task AStreamDyingMidTakeFailsTheSessionDeletesThePartialAndRestoresTheStatusItem()
    {
        using var h = new Harness();
        await h.Coordinator.StartRecordingAsync(DisplayRegion);
        h.Service.OnEvent?.Invoke(new RecordingEvent.Failed(new RecordingError.SystemFailure("display disconnected")));
        await Task.Yield();
        while (h.Coordinator.IsRecording) await Task.Yield();

        Assert.Equal(new RecordingSession.State.Failed(new RecordingError.SystemFailure("display disconnected")), h.Coordinator.RecordingSession.CurrentState);
        Assert.Equal([new RecordingError.SystemFailure("display disconnected")], h.Ui.RecordingFailures);
        Assert.Equal(new RecordingSession.State.Failed(new RecordingError.SystemFailure("display disconnected")), h.Ui.States[^1]);
        Assert.Equal(0, h.Service.StopCount);
    }

    [Fact]
    public async Task RecordScreenRunsTheOverlayFirstAndStartsOnItsRegion()
    {
        using var h = new Harness();
        var rect = new CaptureRegion.RectRegion(new Rect(10, 20, 640, 360));
        h.Overlay.RecordingChoiceResult = new RecordingChoice(rect, RecordingOutputKind.Video);
        await h.Coordinator.RecordScreenAsync();

        Assert.Equal([null], h.Overlay.RecordingCalls.Select(c => c.Initial));
        Assert.Equal([h.Settings.RecordingDefaults], h.Overlay.RecordingCalls.Select(c => c.Defaults));
        Assert.Equal(rect, h.Service.Starts[0].Options.Region);
        Assert.True(h.Coordinator.IsRecording);
    }

    [Fact]
    public async Task TheToolbarsOutputAndTogglesReachTheServiceWithoutTouchingSettings()
    {
        using var h = new Harness();
        h.Overlay.RecordingChoiceResult = new RecordingChoice(
            DisplayRegion, RecordingOutputKind.Gif, new RecordingOverrides { Microphone = true, HighlightClicks = true });
        await h.Coordinator.RecordScreenAsync();

        var options = h.Service.Starts[0].Options;
        Assert.Equal(RecordingOutputKind.Gif, options.Output.Kind);
        Assert.Equal(new InputDeviceSelection.Device(null), options.Microphone);
        Assert.True(options.HighlightClicks);
        Assert.False(options.ComputerAudio); // untouched toggles keep default
        Assert.Equal(new RecordingDefaults { CountdownEnabled = false, AfterRecording = AfterRecordingAction.SaveSilently }, h.Settings.RecordingDefaults); // not written back

        // A GIF take is converted and the GIF is what gets saved.
        await h.Coordinator.StopRecordingAsync();
        Assert.Equal(".gif", Path.GetExtension(h.Sink.Saves[0].Destination));
    }

    [Fact]
    public async Task EscapeInTheOverlayIsASilentNoOp()
    {
        using var h = new Harness();
        h.Overlay.RecordingChoiceResult = null;
        await h.Coordinator.RecordScreenAsync();

        Assert.Empty(h.Service.Starts);
        Assert.False(h.Coordinator.IsRecording);
        Assert.Empty(h.Ui.States);
        Assert.Null(h.Settings.LastRecordingRegion);
    }

    [Fact]
    public async Task TheChosenRegionIsRememberedAndPreFilledOnlyWhenTheSettingIsOn()
    {
        using var h = new Harness();
        var window = new CaptureRegion.WindowRegion(42, new Rect(0, 0, 800, 600));
        h.Overlay.RecordingChoiceResult = new RecordingChoice(window, RecordingOutputKind.Video);
        await h.Coordinator.RecordScreenAsync();
        Assert.Equal(window, h.Settings.LastRecordingRegion); // always kept
        await h.Coordinator.StopRecordingAsync();

        await h.Coordinator.RecordScreenAsync();
        Assert.Equal([null, null], h.Overlay.RecordingCalls.Select(c => c.Initial)); // but only offered when setting is on
        await h.Coordinator.StopRecordingAsync();

        h.Settings.RememberLastRecordingArea = true;
        await h.Coordinator.RecordScreenAsync();
        Assert.Equal(window, h.Overlay.RecordingCalls[^1].Initial);
    }

    [Fact]
    public async Task ALostMicrophoneAsksAndContinuesOrStops()
    {
        using var h = new Harness();
        h.Ui.MicrophoneDisconnectedResults.Enqueue(true);
        h.Ui.MicrophoneDisconnectedResults.Enqueue(false);
        await h.Coordinator.StartRecordingAsync(DisplayRegion);
        h.Service.OnEvent?.Invoke(new RecordingEvent.AudioInputLost());
        while (h.Ui.MicrophoneDisconnectedResolutions == 0) await Task.Yield();
        await Task.Yield();
        Assert.True(h.Coordinator.IsRecording); // "Continue Without Audio"
        Assert.Equal(0, h.Service.StopCount);

        h.Service.OnEvent?.Invoke(new RecordingEvent.AudioInputLost());
        while (h.Coordinator.IsRecording || h.Sink.Saves.Count == 0) await Task.Yield();
        Assert.Equal(2, h.Ui.MicrophoneDisconnectedResolutions);
        Assert.Equal(1, h.Service.StopCount); // "Stop": the take is finished and saved
        Assert.Single(h.Sink.Saves);
    }

    [Fact]
    public async Task TheToolbarsMicrophoneChoiceBecomesTheDefaultAndReachesTheOptions()
    {
        using var h = new Harness();
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with
        {
            MicrophoneVolume = 1.5,
            MonoAudio = true,
            CountdownEnabled = false
        };
        h.Overlay.RecordingChoiceResult = new RecordingChoice(
            DisplayRegion, RecordingOutputKind.Video, new RecordingOverrides { Microphone = true }, MicrophoneDeviceID: "usb-mic");
        await h.Coordinator.RecordScreenAsync();

        Assert.Equal("usb-mic", h.Settings.RecordingDefaults.MicrophoneDeviceID);
        var options = h.Service.Starts[0].Options;
        Assert.Equal(new InputDeviceSelection.Device("usb-mic"), options.Microphone);
        Assert.Equal(1.5, options.MicrophoneVolume);
        Assert.True(options.MonoAudio);

        // A take with microphone off never overwrites the remembered device.
        await h.Coordinator.StopRecordingAsync();
        h.Overlay.RecordingChoiceResult = new RecordingChoice(
            DisplayRegion, RecordingOutputKind.Video, new RecordingOverrides { Microphone = false }, MicrophoneDeviceID: null);
        await h.Coordinator.RecordScreenAsync();
        Assert.Equal("usb-mic", h.Settings.RecordingDefaults.MicrophoneDeviceID);
    }

    [Fact]
    public async Task TheToolbarsCameraChoiceBecomesTheDefaultAndReachesTheOptions()
    {
        using var h = new Harness();
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with
        {
            CameraBubble = new CameraBubbleSettings { Size = CameraBubbleSize.Large },
            CountdownEnabled = false
        };
        h.Overlay.RecordingChoiceResult = new RecordingChoice(
            DisplayRegion, RecordingOutputKind.Video, new RecordingOverrides { Camera = true }, CameraDeviceID: "usb-cam");
        await h.Coordinator.RecordScreenAsync();

        Assert.Equal("usb-cam", h.Settings.RecordingDefaults.CameraDeviceID);
        var options = h.Service.Starts[0].Options;
        Assert.Equal(new InputDeviceSelection.Device("usb-cam"), options.Camera);
        Assert.Equal(CameraBubbleSize.Large, options.CameraBubble.Size);

        // A take with camera off never overwrites remembered device.
        await h.Coordinator.StopRecordingAsync();
        h.Overlay.RecordingChoiceResult = new RecordingChoice(
            DisplayRegion, RecordingOutputKind.Video, new RecordingOverrides { Camera = false }, CameraDeviceID: null);
        await h.Coordinator.RecordScreenAsync();
        Assert.Equal("usb-cam", h.Settings.RecordingDefaults.CameraDeviceID);
    }

    [Fact]
    public async Task PauseFreezesTheTimerAndResumeContinuesThroughTheService()
    {
        using var h = new Harness();
        await h.Coordinator.StartRecordingAsync(DisplayRegion);
        h.Now = 110;
        await h.Coordinator.PauseResumeRecordingAsync();
        Assert.Equal(RecordingSession.State.PausedState, h.Coordinator.RecordingSession.CurrentState);
        Assert.Equal(1, h.Service.PauseCount);

        h.Now = 150;
        Assert.Equal(10, h.Coordinator.RecordingElapsed); // frozen while paused
        await h.Coordinator.PauseResumeRecordingAsync();
        Assert.Equal(RecordingSession.State.RecordingState, h.Coordinator.RecordingSession.CurrentState);
        Assert.Equal(1, h.Service.ResumeCount);

        h.Now = 155;
        Assert.Equal(15, h.Coordinator.RecordingElapsed);
        Assert.Equal([RecordingSession.State.PausedState, RecordingSession.State.RecordingState], h.Ui.States.TakeLast(2));

        await h.Coordinator.StopRecordingAsync();
        await h.Coordinator.PauseResumeRecordingAsync(); // nothing to pause once take ended
        Assert.Equal(1, h.Service.PauseCount);
    }

    [Fact]
    public async Task RestartThrowsTheTakeAwayAndStartsAgainWithTheSameOptions()
    {
        using var h = new Harness();
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with { CountdownEnabled = false, ConfirmBeforeDiscard = true };
        await h.Coordinator.StartRecordingAsync(DisplayRegion);
        h.Now = 120;
        await h.Coordinator.RestartRecordingAsync();

        Assert.Equal(1, h.Ui.RestartConfirmations);
        Assert.Equal(1, h.Service.CancelCount); // old stream torn down
        Assert.Equal(2, h.Service.Starts.Count); // new one started
        Assert.All(h.Service.Starts.Select(s => s.Options), opt => Assert.Equal(h.Service.Starts[0].Options, opt));
        Assert.All(h.Service.Starts.Select(s => s.OutputPath), p => Assert.Equal(h.Service.Starts[0].OutputPath, p));
        Assert.Equal(RecordingSession.State.RecordingState, h.Coordinator.RecordingSession.CurrentState);
        Assert.Equal(0, h.Coordinator.RecordingElapsed); // clock restarted
        Assert.Empty(h.Ui.Countdowns);
    }

    [Fact]
    public async Task RestartGoesBackThroughTheCountdownWhenOneIsConfigured()
    {
        using var h = new Harness();
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with
        {
            CountdownEnabled = true,
            CountdownSeconds = 3,
            ConfirmBeforeDiscard = false
        };
        await h.Coordinator.StartRecordingAsync(DisplayRegion);
        await h.Coordinator.RestartRecordingAsync();

        Assert.Equal(0, h.Ui.RestartConfirmations);
        Assert.Equal([3, 3], h.Ui.Countdowns);
        Assert.Equal(2, h.Service.Starts.Count);
        Assert.Equal(RecordingSession.State.RecordingState, h.Coordinator.RecordingSession.CurrentState);
    }

    [Fact]
    public async Task ADeclinedConfirmationLeavesTheTakeRunning()
    {
        using var h = new Harness();
        h.Ui.ConfirmRestartResult = false;
        h.Ui.ConfirmDiscardResult = false;
        await h.Coordinator.StartRecordingAsync(DisplayRegion);
        await h.Coordinator.RestartRecordingAsync();
        await h.Coordinator.DiscardRecordingAsync();

        Assert.Equal(1, h.Ui.RestartConfirmations);
        Assert.Equal(1, h.Ui.DiscardConfirmations);
        Assert.Equal(0, h.Service.CancelCount);
        Assert.Single(h.Service.Starts);
        Assert.Equal(RecordingSession.State.RecordingState, h.Coordinator.RecordingSession.CurrentState);
    }

    [Fact]
    public async Task DiscardDeletesThePartialAndEndsInIdleWithNoSave()
    {
        using var h = new Harness();
        await h.Coordinator.StartRecordingAsync(DisplayRegion);
        await h.Coordinator.PauseResumeRecordingAsync();
        await h.Coordinator.DiscardRecordingAsync();

        Assert.Equal(1, h.Ui.DiscardConfirmations);
        Assert.Equal(1, h.Service.CancelCount); // service deletes partial file
        Assert.Empty(h.Sink.Saves);
        Assert.Equal(RecordingSession.State.IdleState, h.Coordinator.RecordingSession.CurrentState);
        Assert.Equal(RecordingSession.State.IdleState, h.Ui.States[^1]);
    }

    [Fact]
    public async Task AStopDuringRestartsTeardownWaitsForTheNewStream()
    {
        using var h = new Harness();
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with { CountdownEnabled = false, ConfirmBeforeDiscard = false };
        await h.Coordinator.StartRecordingAsync(DisplayRegion);
        h.Service.HoldCancel = true;
        var restarting = Task.Run(async () => await h.Coordinator.RestartRecordingAsync(), TestContext.Current.CancellationToken);
        while (h.Service.CancelGate == null) await Task.Yield();

        await h.Coordinator.StopRecordingAsync(); // arrives mid-teardown
        Assert.Equal(0, h.Service.StopCount);

        h.Service.CancelGate.TrySetResult(true);
        await restarting;
        Assert.Equal(2, h.Service.Starts.Count);
        Assert.Equal(RecordingSession.State.RecordingState, h.Coordinator.RecordingSession.CurrentState);
        Assert.Empty(h.Ui.RecordingFailures);
    }

    [Fact]
    public async Task DiscardIsLegalDuringTheCountdownAndRestartIsNot()
    {
        using var h = new Harness();
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with
        {
            CountdownEnabled = true,
            CountdownSeconds = 3,
            ConfirmBeforeDiscard = false
        };
        h.Ui.HoldCountdown = true;
        var starting = Task.Run(async () => await h.Coordinator.StartRecordingAsync(DisplayRegion), TestContext.Current.CancellationToken);
        while (h.Ui.CountdownGate == null) await Task.Yield();

        await h.Coordinator.RestartRecordingAsync(); // no footage yet: nothing to restart
        Assert.Equal(RecordingSession.State.CountdownState, h.Coordinator.RecordingSession.CurrentState);
        await h.Coordinator.DiscardRecordingAsync();
        Assert.Equal(RecordingSession.State.IdleState, h.Coordinator.RecordingSession.CurrentState);

        h.Ui.CountdownGate.TrySetResult(true);
        await starting;
        Assert.Empty(h.Service.Starts);
    }

    [Fact]
    public async Task RestartAndDiscardAreNoOpsWhenNothingIsRecording()
    {
        using var h = new Harness();
        await h.Coordinator.RestartRecordingAsync();
        await h.Coordinator.DiscardRecordingAsync();
        Assert.Equal(0, h.Ui.RestartConfirmations);
        Assert.Equal(0, h.Ui.DiscardConfirmations);
        Assert.Equal(0, h.Service.CancelCount);
    }

    [Fact]
    public async Task AConfiguredCountdownRunsInTheUIThenBeginsRecording()
    {
        using var h = new Harness();
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with { CountdownEnabled = true, CountdownSeconds = 3 };
        await h.Coordinator.StartRecordingAsync(DisplayRegion);

        Assert.Equal([3], h.Ui.Countdowns);
        Assert.Equal([RecordingSession.State.CountdownState, RecordingSession.State.RecordingState], h.Ui.States);
        Assert.Single(h.Service.Starts);
        Assert.Equal(3, h.Service.Starts[0].Options.CountdownSeconds);
        Assert.Equal(0, h.Coordinator.RecordingElapsed); // countdown itself not footage
    }

    [Fact]
    public async Task EscapeDuringTheCountdownDiscardsBeforeAnythingIsRecorded()
    {
        using var h = new Harness();
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with { CountdownEnabled = true, CountdownSeconds = 3 };
        h.Ui.CountdownResult = false;
        await h.Coordinator.StartRecordingAsync(DisplayRegion);

        Assert.Empty(h.Service.Starts);
        Assert.Equal(0, h.Service.CancelCount); // nothing was ever started
        Assert.Equal(RecordingSession.State.IdleState, h.Coordinator.RecordingSession.CurrentState);
        Assert.Equal([RecordingSession.State.CountdownState, RecordingSession.State.IdleState], h.Ui.States);
    }

    [Fact]
    public async Task PermissionDeniedOnStartRoutesToRecoveryAndFailsTheSession()
    {
        using var h = new Harness();
        h.Service.StartResult = new RecordingError.PermissionDenied(PermissionKind.ScreenRecording);
        await h.Coordinator.StartRecordingAsync(DisplayRegion);

        Assert.Equal(1, h.Ui.PermissionDeniedCount);
        Assert.Equal([PermissionKind.ScreenRecording], h.Ui.DeniedKinds);
        Assert.Empty(h.Ui.RecordingFailures);
        Assert.Equal(new RecordingSession.State.Failed(new RecordingError.PermissionDenied(PermissionKind.ScreenRecording)), h.Coordinator.RecordingSession.CurrentState);
        Assert.False(h.Coordinator.IsRecording);
        Assert.Empty(h.Sink.Saves);
        Assert.Equal(new RecordingSession.State.Failed(new RecordingError.PermissionDenied(PermissionKind.ScreenRecording)), h.Ui.States[^1]);
    }

    [Fact]
    public async Task AMissingMicrophoneGrantRoutesToTheMicrophoneRecoveryNotScreenRecording()
    {
        using var h = new Harness();
        h.Service.StartResult = new RecordingError.PermissionDenied(PermissionKind.Microphone);
        await h.Coordinator.StartRecordingAsync(DisplayRegion);

        Assert.Equal([PermissionKind.Microphone], h.Ui.DeniedKinds);
        Assert.Empty(h.Ui.RecordingFailures);
    }

    [Fact]
    public async Task UserCancelledIsSilentAndOtherFailuresGetADistinctMessage()
    {
        using var cancelled = new Harness();
        cancelled.Service.StartResult = new RecordingError.UserCancelled();
        await cancelled.Coordinator.StartRecordingAsync(DisplayRegion);
        Assert.Empty(cancelled.Ui.RecordingFailures);
        Assert.Equal(0, cancelled.Ui.PermissionDeniedCount);

        using var broken = new Harness();
        broken.Service.StartResult = new RecordingError.NoDisplayAvailable();
        await broken.Coordinator.StartRecordingAsync(DisplayRegion);
        Assert.Equal([new RecordingError.NoDisplayAvailable()], broken.Ui.RecordingFailures);
        Assert.Equal(new RecordingSession.State.Failed(new RecordingError.NoDisplayAvailable()), broken.Coordinator.RecordingSession.CurrentState);
    }

    [Fact]
    public async Task AStopFailureFailsTheSessionWithAMessageAndSavesNothing()
    {
        using var h = new Harness(service: new FakeRecordingService((null, new RecordingError.DiskFull())));
        await h.Coordinator.StartRecordingAsync(DisplayRegion);
        await h.Coordinator.StopRecordingAsync();

        Assert.Equal([new RecordingError.DiskFull()], h.Ui.RecordingFailures);
        Assert.Equal(new RecordingSession.State.Failed(new RecordingError.DiskFull()), h.Coordinator.RecordingSession.CurrentState);
        Assert.Empty(h.Sink.Saves);
        Assert.Empty(h.Ui.FinishedRecordings);
    }

    [Fact]
    public async Task ASaveFailureIsSurfacedAndTheTakeStaysFinished()
    {
        using var h = new Harness();
        h.Sink.SaveFails = true;
        await h.Coordinator.StartRecordingAsync(DisplayRegion);
        await h.Coordinator.StopRecordingAsync();

        Assert.Empty(h.Ui.FinishedRecordings);
        Assert.Single(h.Ui.RecordingFailures);
        Assert.IsType<RecordingError.SystemFailure>(h.Ui.RecordingFailures[0]);
        Assert.IsType<RecordingSession.State.Finished>(h.Coordinator.RecordingSession.CurrentState);
    }

    [Fact]
    public async Task FirstRunPromptsThroughTheRecorderBeforeStarting()
    {
        using var h = new Harness();
        h.Service.Status = CaptureAuthorizationStatus.NotDetermined;
        h.Service.RequestResult = CaptureAuthorizationStatus.Denied;
        await h.Coordinator.StartRecordingAsync(DisplayRegion);

        Assert.Equal(1, h.Service.RequestAuthorizationCount);
        Assert.Empty(h.Service.Starts);
        Assert.False(h.Coordinator.IsRecording);
    }

    [Fact]
    public async Task AStandingGrantOrDenialNeverRePrompts()
    {
        foreach (var status in new[] { CaptureAuthorizationStatus.Authorized, CaptureAuthorizationStatus.Denied })
        {
            using var h = new Harness();
            h.Service.Status = status;
            await h.Coordinator.StartRecordingAsync(DisplayRegion);
            Assert.Equal(0, h.Service.RequestAuthorizationCount);
            Assert.Single(h.Service.Starts);
        }
    }

    [Fact]
    public void RecordingDestinationUsesTheSaveLocationPatternAndContainerExtension()
    {
        var settings = new FakeSettingsStore();
        settings.SaveLocation = "/tmp/movies";
        settings.FilenamePattern = "Recording %Y";
        var date = new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Local);
        Assert.Equal("/tmp/movies/Recording 2026.mp4", settings.RecordingDestination(RecordingOutputKind.Video, date));
        Assert.Equal("/tmp/movies/Recording 2026.gif", settings.RecordingDestination(RecordingOutputKind.Gif, date));
    }

    [Fact]
    public async Task AStudioModeTakeOpensTheEditorWhateverTheSettingSays()
    {
        using var h = new Harness();
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with { AfterRecording = AfterRecordingAction.SaveSilently };
        await h.Coordinator.StartRecordingAsync(DisplayRegion, RecordingOutputKind.Video, new RecordingOverrides { AfterRecording = AfterRecordingAction.OpenEditor });
        h.Now = 130;
        await h.Coordinator.StopRecordingAsync();

        Assert.Single(h.Ui.VideoEditorOpens);
        Assert.Empty(h.Ui.PostRecordings);
        Assert.Empty(h.Ui.FinishedRecordings);
        Assert.Equal(AfterRecordingAction.SaveSilently, h.Settings.RecordingDefaults.AfterRecording); // setting is untouched
    }

    [Fact]
    public void NothingIsInProgressWhenIdle()
    {
        using var h = new Harness();
        Assert.False(h.Coordinator.HasWorkInProgress);
    }

    [Fact]
    public async Task ATakeIsWorkFromStartThroughPauseUntilItsResultIsRouted()
    {
        using var h = new Harness();
        h.Service.HoldStart = true;
        var starting = Task.Run(async () => await h.Coordinator.StartRecordingAsync(DisplayRegion), TestContext.Current.CancellationToken);
        while (h.Service.StartGate == null) await Task.Yield();
        Assert.True(h.Coordinator.HasWorkInProgress); // stream being started

        h.Service.StartGate.TrySetResult(true);
        await starting;
        Assert.True(h.Coordinator.HasWorkInProgress); // recording

        await h.Coordinator.PauseResumeRecordingAsync();
        Assert.Equal(RecordingSession.State.PausedState, h.Coordinator.RecordingSession.CurrentState);
        Assert.True(h.Coordinator.HasWorkInProgress); // paused

        h.Service.HoldStop = true;
        var stopping = Task.Run(async () => await h.Coordinator.StopRecordingAsync(), TestContext.Current.CancellationToken);
        while (h.Service.StopGate == null) await Task.Yield();
        Assert.True(h.Coordinator.HasWorkInProgress); // file being finalised

        h.Service.StopGate.TrySetResult(true);
        await stopping;
        Assert.Single(h.Ui.FinishedRecordings);
        Assert.False(h.Coordinator.HasWorkInProgress); // saved and routed
    }

    [Fact]
    public async Task ACountdownIsWorkAndEscapingItEndsIt()
    {
        using var h = new Harness();
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with { CountdownEnabled = true, CountdownSeconds = 3 };
        h.Ui.HoldCountdown = true;
        h.Ui.CountdownResult = false; // Escape
        var starting = Task.Run(async () => await h.Coordinator.StartRecordingAsync(DisplayRegion), TestContext.Current.CancellationToken);
        while (h.Ui.CountdownGate == null) await Task.Yield();
        Assert.True(h.Coordinator.HasWorkInProgress);

        h.Ui.CountdownGate.TrySetResult(false);
        await starting;
        Assert.False(h.Coordinator.HasWorkInProgress);
    }

    [Fact]
    public async Task ADiscardedTakeEndsTheWork()
    {
        using var h = new Harness();
        h.Settings.RecordingDefaults = h.Settings.RecordingDefaults with { CountdownEnabled = false, ConfirmBeforeDiscard = false };
        await h.Coordinator.StartRecordingAsync(DisplayRegion);
        await h.Coordinator.DiscardRecordingAsync();
        Assert.False(h.Coordinator.HasWorkInProgress);
    }
}
