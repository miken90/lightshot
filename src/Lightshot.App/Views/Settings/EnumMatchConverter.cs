// MIT License, Copyright (c) 2026 Viet Le

using System;
using System.Globalization;
using System.Windows.Data;

namespace Lightshot.App.Views.Settings;

[ValueConversion(typeof(Enum), typeof(bool))]
public class EnumMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return value.Equals(parameter);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter != null) return parameter;
        return Binding.DoNothing;
    }
}
