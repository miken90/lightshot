// Ported from LightshotKit/Sources/LightshotKit/PostRecordingOverlayController.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Lightshot.App.Views.QuickAccess;
using Lightshot.Core;

namespace Lightshot.App.Views.Recording;

public class PostRecordingOverlayViewModel : INotifyPropertyChanged, IDisposable
{
    public const int DefaultTimeoutSeconds = 20;

    private readonly IMediaSink _mediaSink;
    private readonly IClock _clock;
    private readonly Action<string>? _onDismiss;
    private readonly Action<string>? _onEditor;

    private PendingRecording _recording;
    private string _name;
    private bool _isSettled;
    private bool _isDeleted;
    private IDisposable? _timerSubscription;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Settled;

    public PostRecordingOverlayViewModel(
        PendingRecording recording,
        IMediaSink mediaSink,
        IClock? clock = null,
        Action<string>? onDismiss = null,
        Action<string>? onEditor = null)
    {
        _recording = recording ?? throw new ArgumentNullException(nameof(recording));
        _mediaSink = mediaSink ?? throw new ArgumentNullException(nameof(mediaSink));
        _clock = clock ?? SystemClock.Instance;
        _onDismiss = onDismiss;
        _onEditor = onEditor;

        _name = !string.IsNullOrWhiteSpace(recording.SuggestedName)
            ? recording.SuggestedName
            : Path.GetFileNameWithoutExtension(recording.File);

        StartTimer();
    }

    public PendingRecording Recording => _recording;

    public string FilePath => _recording.File;

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
                ResetTimer();
            }
        }
    }

    public double Duration => _recording.Duration;

    public string FormattedDuration
    {
        get
        {
            int whole = (int)Math.Floor(Math.Max(0, Duration));
            return $"{whole / 60:D2}:{whole % 60:D2}";
        }
    }

    public RecordingOutputKind Kind => _recording.Kind;

    public bool IsVideo => _recording.Kind == RecordingOutputKind.Video;

    public bool IsGif => _recording.Kind == RecordingOutputKind.Gif;

    public bool CanEdit => IsVideo;

    public bool IsSettled
    {
        get => _isSettled;
        private set
        {
            if (_isSettled != value)
            {
                _isSettled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsDeleted
    {
        get => _isDeleted;
        private set
        {
            if (_isDeleted != value)
            {
                _isDeleted = value;
                OnPropertyChanged();
            }
        }
    }

    private void StartTimer()
    {
        _timerSubscription?.Dispose();
        _timerSubscription = _clock.Schedule(TimeSpan.FromSeconds(DefaultTimeoutSeconds), OnTimerFired);
    }

    public void ResetTimer()
    {
        if (IsSettled) return;
        StartTimer();
    }

    private void CancelTimer()
    {
        _timerSubscription?.Dispose();
        _timerSubscription = null;
    }

    private void OnTimerFired()
    {
        if (IsSettled) return;
        AutoSave();
    }

    public void AutoSave()
    {
        if (IsSettled) return;
        CancelTimer();
        IsSettled = true;
        _onDismiss?.Invoke(_name);
        Settled?.Invoke(this, EventArgs.Empty);
    }

    public void Copy()
    {
        if (IsSettled) return;
        CancelTimer();
        _mediaSink.CopyFile(FilePath);
        IsSettled = true;
        Settled?.Invoke(this, EventArgs.Empty);
    }

    public void Save(string? destinationPath = null)
    {
        if (IsSettled) return;
        CancelTimer();

        if (!string.IsNullOrEmpty(destinationPath))
        {
            _mediaSink.Save(FilePath, destinationPath);
        }

        IsSettled = true;
        _onDismiss?.Invoke(_name);
        Settled?.Invoke(this, EventArgs.Empty);
    }

    public void Trash()
    {
        if (IsSettled) return;
        CancelTimer();
        _mediaSink.Trash(FilePath);
        IsDeleted = true;
        IsSettled = true;
        Settled?.Invoke(this, EventArgs.Empty);
    }

    public void Delete() => Trash();

    public void Dismiss()
    {
        if (IsSettled) return;
        AutoSave();
    }

    public void OpenEditor()
    {
        if (IsSettled) return;
        CancelTimer();
        IsSettled = true;
        _onEditor?.Invoke(_name);
        Settled?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        CancelTimer();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
