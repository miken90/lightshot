// Ported from LightshotKit/Sources/LightshotKit/AppCoordinator.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lightshot.Core;

public partial class AppCoordinator
{
    /// <summary>
    /// Toggles recording: starts if idle, stops if active.
    /// </summary>
    public async Task ToggleRecordingAsync()
    {
        if (IsRecording)
        {
            await StopRecordingAsync();
        }
        else
        {
            await RecordScreenAsync();
        }
    }

    /// <summary>
    /// Opens the recording overlay to select a region/options and starts recording.
    /// </summary>
    public async Task RecordScreenAsync()
    {
        if (_recordingService == null || IsRecording || _isStartingRecording)
        {
            return;
        }

        var initial = _settings.RememberLastRecordingArea ? _settings.LastRecordingRegion : null;
        var choice = await _overlay.SelectRecordingAsync(initial, _settings.RecordingDefaults);
        if (choice == null)
        {
            return;
        }

        _settings.LastRecordingRegion = choice.Region;

        bool micOn = choice.Overrides.Microphone ?? _settings.RecordingDefaults.RecordMicrophone;
        if (micOn && _settings.RecordingDefaults.MicrophoneDeviceID != choice.MicrophoneDeviceID)
        {
            _settings.RecordingDefaults = _settings.RecordingDefaults with { MicrophoneDeviceID = choice.MicrophoneDeviceID };
        }

        bool camOn = choice.Overrides.Camera ?? _settings.RecordingDefaults.RecordCamera;
        if (camOn && _settings.RecordingDefaults.CameraDeviceID != choice.CameraDeviceID)
        {
            _settings.RecordingDefaults = _settings.RecordingDefaults with { CameraDeviceID = choice.CameraDeviceID };
        }

        await StartRecordingAsync(choice.Region, choice.Output, choice.Overrides);
    }

    /// <summary>
    /// Starts recording a region with given output kind and overrides.
    /// </summary>
    public async Task StartRecordingAsync(
        CaptureRegion region,
        RecordingOutputKind output = RecordingOutputKind.Video,
        RecordingOverrides? overrides = null)
    {
        if (_recordingService == null || IsRecording || _isStartingRecording || (_gifConversion != null && !_gifConversion.IsCompleted) || _isArchiving)
        {
            return;
        }

        if (!await GuideFirstRunAuthorizationIfNeededAsync(_recordingService))
        {
            return;
        }

        DismissPendingRecording();

        var options = RecordingOptions.Resolve(
            region,
            output,
            _settings.RecordingDefaults,
            overrides ?? RecordingOverrides.None,
            _settings.HideDesktopIcons);

        var path = ScratchPath();
        try
        {
            _recordingSession.Start(options, path, _clock());
        }
        catch
        {
            return;
        }

        _ui.PresentRecordingState(_recordingSession);

        if (!await RunCountdownIfNeededAsync(options))
        {
            return;
        }

        await StartStreamAsync(options, path);
    }

    private async Task<bool> RunCountdownIfNeededAsync(RecordingOptions options)
    {
        if (_recordingSession.CurrentState is not RecordingSession.State.Countdown)
        {
            return _recordingSession.CurrentState is RecordingSession.State.Recording;
        }

        bool completed = await _ui.RunRecordingCountdownAsync(options.CountdownSeconds);
        if (_recordingSession.CurrentState is not RecordingSession.State.Countdown)
        {
            return false;
        }

        if (!completed)
        {
            try { _recordingSession.Discard(); } catch { }
            _ui.PresentRecordingState(_recordingSession);
            return false;
        }

        try
        {
            _recordingSession.BeginRecording(_clock());
        }
        catch
        {
            try { _recordingSession.Discard(); } catch { }
            _ui.PresentRecordingState(_recordingSession);
            return false;
        }

        _ui.PresentRecordingState(_recordingSession);
        return true;
    }

    private async Task StartStreamAsync(RecordingOptions options, string path)
    {
        if (_recordingService == null) return;

        _isStartingRecording = true;
        var outcome = await _recordingService.StartAsync(options, path, ev =>
        {
            Task.Run(() => RecordingDidReportAsync(ev));
        });
        _isStartingRecording = false;

        if (outcome != null)
        {
            FailRecording(outcome);
        }
    }

