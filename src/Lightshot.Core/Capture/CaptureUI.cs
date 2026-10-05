// Ported from LightshotKit/Sources/LightshotKit/AppCoordinator.swift (CaptureUI)
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Threading.Tasks;

namespace Lightshot.Core;

/// <summary>
/// The user-facing UI surface driven by the coordinator (editor, quick access, alerts).
/// </summary>
public interface ICaptureUI
{
    void OpenEditor(CapturedImage image);
    void PresentQuickAccess(CapturedImage image);
    void PresentPermissionDenied(PermissionKind kind);
    void PresentCaptureFailure(CaptureError error);
    void PresentImageLoadFailure(ImageLoadError error);

    void PresentRecordingState(RecordingSession session);
    Task<bool> RunRecordingCountdownAsync(int seconds);
    Task<bool> ConfirmRecordingRestartAsync();
    Task<bool> ConfirmRecordingDiscardAsync();
    Task<bool> ResolveMicrophoneDisconnectedAsync();
    void PresentRecordingFinished(string path);
    void PresentPostRecordingOverlay(PendingRecording recording);
    void OpenVideoEditor(string path, string? inputPath = null);

    void PresentRecordingPreparation(Action cancel);
    void UpdateRecordingPreparation(double progress);
    void DismissRecordingPreparation();

    void PresentGifConversion(Action cancel);
    void UpdateGifConversion(double progress);
    void DismissGifConversion();
    Task<bool> ResolveCancelledGifConversionAsync();
    void PresentRecordingFailure(RecordingError error);
}
