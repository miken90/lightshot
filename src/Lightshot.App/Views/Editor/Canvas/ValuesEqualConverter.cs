// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Lightshot.App.Views.Editor;

/// <summary>
/// Selected state for presets and swatches.
/// </summary>
public sealed class ValuesEqualConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        return values != null &&
               values.Length == 2 &&
               values[0] != null &&
               values[0] != DependencyProperty.UnsetValue &&
               Equals(values[0], values[1]);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
