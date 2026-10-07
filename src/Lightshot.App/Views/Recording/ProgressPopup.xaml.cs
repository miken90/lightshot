using System.Windows;
using Lightshot.Core;

namespace Lightshot.App.Views.Recording;

public partial class ProgressPopup : Window
{
    public ProgressPopupViewModel? ViewModel => DataContext as ProgressPopupViewModel;

    /// <summary>The region of the take being processed; the popup opens centred on its monitor.</summary>
    public CaptureRegion? RecordedRegion { get; set; }

    public ProgressPopup()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (RecordedRegion is { } region)
            {
                WindowPlacement.PlaceAnchoredOnRecordedDisplay(this, region, PlacementAnchor.Center, 0);
            }
        };
    }

    public ProgressPopup(ProgressPopupViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.Cancelled += (s, e) => Dispatcher.Invoke(Close);
        viewModel.CancelToVideoRequested += (s, e) => Dispatcher.Invoke(Close);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.Cancel();
        Close();
    }

    private void OnCancelToVideoClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.CancelToVideo();
        Close();
    }
}