    /// <summary>
    /// Pauses an active recording or resumes a paused recording.
    /// </summary>
    public async Task PauseResumeRecordingAsync()
    {
        if (_recordingService == null || _isStartingRecording) return;

        switch (_recordingSession.CurrentState)
        {
            case RecordingSession.State.Recording:
                try { _recordingSession.Pause(_clock()); } catch { return; }
                await _recordingService.PauseAsync();
                break;
            case RecordingSession.State.Paused:
                try { _recordingSession.Resume(_clock()); } catch { return; }
                await _recordingService.ResumeAsync();
                break;
            default:
                return;
        }

        _ui.PresentRecordingState(_recordingSession);
    }

    /// <summary>
    /// Restarts the recording with the same options, discarding the current take.
    /// </summary>
    public async Task RestartRecordingAsync()
    {
        if (_recordingService == null || _isStartingRecording
            || (_recordingSession.CurrentState is not RecordingSession.State.Recording and not RecordingSession.State.Paused))
        {
            return;
        }

        if (_settings.RecordingDefaults.ConfirmBeforeDiscard)
        {
            if (!await _ui.ConfirmRecordingRestartAsync()) return;
            if (_recordingSession.CurrentState is not RecordingSession.State.Recording and not RecordingSession.State.Paused)
            {
                return;
            }
        }

        var options = _recordingSession.Options;
        var path = _recordingSession.OutputPath;
        if (options == null || path == null) return;

        try { _recordingSession.Restart(_clock()); } catch { return; }

        _isStartingRecording = true;
        await _recordingService.CancelAsync();
        _isStartingRecording = false;

        _ui.PresentRecordingState(_recordingSession);

        if (!await RunCountdownIfNeededAsync(options))
        {
            return;
        }

        await StartStreamAsync(options, path);
    }

    /// <summary>
    /// Discards the current recording without saving.
    /// </summary>
    public async Task DiscardRecordingAsync()
    {
        if (_recordingService == null || _isStartingRecording || !IsRecording || _recordingSession.CurrentState is RecordingSession.State.Stopping)
        {
            return;
        }

        if (_settings.RecordingDefaults.ConfirmBeforeDiscard)
        {
            if (!await _ui.ConfirmRecordingDiscardAsync()) return;
            if (!IsRecording || _recordingSession.CurrentState is RecordingSession.State.Stopping) return;
        }

        try { _recordingSession.Discard(); } catch { return; }
        await _recordingService.CancelAsync();
        _ui.PresentRecordingState(_recordingSession);
    }

    private async Task RecordingDidReportAsync(RecordingEvent ev)
    {
        if (!IsRecording) return;

        switch (ev)
        {
            case RecordingEvent.Failed failed:
                FailRecording(failed.Error);
                break;
            case RecordingEvent.AudioInputLost:
                bool keep = await _ui.ResolveMicrophoneDisconnectedAsync();
                if (!keep && IsRecording)
                {
                    await StopRecordingAsync();
                }
                break;
        }
    }

    /// <summary>
    /// Stops the recording and routes the finished take.
    /// </summary>
    public async Task StopRecordingAsync()
    {
        if (_recordingService == null || !IsRecording || _isStartingRecording) return;

        RecordingSession.StopOutcome outcome;
        try
        {
            outcome = _recordingSession.Stop(_clock());
        }
        catch
        {
            return;
        }

        switch (outcome)
        {
            case RecordingSession.StopOutcome.Discarded:
                await _recordingService.CancelAsync();
                _ui.PresentRecordingState(_recordingSession);
                break;

            case RecordingSession.StopOutcome.Stopping:
                _ui.PresentRecordingState(_recordingSession);
                var result = await _recordingService.StopAsync();
                if (result.Error == null && result.Path != null)
                {
                    try { _recordingSession.Finish(result.Path); } catch { }
                    _ui.PresentRecordingState(_recordingSession);
                    await FinishedAsync(result.Path);
                }
                else if (result.Error != null)
                {
                    FailRecording(result.Error);
                }
                break;
        }
    }

