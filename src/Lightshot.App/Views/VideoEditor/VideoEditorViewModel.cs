// Ported from LightshotKit/Sources/LightshotKit/VideoEditorController.swift
// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lightshot.Core;
using Lightshot.Platform.Windows.Media;

namespace Lightshot.App.Views.VideoEditor;

public class VideoEditorViewModel : INotifyPropertyChanged
{
    private readonly MfVideoTrimmer _trimmer;
    private readonly MfTranscoder _transcoder;
    private readonly MfMediaMetadata _metadata;

    private string _filePath;
    private string? _backupPath;
    private double _duration;
    private Size _sourceDimensions;
    private double _sourceFps;
    private long _sourceFileSizeBytes;
    private int _audioTracksCount;
    private TrimRange _trimRange;
    private DimensionPreset _dimensionPreset = DimensionPreset.Original;
    private Size _targetDimensions;
    private double _quality = VideoBitRate.DefaultQuality;
    private AudioEdit _audioEdit = AudioEdit.Unchanged.Instance;
    private bool _isTrimOnly;
    private double _snappedKeyFrameTime;
    private double _estimatedSizeBytes;
    private bool _isModified;
    private bool _isBusy;
    private double _progress;
    private string? _statusMessage;

    public event PropertyChangedEventHandler? PropertyChanged;

    public VideoEditorViewModel(
        string filePath,
        double duration,
        Size sourceDimensions,
        long sourceFileSizeBytes = 0,
        double sourceFps = 30.0,
        int audioTracks = 1,
        MfVideoTrimmer? trimmer = null,
        MfTranscoder? transcoder = null,
        MfMediaMetadata? metadata = null)
    {
        _filePath = filePath;
        _duration = Math.Max(0.5, duration);
        _sourceDimensions = sourceDimensions;
        _sourceFps = sourceFps > 0 ? sourceFps : 30.0;
        _sourceFileSizeBytes = sourceFileSizeBytes > 0 ? sourceFileSizeBytes : (File.Exists(filePath) ? new FileInfo(filePath).Length : 1_000_000);
        _audioTracksCount = Math.Max(0, audioTracks);

        _trimmer = trimmer ?? new MfVideoTrimmer();
        _transcoder = transcoder ?? new MfTranscoder();
        _metadata = metadata ?? new MfMediaMetadata();

        _trimRange = new TrimRange(_duration);
        _targetDimensions = VideoDimensions.CalculateSize(_dimensionPreset, _sourceDimensions);
        _snappedKeyFrameTime = _trimRange.Start;

        RecalculateEstimate();
    }

    public string FilePath
    {
        get => _filePath;
        private set
        {
            if (_filePath != value)
            {
                _filePath = value;
                OnPropertyChanged();
            }
        }
    }

    public double Duration => _duration;

    public Size SourceDimensions => _sourceDimensions;

    public double SourceFps => _sourceFps;

    public long SourceFileSizeBytes => _sourceFileSizeBytes;

    public TrimRange TrimRange => _trimRange;

    public double TrimStart => _trimRange.Start;

    public double TrimEnd => _trimRange.End;

    public double TrimLength => _trimRange.Length;

