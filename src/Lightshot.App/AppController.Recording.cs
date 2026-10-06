// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Lightshot.App.Views.History;
using Lightshot.App.Views.Notices;
using Lightshot.App.Views.Recording;
using Lightshot.App.Views.VideoEditor;
using Lightshot.Core;
using Lightshot.Platform.Windows.Media;
using Lightshot.Platform.Windows.Overlay;
using Lightshot.Platform.Windows.Recording;
using Lightshot.Platform.Windows.Tray;

namespace Lightshot.App;

public sealed partial class AppController
{
    private RecordingChromeCoordinator? _chrome;
    private ControlsPill? _pill;
    private ControlsPillViewModel? _pillVm;
    private DispatcherTimer? _recordingTimer;
    private bool _frameShown;
    private ProgressPopup? _gifPopup;
    private ProgressPopup? _prepPopup;

    private void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private Task<T> OnUiAsync<T>(Func<Task<T>> func)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return func();
        }
        return dispatcher.InvokeAsync(func).Task.Unwrap();
    }

    private RecordingChromeCoordinator Chrome() => _chrome ??= new RecordingChromeCoordinator();

    private void InitializeRecording()
    {
        _trayIcon.RecordingState = () => _coordinator.IsRecording
            ? new RecordingMenuState(true, ControlsPillViewModel.FormatTime(_coordinator.RecordingElapsed))
            : null;

        _ = Task.Run(RecoverScratchAsync);
    }

    public void PresentRecordingState(RecordingSession session)
    {
        OnUi(() =>
        {
            var d = _settingsStore.RecordingDefaults;
            switch (session.CurrentState)
            {
                case RecordingSession.State.Recording or RecordingSession.State.Paused:
                    bool paused = session.CurrentState is RecordingSession.State.Paused;
                    if (!_frameShown)
                    {
                        _shellThread.InvokeAsync(() => Chrome().ShowFrame(session.Options!.Region, d.DimScreenWhileRecording, paused));
                        _frameShown = true;
                    }
                    else
                    {
                        _shellThread.InvokeAsync(() => Chrome().SetFramePaused(paused));
                    }

                    if (d.ShowRecordingControls && _pill == null)
                    {
                        _pillVm = new ControlsPillViewModel(
                            hasMicrophone: session.Options?.Microphone is not InputDeviceSelection.Off,
                            hasComputerAudio: session.Options?.ComputerAudio ?? false);

                        _pillVm.PauseResumeRequested += (_, _) => _ = _coordinator.PauseResumeRecordingAsync();
                        _pillVm.StopRequested += (_, _) => _ = _coordinator.StopRecordingAsync();
                        _pillVm.RestartRequested += (_, _) => _ = _coordinator.RestartRecordingAsync();
                        _pillVm.DiscardRequested += (_, _) => _ = _coordinator.DiscardRecordingAsync();

                        _pill = new ControlsPill(_pillVm);
                        _pill.Show();
                        _pill.PositionPill(d.ControlsPosition == RecordingControlsPosition.Top);
                    }

                    if (_pillVm != null)
                    {
                        _pillVm.IsPaused = paused;
                    }

                    if (_recordingTimer == null)
                    {
                        _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                        _recordingTimer.Tick += OnRecordingTimerTick;
                    }
                    _recordingTimer.Start();

                    _trayIcon.SetRecording(true);
                    break;

                case RecordingSession.State.Countdown:
                    // No visible change
                    break;

                default:
                    _recordingTimer?.Stop();
                    _pill?.Close();
                    _pill = null;
                    _pillVm = null;
                    _shellThread.InvokeAsync(() =>
                    {
                        _chrome?.CancelCountdown();
                        _chrome?.HideFrame();
                    });
                    _frameShown = false;
                    _trayIcon.SetRecording(false);
                    _trayIcon.UpdateTooltip("Lightshot");
                    break;
            }
        });
    }

    private void OnRecordingTimerTick(object? sender, EventArgs e)
    {
        var elapsed = _coordinator.RecordingElapsed;
        var eng = _recordingService.Engine;
        _pillVm?.Update(elapsed, eng.MicrophoneLevel, eng.ComputerAudioLevel, 0.1);

        if (_settingsStore.RecordingDefaults.ShowRecordingTimeInMenuBar)
        {
            _trayIcon.UpdateTooltip($"Lightshot - Recording {ControlsPillViewModel.FormatTime(elapsed)}");
        }
    }

    public Task<bool> RunRecordingCountdownAsync(int seconds) =>
        _shellThread.InvokeAsync(() => Chrome().RunCountdownAsync(seconds, _settingsStore.RecordingDefaults.PlaySounds)).Unwrap();

    public Task<bool> ConfirmRecordingRestartAsync() =>
        OnUiAsync(() => Task.FromResult(MessageBox.Show("Cancel this recording and start a new one?", "Lightshot", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK));

    public Task<bool> ConfirmRecordingDiscardAsync() =>
        OnUiAsync(() => Task.FromResult(MessageBox.Show("Delete this recording?", "Lightshot", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK));

    public Task<bool> ResolveMicrophoneDisconnectedAsync() =>
        OnUiAsync(() => Task.FromResult(MessageBox.Show("The microphone was disconnected. Continue recording without audio?", "Lightshot", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK));

    public void PresentRecordingFailure(RecordingError error)
    {
        OnUi(() =>
        {
            string message = error switch
            {
                RecordingError.DiskFull => "The disk is full. Free up some space and try again.",
                RecordingError.NoDisplayAvailable => "No display is available to record.",
                RecordingError.SystemFailure f => f.Message,
                _ => error.ToString()
            };
            ErrorDialog.ShowNotice(null, "Recording Failed", message);
        });
    }

    public void PresentPostRecordingOverlay(PendingRecording recording)
    {
        OnUi(() =>
        {
            var vm = new PostRecordingOverlayViewModel(recording, _mediaSink,
                onDismiss: name => _coordinator.DismissPendingRecording(name),
                onEditor: name => _coordinator.OpenPendingRecordingInEditor(name),
                onCopy: name => _coordinator.CopyPendingRecordingFile(name),
                onDelete: () => _coordinator.DeletePendingRecording());
            new PostRecordingOverlay(vm).Show();
        });
    }

    public void PresentRecordingFinished(string path)
    {
        OnUi(() => new DefaultExplorerService().RevealInExplorer(path));
    }

    public void OpenVideoEditor(string path, string? inputPath = null) => _ = OpenVideoEditorAsync(path);

    private async Task OpenVideoEditorAsync(string path)
    {
        try
        {
            var meta = await new MfMediaMetadata().VideoMetadataAsync(path);
            long bytes = new FileInfo(path).Length;
            await OnUiAsync(() =>
            {
                new VideoEditorWindow(new VideoEditorViewModel(path, meta?.Duration ?? 0,
                    new Lightshot.Core.Size(meta?.PixelWidth ?? 0, meta?.PixelHeight ?? 0), bytes)).Show();
                return Task.FromResult(true);
            });
        }
        catch (Exception ex)
        {
            OnUi(() => ErrorDialog.ShowNotice(null, "Video Editor", "The recording could not be opened.", ex.Message));
        }
    }

    public void PresentGifConversion(Action cancel)
    {
        OnUi(() =>
        {
            var vm = new ProgressPopupViewModel("Converting to GIF");
            vm.Cancelled += (_, _) => cancel();
            _gifPopup = new ProgressPopup(vm);
            _gifPopup.Show();
        });
    }

    public void UpdateGifConversion(double progress)
    {
        OnUi(() =>
        {
            if (_gifPopup?.DataContext is ProgressPopupViewModel vm)
            {
                vm.Progress = progress;
            }
        });
    }

    public void DismissGifConversion()
    {
        OnUi(() =>
        {
            _gifPopup?.Close();
            _gifPopup = null;
        });
    }

    public Task<bool> ResolveCancelledGifConversionAsync() =>
        OnUiAsync(() => Task.FromResult(MessageBox.Show("GIF conversion was cancelled. Keep the video instead?", "Lightshot", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes));

    public void PresentRecordingPreparation(Action cancel)
    {
        OnUi(() =>
        {
            var vm = new ProgressPopupViewModel("Preparing recording");
            vm.Cancelled += (_, _) => cancel();
            _prepPopup = new ProgressPopup(vm);
            _prepPopup.Show();
        });
    }

    public void UpdateRecordingPreparation(double progress)
    {
        OnUi(() =>
        {
            if (_prepPopup?.DataContext is ProgressPopupViewModel vm)
            {
                vm.Progress = progress;
            }
        });
    }

    public void DismissRecordingPreparation()
    {
        OnUi(() =>
        {
            _prepPopup?.Close();
            _prepPopup = null;
        });
    }

    private void DisposeRecording()
    {
        OnUi(() =>
        {
            _recordingTimer?.Stop();
            _pill?.Close();
            _pill = null;
            _pillVm = null;
            _gifPopup?.Close();
            _gifPopup = null;
            _prepPopup?.Close();
            _prepPopup = null;
        });

        _shellThread.Invoke(() => _chrome?.Dispose());
        _recordingService.Dispose();
    }

    private async Task RecoverScratchAsync()
    {
        try
        {
            var result = await RecordingRecovery.RecoverAsync(_scratchStore);
            var delivered = new List<string>();
            foreach (var file in result.RecoveredFiles)
            {
                try
                {
                    var pending = await _coordinator.ArchiveRecoveredRecordingAsync(file);
                    var dest = UniqueDestination(_settingsStore.RecordingDestination(Path.GetExtension(file).TrimStart('.')));
                    if (pending.HistoryRecordId != null) _mediaSink.Copy(pending.File, dest); else _mediaSink.Save(pending.File, dest);
                    delivered.Add(dest);
                }
                catch (Exception ex)
                {
                    // Never delete: the file stays in scratch and is retried at the next launch.
                    Trace.TraceError($"Recording recovery failed for {file}: {ex}");
                }
            }

            if (delivered.Count + result.UnfinalizedTakes.Count == 0 || IsTestMode()) return;

            OnUi(() => ErrorDialog.ShowNotice(null, "Recovered Recordings",
                "Lightshot found recordings from a previous session.",
                string.Join("\n", delivered.Concat(result.UnfinalizedTakes.Select(t => t + " (not finalised)")))));
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Recording recovery failed: {ex}");
        }
    }

    private static string UniqueDestination(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} {i}{ext}").Replace('\\', '/');
            if (!File.Exists(candidate)) return candidate;
        }
    }

    internal static bool IsTestMode()
    {
        return string.Equals(Environment.GetEnvironmentVariable("LIGHTSHOT_TEST_MODE"), "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Environment.GetEnvironmentVariable("LIGHTSHOT_DISABLE_ONBOARDING"), "1", StringComparison.OrdinalIgnoreCase);
    }
}
