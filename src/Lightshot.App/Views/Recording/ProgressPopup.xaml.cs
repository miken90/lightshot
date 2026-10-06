using System.Windows;

namespace Lightshot.App.Views.Recording;

public partial class ProgressPopup : Window
{
    public ProgressPopupViewModel? ViewModel => DataContext as ProgressPopupViewModel;

    public ProgressPopup()
    {
        InitializeComponent();
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
