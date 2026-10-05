using System;
using System.Windows;
using Lightshot.App.Theming;

namespace Lightshot.App.Views.Onboarding;

public partial class OnboardingWindow : Window
{
    public OnboardingViewModel? ViewModel => DataContext as OnboardingViewModel;

    public OnboardingWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public OnboardingWindow(OnboardingViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeService.Instance.ApplyWindowTheme(this);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null && ViewModel.IsPrintScreenClaimedBySnippingTool)
        {
            SnippingBanner.Visibility = Visibility.Visible;
        }
    }

    private void OnOpenKeyboardSettingsClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.OpenKeyboardSettings();
    }

    private void OnGetStartedClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.CompleteOnboarding();
        Close();
    }
}
