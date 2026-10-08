using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace Lightshot.App.Views.Settings;

[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : false;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : false;
}

[ValueConversion(typeof(double), typeof(double))]
public class PercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? Math.Round(d * 100.0) : 90.0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? Math.Clamp(d / 100.0, 0.1, 1.0) : 0.9;
}

public partial class ScreenshotsPane : UserControl
{
    public ScreenshotsPane()
    {
        InitializeComponent();
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Screenshot Save Location"
        };
        if (dialog.ShowDialog() == true)
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.SaveLocation = dialog.FolderName;
            }
        }
    }
}
