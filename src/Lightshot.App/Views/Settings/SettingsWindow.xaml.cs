using System;
using System.Windows;
using Lightshot.App.Theming;

namespace Lightshot.App.Views.Settings;

public partial class SettingsWindow : Window
{
    public SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeService.Instance.ApplyWindowTheme(this);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
