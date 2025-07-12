using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CloudlogHelper.Converters;

public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return Brushes.Red;
        return value?.ToString() switch
        {
            "True" => Brushes.LawnGreen,
            _ => Brushes.Red
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}