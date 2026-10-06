// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Lightshot.App.Views.Settings;

public partial class UpdatePane : UserControl
{
    public UpdatePane()
    {
        InitializeComponent();
    }

    private async void OnCheckNowClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is SettingsViewModel vm)
            {
                await vm.Updates.CheckNowAsync();
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Check now failed: {ex}");
        }
    }

    private async void OnRestartClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is SettingsViewModel vm)
            {
                await vm.Updates.RestartAsync();
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Restart to update failed: {ex}");
        }
    }
}
