using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Lightshot.App.Views.Editor;
using Lightshot.Core;
using Lightshot.Platform.Windows.Displays;
using Lightshot.Platform.Windows.Recording;

namespace Lightshot.App.Views.Recording;

public partial class MediaViewerWindow : Window
{
    private const double DefaultWindowWidth = 800;
    private const double DefaultWindowHeight = 540;

    private readonly string _filePath;
    private bool _isPlaying = true;

    public MediaViewerWindow()
    {
        InitializeComponent();
        _filePath = string.Empty;
    }

    public MediaViewerWindow(string filePath, CaptureRegion? recordedRegion = null) : this()
    {
        _filePath = filePath;
        try
        {
            var target = ResolveTargetPoint(DisplayTopology.GetDisplays(), recordedRegion, DisplayTopology.GetCursorPosition());
            WindowPlacement.PlaceOnDisplayAt(this, target, CalculateWindowSize);
        }
        catch
        {
            // Topology failures keep the XAML's centred placement
        }
        Loaded += OnLoaded;
    }

    /// <summary>
    /// A physical point on the monitor the viewer opens on: the one that showed the recording, else the pointer's.
    /// </summary>
    public static Lightshot.Core.Point ResolveTargetPoint(
        IReadOnlyList<DisplayInfo> displays, CaptureRegion? recordedRegion, Lightshot.Core.Point cursor) =>
        recordedRegion is null || displays.Count == 0
            ? cursor
            : RecordingDisplayResolver.Resolve(displays, recordedRegion).Display.WorkArea.Center;

    /// <summary>
    /// The default size, capped like the editors so a short secondary keeps a margin around it.
    /// </summary>
    public static (double Width, double Height) CalculateWindowSize(double workAreaWidth, double workAreaHeight) =>
        (Math.Min(DefaultWindowWidth, workAreaWidth * EditorViewModel.WorkAreaWidthCapRatio),
         Math.Min(DefaultWindowHeight, workAreaHeight * EditorViewModel.WorkAreaHeightCapRatio));

    public static Lightshot.Core.Rect CalculateFrame(Lightshot.Core.Rect workAreaPhysical, double windowScale) =>
        WindowPlacement.CalculateFrame(workAreaPhysical, windowScale, CalculateWindowSize);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
        {
            MediaPlayer.Source = new Uri(_filePath, UriKind.Absolute);
            MediaPlayer.Play();
            _isPlaying = true;
        }
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e)
    {
        MediaPlayer.Position = TimeSpan.Zero;
        MediaPlayer.Play();
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        TogglePlayPause();
    }

    private void TogglePlayPause()
    {
        if (_isPlaying)
        {
            MediaPlayer.Pause();
            _isPlaying = false;
            PlayPauseIcon.Text = "\uE768";
        }
        else
        {
            MediaPlayer.Play();
            _isPlaying = true;
            PlayPauseIcon.Text = "\uE769";
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            TogglePlayPause();
            e.Handled = true;
        }
    }
}
