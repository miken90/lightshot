using System.Windows.Controls;

namespace Lightshot.App.Views.Settings;

public partial class GeneralPane : UserControl
{
    public GeneralPane()
    {
        InitializeComponent();
    }

    private void OnOpenStartupAppsClick(object sender, System.Windows.RoutedEventArgs e)
    {
        (DataContext as SettingsViewModel)?.OpenStartupSettings();
    }
}
