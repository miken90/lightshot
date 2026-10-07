using System;
using System.IO;
using System.Windows;
using Lightshot.App.Views.Editor;
using Lightshot.Platform.Windows.Displays;
using Microsoft.Win32;

namespace Lightshot.App.Views.VideoEditor;

public partial class VideoEditorWindow : Window
{
    public VideoEditorViewModel? ViewModel => DataContext as VideoEditorViewModel;

    private const double DefaultWindowWidth = 1000;
    private const double DefaultWindowHeight = 700;

    public VideoEditorWindow()
    {
        InitializeComponent();
        // Every caller shows the editor right after constructing it, from the monitor the pointer is on.
        WindowPlacement.PlaceOnDisplayAt(this, DisplayTopology.GetCursorPosition(), CalculateWindowSize);
        Loaded += OnLoaded;
    }

    /// <summary>
    /// The default size, capped like the image editor so a short secondary keeps a margin around it.
    /// </summary>
    public static (double Width, double Height) CalculateWindowSize(double workAreaWidth, double workAreaHeight) =>
        (Math.Min(DefaultWindowWidth, workAreaWidth * EditorViewModel.WorkAreaWidthCapRatio),
         Math.Min(DefaultWindowHeight, workAreaHeight * EditorViewModel.WorkAreaHeightCapRatio));

    public static Lightshot.Core.Rect CalculateFrame(Lightshot.Core.Rect workAreaPhysical, double windowScale) =>
        WindowPlacement.CalculateFrame(workAreaPhysical, windowScale, CalculateWindowSize);

    public VideoEditorWindow(VideoEditorViewModel viewModel) : this()
    {
        DataContext = viewModel;
        EditorTrimBar.DataContext = viewModel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && File.Exists(ViewModel.FilePath))
        {
            try
            {
                Player.Source = new Uri(ViewModel.FilePath, UriKind.Absolute);
                Player.Play();
            }
            catch
            {
                // Degrade gracefully
            }
        }
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            Player.Position = TimeSpan.FromSeconds(ViewModel.TrimStart);
            Player.Play();
        }
        else
        {
            Player.Position = TimeSpan.Zero;
            Player.Play();
        }
    }

    private async void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var sfd = new SaveFileDialog
        {
            Filter = "MP4 Video (*.mp4)|*.mp4|All Files (*.*)|*.*",
            FileName = Path.GetFileNameWithoutExtension(ViewModel.FilePath) + " (Trimmed).mp4"
        };

        if (sfd.ShowDialog() == true)
        {
            await ViewModel.SaveAsNewAsync(sfd.FileName);
        }
    }

    private async void OnReplaceClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var result = MessageBox.Show(
            "Replace the original recording? A backup will be preserved for Revert.",
            "Confirm Replace",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await ViewModel.ReplaceOriginalAsync();
        }
    }

    private void OnRevertClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.RevertToOriginal();
    }
}
