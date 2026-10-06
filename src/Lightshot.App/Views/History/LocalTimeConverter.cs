using System;
using System.Globalization;
using System.Windows.Data;

namespace Lightshot.App.Views.History;

// History stores timestamps in UTC so they sort correctly; the user reads them in local time.
public class LocalTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is DateTime time ? time.ToLocalTime() : value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is DateTime time ? time.ToUniversalTime() : value;
    }
}