    private async Task FinishedAsync(string take)
    {
        _isFinishingTake = true;
        try
        {
            double duration = _recordingSession.Elapsed(_clock());
            var options = _recordingSession.Options;
            var after = options?.AfterRecording ?? _settings.RecordingDefaults.AfterRecording;
            string file = take;

            if (options?.Output is RecordingOutput.Gif gifOutput && _gifEncoder != null && _mediaSink != null)
            {
                string gif = Path.ChangeExtension(file, "gif");
                var gifSettings = gifOutput.Settings;
                var cts = new CancellationTokenSource();
                var conversionTask = _gifEncoder.EncodeAsync(file, gif, gifSettings, p => _ui.UpdateGifConversion(p), cts.Token);
                _gifConversion = conversionTask;
                _ui.PresentGifConversion(() => cts.Cancel());

                try
                {
                    await conversionTask;
                    _gifConversion = null;
                    _ui.DismissGifConversion();
                    try { _mediaSink.Delete(file); } catch { }
                    await ArchiveAndRouteAsync(new PendingRecording(gif, RecordingOutputKind.Gif, duration), after);
                }
                catch (OperationCanceledException)
                {
                    _gifConversion = null;
                    _ui.DismissGifConversion();
                    if (await _ui.ResolveCancelledGifConversionAsync())
                    {
                        await ArchiveAndRouteAsync(new PendingRecording(file, RecordingOutputKind.Video, duration), after);
                    }
                    else
                    {
                        try { _mediaSink.Delete(file); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    _gifConversion = null;
                    _ui.DismissGifConversion();
                    _ui.PresentRecordingFailure(new RecordingError.SystemFailure($"The GIF could not be made: {ex.Message}"));
                    await ArchiveAndRouteAsync(new PendingRecording(file, RecordingOutputKind.Video, duration), after);
                }
            }
            else
            {
                await ArchiveAndRouteAsync(new PendingRecording(file, RecordingOutputKind.Video, duration), after);
            }
        }
        finally
        {
            _isFinishingTake = false;
        }
    }

    private async Task ArchiveAndRouteAsync(PendingRecording recording, AfterRecordingAction after)
    {
        var archived = await ArchiveAsync(recording);
        Route(archived, after);
    }

    private async Task<PendingRecording> ArchiveAsync(PendingRecording recording)
    {
        if (_history == null) return recording;

        _isArchiving = true;
        try
        {
            CaptureRecord? record = null;
            if (recording.Kind == RecordingOutputKind.Gif)
            {
                try { record = _history.AddGif(recording.File, CaptureSource.Recording); } catch { }
            }
            else if (recording.Kind == RecordingOutputKind.Video)
            {
                if (_mediaMetadata != null)
                {
                    var metadata = await _mediaMetadata.VideoMetadataAsync(recording.File);
                    if (metadata != null)
                    {
                        try
                        {
                            record = _history.Add(
                                recording.File,
                                CaptureKind.Video,
                                metadata.Value.PixelWidth,
                                metadata.Value.PixelHeight,
                                metadata.Value.Duration,
                                metadata.Value.ThumbnailPng,
                                CaptureSource.Recording);
                        }
                        catch { }
                    }
                }
            }

            if (record == null) return recording;

            return new PendingRecording(
                record.FileUrl,
                recording.Kind,
                recording.Duration,
                new RecordingOrigin.FreshInHistory(record.Id),
                SuggestedName(recording.File));
        }
        finally
        {
            _isArchiving = false;
        }
    }

    private void Route(PendingRecording recording, AfterRecordingAction after)
    {
        switch (after)
        {
            case AfterRecordingAction.ShowOverlay:
                _pendingRecording = recording;
                _ui.PresentPostRecordingOverlay(recording);
                break;

            case AfterRecordingAction.SaveSilently:
                var saved = Deliver(recording, null);
                if (saved != null) _ui.PresentRecordingFinished(saved);
                break;

            case AfterRecordingAction.OpenEditor:
                var dest = Deliver(recording, null);
                if (dest != null)
                {
                    if (recording.Kind == RecordingOutputKind.Gif)
                    {
                        _ui.PresentRecordingFinished(dest);
                    }
                    else
                    {
                        _ui.OpenVideoEditor(dest);
                    }
                }
                break;
        }
    }

    private string? Deliver(PendingRecording recording, string? name)
    {
        if (_mediaSink == null) return null;

        var file = recording.File;
        var ext = Path.GetExtension(file);
        var destination = name != null
            ? _settings.RecordingDestination(name, ext)
            : _settings.RecordingDestination(ext);

        try
        {
            if (recording.HistoryRecordId != null)
            {
                _mediaSink.Copy(file, destination);
            }
            else
            {
                _mediaSink.Save(file, destination);
            }
            return destination;
        }
        catch (Exception ex)
        {
            _ui.PresentRecordingFailure(new RecordingError.SystemFailure(ex.Message));
            return null;
        }
    }

    public string? CopyPendingRecordingFile(string? name = null)
    {
        var saved = SavePendingRecording(name);
        if (saved == null) return null;
        _mediaSink?.CopyFile(saved);
        return saved;
    }

    public string? SavePendingRecording(string? name = null)
    {
        if (_pendingRecording == null) return null;
        var saved = Deliver(_pendingRecording, name);
        if (saved == null) return null;
        _pendingRecording = null;
        return saved;
    }

    public string? OpenPendingRecordingInEditor(string? name = null)
    {
        if (_pendingRecording == null) return null;
        var saved = SavePendingRecording(name);
        if (saved == null) return null;
        _ui.OpenVideoEditor(saved);
        return saved;
    }

    public void DismissPendingRecording(string? name = null)
    {
        if (_pendingRecording == null) return;
        if (_pendingRecording.IsNew)
        {
            SavePendingRecording(name);
        }
        else
        {
            _pendingRecording = null;
        }
    }

    public bool DeletePendingRecording()
    {
        if (_pendingRecording == null || _mediaSink == null) return false;

        try
        {
            if (_pendingRecording.HistoryRecordId is Guid id && _history != null && _history.Record(id) is CaptureRecord record)
            {
                _history.Remove(record);
            }
            else
            {
                _mediaSink.Trash(_pendingRecording.File);
            }
            _pendingRecording = null;
            return true;
        }
        catch (Exception ex)
        {
            _ui.PresentRecordingFailure(new RecordingError.SystemFailure(ex.Message));
            return false;
        }
    }

    public void ReopenRecording(CaptureRecord record)
    {
        DismissPendingRecording();

        var recording = new PendingRecording(
            record.FileUrl,
            record.Kind == CaptureKind.Gif ? RecordingOutputKind.Gif : RecordingOutputKind.Video,
            record.Duration ?? 0,
            new RecordingOrigin.HistoryItem(record.Id),
            SuggestedName(record.FileUrl));

        switch (record.Kind)
        {
            case CaptureKind.Video:
                var copy = Deliver(recording, null);
                if (copy != null) _ui.OpenVideoEditor(copy);
                break;
            case CaptureKind.Gif:
                _pendingRecording = recording;
                _ui.PresentPostRecordingOverlay(recording);
                break;
            case CaptureKind.Screenshot:
                break;
        }
    }

    public async Task<PendingRecording> ArchiveRecoveredRecordingAsync(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.');
        var kind = ext.Equals("gif", StringComparison.OrdinalIgnoreCase)
            ? RecordingOutputKind.Gif
            : RecordingOutputKind.Video;

        double duration = 0;
        if (kind == RecordingOutputKind.Video && _mediaMetadata != null)
        {
            var meta = await _mediaMetadata.VideoMetadataAsync(path);
            if (meta != null) duration = meta.Value.Duration;
        }

        return await ArchiveAsync(new PendingRecording(path, kind, duration));
    }

    private void FailRecording(RecordingError error)
    {
        try { _recordingSession.Fail(error, _clock()); } catch { }
        _ui.PresentRecordingState(_recordingSession);

        switch (error)
        {
            case RecordingError.PermissionDenied perm:
                _ui.PresentPermissionDenied(perm.Kind);
                break;
            case RecordingError.UserCancelled:
                break;
            default:
                _ui.PresentRecordingFailure(error);
                break;
        }
    }

    private string ScratchPath()
    {
        var dir = RecordingScratchDirectory;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return Path.Combine(dir, $"Recording-{Guid.NewGuid()}.mp4").Replace('\\', '/');
    }

    private string SuggestedName(string path)
    {
        var ext = Path.GetExtension(path);
        return Path.GetFileNameWithoutExtension(_settings.RecordingDestination(ext));
    }
}
