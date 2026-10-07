using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Lightshot.Core;

namespace Lightshot.App.Views.Recording;

public partial class PostRecordingOverlay : Window
{
    private System.Windows.Point? _dragStart;

    /// <summary>The region of the take that produced the recording; decides the monitor the overlay opens on.</summary>
    public CaptureRegion? RecordedRegion { get; set; }

    public PostRecordingOverlayViewModel? ViewModel => DataContext as PostRecordingOverlayViewModel;

    public PostRecordingOverlay()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public PostRecordingOverlay(PostRecordingOverlayViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.Settled += (s, e) => Dispatcher.Invoke(Close);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionInBottomRight();

        if (ViewModel != null && File.Exists(ViewModel.FilePath))
        {
            try
            {
                MediaPreview.Source = new Uri(ViewModel.FilePath, UriKind.Absolute);
            }
            catch
            {
                // Degrade gracefully if media cannot be loaded
            }
        }
    }

    private void PositionInBottomRight()
    {
        // The monitor of the take that produced this recording; the primary when none is known
        WindowPlacement.PlaceAnchoredOnRecordedDisplay(
            this, RecordedRegion ?? new CaptureRegion.DisplayRegion(0), PlacementAnchor.BottomRight, 16);
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e)
    {
        MediaPreview.Position = TimeSpan.Zero;
        MediaPreview.Play();
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _dragStart = e.GetPosition(this);
        }
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _dragStart.HasValue)
        {
            var currentPos = e.GetPosition(this);
            var diff = _dragStart.Value - currentPos;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _dragStart = null;
                if (ViewModel != null && File.Exists(ViewModel.FilePath))
                {
                    var data = new DataObject(DataFormats.FileDrop, new[] { ViewModel.FilePath });
                    DragDrop.DoDragDrop(PreviewContainer, data, DragDropEffects.Copy);
                }
            }
        }
    }

    private void OnTrashClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.Trash();
        Close();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.Copy();
        Close();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.Save();
        Close();
    }

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.OpenEditor();
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ViewModel?.Dismiss();
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            if (ViewModel != null && File.Exists(ViewModel.FilePath))
            {
                var viewer = new MediaViewerWindow(ViewModel.FilePath, RecordedRegion);
                viewer.Owner = this;
                viewer.ShowDialog();
                e.Handled = true;
            }
        }
    }
}
