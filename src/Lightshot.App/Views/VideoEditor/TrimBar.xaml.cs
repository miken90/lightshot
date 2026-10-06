using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Lightshot.App.Views.VideoEditor;

public partial class TrimBar : UserControl
{
    private double _duration = 10.0;
    private double _trimStart = 0.0;
    private double _trimEnd = 10.0;
    private double _playhead = 0.0;
    private bool _isDraggingStart;
    private bool _isDraggingEnd;

    public event EventHandler<(double Start, double End)>? TrimChanged;

    public VideoEditorViewModel? ViewModel => DataContext as VideoEditorViewModel;

    public TrimBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            _duration = ViewModel.Duration;
            _trimStart = ViewModel.TrimStart;
            _trimEnd = ViewModel.TrimEnd;
        }
        UpdateVisuals();
    }

    public void SetDuration(double duration)
    {
        _duration = Math.Max(0.5, duration);
        _trimStart = 0.0;
        _trimEnd = _duration;
        UpdateVisuals();
    }

    public void SetTrim(double start, double end)
    {
        _trimStart = Math.Max(0.0, start);
        _trimEnd = Math.Min(_duration, Math.Max(_trimStart + 0.5, end));
        UpdateVisuals();
    }

    public void SetPlayhead(double position)
    {
        _playhead = Math.Clamp(position, 0.0, _duration);
        UpdatePlayheadVisual();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        double width = TrackCanvas.ActualWidth;
        if (width <= 0 || _duration <= 0) return;

        double startX = (_trimStart / _duration) * width;
        double endX = (_trimEnd / _duration) * width;

        Canvas.SetLeft(StartThumb, Math.Max(0, startX - StartThumb.ActualWidth));
        Canvas.SetLeft(EndThumb, Math.Min(width - EndThumb.ActualWidth, endX));

        Canvas.SetLeft(SelectedRangeRect, startX);
        SelectedRangeRect.Width = Math.Max(0, endX - startX);

        StartLabel.Text = FormatTime(_trimStart);
        EndLabel.Text = FormatTime(_trimEnd);
        DurationLabel.Text = FormatTime(_trimEnd - _trimStart);

        if (ViewModel != null && ViewModel.IsTrimOnly)
        {
            SnappedKeyframeText.Text = $"Trim snaps to keyframe at: {FormatTime(ViewModel.SnappedKeyFrameTime)}";
            SnappedKeyframeText.Visibility = Visibility.Visible;
        }
        else
        {
            SnappedKeyframeText.Visibility = Visibility.Collapsed;
        }

        UpdatePlayheadVisual();
    }

    private void UpdatePlayheadVisual()
    {
        double width = TrackCanvas.ActualWidth;
        if (width <= 0 || _duration <= 0) return;

        double playheadX = (_playhead / _duration) * width;
        PlayheadLine.X1 = playheadX;
        PlayheadLine.X2 = playheadX;
    }

    private void OnStartThumbMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingStart = true;
        StartThumb.CaptureMouse();
    }

    private void OnEndThumbMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingEnd = true;
        EndThumb.CaptureMouse();
    }

    private void OnThumbMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingStart)
        {
            _isDraggingStart = false;
            StartThumb.ReleaseMouseCapture();
            NotifyTrimChanged();
        }
        if (_isDraggingEnd)
        {
            _isDraggingEnd = false;
            EndThumb.ReleaseMouseCapture();
            NotifyTrimChanged();
        }
    }

    private void OnStartThumbMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingStart) return;

        double mouseX = e.GetPosition(TrackCanvas).X;
        double width = TrackCanvas.ActualWidth;
        if (width <= 0) return;

        double targetSeconds = (mouseX / width) * _duration;
        _trimStart = Math.Clamp(targetSeconds, 0.0, _trimEnd - 0.5); // enforce min 0.5s

        UpdateVisuals();
    }

    private void OnEndThumbMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingEnd) return;

        double mouseX = e.GetPosition(TrackCanvas).X;
        double width = TrackCanvas.ActualWidth;
        if (width <= 0) return;

        double targetSeconds = (mouseX / width) * _duration;
        _trimEnd = Math.Clamp(targetSeconds, _trimStart + 0.5, _duration); // enforce min 0.5s

        UpdateVisuals();
    }

    private void NotifyTrimChanged()
    {
        ViewModel?.SetTrim(_trimStart, _trimEnd);
        TrimChanged?.Invoke(this, (_trimStart, _trimEnd));
    }

    private static string FormatTime(double seconds)
    {
        int whole = (int)Math.Floor(Math.Max(0, seconds));
        int mins = whole / 60;
        int secs = whole % 60;
        int ms = (int)((seconds - whole) * 10);
        return $"{mins:D2}:{secs:D2}.{ms}";
    }
}
