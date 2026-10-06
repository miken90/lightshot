using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Lightshot.Platform.Windows.Notifications;

namespace Lightshot.App.Views.Notices;

public partial class NotificationGuidanceBanner : UserControl
{
    public event EventHandler? Dismissed;

    public NotificationGuidanceBanner()
    {
        InitializeComponent();
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        OpenNotificationSettings();
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    public static bool OpenNotificationSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = NotificationStateProbe.SettingsUri,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
