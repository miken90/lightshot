// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lightshot.Core.Tests.FakeServices;

public class FakeCaptureUI : ICaptureUI
{
    public List<CapturedImage> OpenedImages { get; } = [];
    public List<CapturedImage> QuickAccess { get; } = [];
    public int PermissionDeniedCount { get; private set; }
    public List<PermissionKind> DeniedKinds { get; } = [];
    public List<CaptureError> Failures { get; } = [];
    public List<ImageLoadError> ImageLoadFailures { get; } = [];
    public List<RecordingError> RecordingFailures { get; } = [];

    public List<RecordingSession.State> States { get; } = [];
    public List<int> Countdowns { get; } = [];
    public bool CountdownResult { get; set; } = true;
    public bool HoldCountdown { get; set; }
    public TaskCompletionSource<bool>? CountdownGate { get; set; }

    public int RestartConfirmations { get; private set; }
    public bool ConfirmRestartResult { get; set; } = true;

    public int DiscardConfirmations { get; private set; }
    public bool ConfirmDiscardResult { get; set; } = true;
    public List<string> FinishedRecordings { get; } = [];
    public List<PendingRecording> PostRecordings { get; } = [];
    public List<(string Path, string? InputPath)> VideoEditorOpens { get; } = [];

    public bool RecordingPreparationPresented { get; private set; }
    public bool RecordingPreparationDismissed { get; private set; }
    public List<double> RecordingPreparationProgresses { get; } = [];

    public int GifConversionsPresented { get; private set; }
    public bool GifConversionDismissed { get; private set; }
    public List<double> GifConversionProgresses { get; } = [];
    public bool ResolveCancelledGifConversionResult { get; set; } = true;

    public void OpenEditor(CapturedImage image) => OpenedImages.Add(image);
    public void PresentQuickAccess(CapturedImage image) => QuickAccess.Add(image);
    public void PresentPermissionDenied(PermissionKind kind)
    {
        PermissionDeniedCount++;
        DeniedKinds.Add(kind);
    }
    public void PresentCaptureFailure(CaptureError error) => Failures.Add(error);
    public void PresentImageLoadFailure(ImageLoadError error) => ImageLoadFailures.Add(error);
    public void PresentRecordingFailure(RecordingError error) => RecordingFailures.Add(error);

    public void PresentRecordingState(RecordingSession session)
    {
        States.Add(session.CurrentState);
    }

    public async Task<bool> RunRecordingCountdownAsync(int seconds)
    {
        Countdowns.Add(seconds);
        if (HoldCountdown)
        {
            CountdownGate = new TaskCompletionSource<bool>();
            return await CountdownGate.Task;
        }
        return CountdownResult;
    }

    public Task<bool> ConfirmRecordingRestartAsync()
    {
        RestartConfirmations++;
        return Task.FromResult(ConfirmRestartResult);
    }

    public Task<bool> ConfirmRecordingDiscardAsync()
    {
        DiscardConfirmations++;
        return Task.FromResult(ConfirmDiscardResult);
    }

    public int MicrophoneDisconnectedResolutions { get; private set; }
    public bool ResolveMicrophoneDisconnectedResult { get; set; } = true;
    public Queue<bool> MicrophoneDisconnectedResults { get; } = new();

    public Task<bool> ResolveMicrophoneDisconnectedAsync()
    {
        MicrophoneDisconnectedResolutions++;
        if (MicrophoneDisconnectedResults.Count > 0)
        {
            return Task.FromResult(MicrophoneDisconnectedResults.Dequeue());
        }
        return Task.FromResult(ResolveMicrophoneDisconnectedResult);
    }

    public void PresentRecordingFinished(string path) => FinishedRecordings.Add(path);
    public void PresentPostRecordingOverlay(PendingRecording recording) => PostRecordings.Add(recording);
    public void OpenVideoEditor(string path, string? inputPath = null) => VideoEditorOpens.Add((path, inputPath));

    public void PresentRecordingPreparation(Action cancel) => RecordingPreparationPresented = true;
    public void UpdateRecordingPreparation(double progress) => RecordingPreparationProgresses.Add(progress);
    public void DismissRecordingPreparation() => RecordingPreparationDismissed = true;

    public void PresentGifConversion(Action cancel) => GifConversionsPresented++;
    public void UpdateGifConversion(double progress) => GifConversionProgresses.Add(progress);
    public void DismissGifConversion() => GifConversionDismissed = true;
    public Task<bool> ResolveCancelledGifConversionAsync() => Task.FromResult(ResolveCancelledGifConversionResult);
}