    public DimensionPreset DimensionPreset
    {
        get => _dimensionPreset;
        set
        {
            if (_dimensionPreset != value)
            {
                _dimensionPreset = value;
                _targetDimensions = VideoDimensions.CalculateSize(value, _sourceDimensions);
                _isModified = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TargetDimensions));
                RecalculateEstimate();
            }
        }
    }

    public Size TargetDimensions => _targetDimensions;

    public double Quality
    {
        get => _quality;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_quality - clamped) > 0.001)
            {
                _quality = clamped;
                _isModified = true;
                OnPropertyChanged();
                RecalculateEstimate();
            }
        }
    }

    public AudioEdit AudioEdit
    {
        get => _audioEdit;
        set
        {
            if (!Equals(_audioEdit, value))
            {
                _audioEdit = value;
                _isModified = true;
                OnPropertyChanged();
                RecalculateEstimate();
            }
        }
    }

    public bool IsTrimOnly
    {
        get => _isTrimOnly;
        set
        {
            if (_isTrimOnly != value)
            {
                _isTrimOnly = value;
                OnPropertyChanged();
                RecalculateEstimate();
            }
        }
    }

    public double SnappedKeyFrameTime
    {
        get => _snappedKeyFrameTime;
        set
        {
            if (Math.Abs(_snappedKeyFrameTime - value) > 0.001)
            {
                _snappedKeyFrameTime = value;
                OnPropertyChanged();
            }
        }
    }

    public double EstimatedSizeBytes
    {
        get => _estimatedSizeBytes;
        private set
        {
            if (Math.Abs(_estimatedSizeBytes - value) > 0.001)
            {
                _estimatedSizeBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedEstimatedSize));
            }
        }
    }

    public string FormattedEstimatedSize
    {
        get
        {
            double mb = _estimatedSizeBytes / (1024.0 * 1024.0);
            return mb < 0.1 ? $"{_estimatedSizeBytes / 1024.0:F1} KB" : $"{mb:F1} MB";
        }
    }

    public bool IsModified => _isModified;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
            }
        }
    }

    public double Progress
    {
        get => _progress;
        private set
        {
            if (Math.Abs(_progress - value) > 0.01)
            {
                _progress = value;
                OnPropertyChanged();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public void SetTrim(double start, double end)
    {
        _trimRange.SetStart(start);
        _trimRange.SetEnd(end);

        // In Trim Only mode, snapped keyframe time is at or before start
        _snappedKeyFrameTime = Math.Max(0.0, Math.Floor(_trimRange.Start));

        _isModified = !_trimRange.IsWholeClip;
        OnPropertyChanged(nameof(TrimStart));
        OnPropertyChanged(nameof(TrimEnd));
        OnPropertyChanged(nameof(TrimLength));
        OnPropertyChanged(nameof(SnappedKeyFrameTime));
        RecalculateEstimate();
    }

    public void SetPreset(DimensionPreset preset)
    {
        DimensionPreset = preset;
    }

    public void SetQuality(double quality)
    {
        Quality = quality;
    }

    public void RecalculateEstimate()
    {
        if (_isTrimOnly)
        {
            EstimatedSizeBytes = SizeEstimator.EstimatedTrimOnlyBytes(_sourceFileSizeBytes, _trimRange);
        }
        else
        {
            var settings = new VideoEditSettings(_trimRange, _targetDimensions, _quality, _audioEdit);
            EstimatedSizeBytes = SizeEstimator.EstimatedBytes(settings, _sourceFps, _audioTracksCount);
        }
    }

    public async Task<bool> SaveAsNewAsync(string outputPath, CancellationToken ct = default)
    {
        IsBusy = true;
        Progress = 0;
        StatusMessage = "Exporting...";

        try
        {
            if (_isTrimOnly)
            {
                var result = await _trimmer.TrimAsync(
                    _filePath,
                    outputPath,
                    _trimRange,
                    p => Progress = p * 100.0,
                    ct);

                if (result.Success)
                {
                    SnappedKeyFrameTime = result.SnappedStartTime;
                    StatusMessage = "Export complete";
                    return true;
                }
                StatusMessage = result.ErrorMessage ?? "Trim failed";
                return false;
            }
            else
            {
                var settings = new VideoEditSettings(_trimRange, _targetDimensions, _quality, _audioEdit);
                var result = await _transcoder.TranscodeAsync(
                    _filePath,
                    outputPath,
                    settings,
                    _sourceFps,
                    p => Progress = p * 100.0,
                    ct);

                if (result.Success)
                {
                    StatusMessage = "Export complete";
                    return true;
                }
                StatusMessage = result.ErrorMessage ?? "Transcode failed";
                return false;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ReplaceOriginalAsync(CancellationToken ct = default)
    {
        string tempOutput = _filePath + ".edited.mp4";
        bool success = await SaveAsNewAsync(tempOutput, ct);
        if (!success) return false;

        try
        {
            _backupPath = _filePath + ".backup";
            if (File.Exists(_backupPath)) File.Delete(_backupPath);
            File.Move(_filePath, _backupPath);
            File.Move(tempOutput, _filePath);
            _isModified = false;
            return true;
        }
        catch
        {
            // Restore original on failure
            if (_backupPath != null && File.Exists(_backupPath) && !File.Exists(_filePath))
            {
                File.Move(_backupPath, _filePath);
            }
            return false;
        }
    }

    public bool RevertToOriginal()
    {
        if (_backupPath == null || !File.Exists(_backupPath)) return false;

        try
        {
            if (File.Exists(_filePath)) File.Delete(_filePath);
            File.Move(_backupPath, _filePath);
            _backupPath = null;
            SetTrim(0, _duration);
            DimensionPreset = DimensionPreset.Original;
            _isModified = false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
