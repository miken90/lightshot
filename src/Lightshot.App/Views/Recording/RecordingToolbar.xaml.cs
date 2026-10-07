using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lightshot.Platform.Windows.Displays;

namespace Lightshot.App.Views.Recording;

public partial class RecordingToolbar : UserControl
{
    public RecordingToolbarViewModel? ViewModel => DataContext as RecordingToolbarViewModel;

    public RecordingToolbar()
    {
        InitializeComponent();
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetFullscreen(DisplayTopology.GetDisplays());
    }

    private void OnRecordGifClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.StartGif();
    }

    private void OnRecordVideoClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.StartVideo();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                ViewModel?.StartGif();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.None)
            {
                ViewModel?.StartVideo();
                e.Handled = true;
            }
        }
    }
}
