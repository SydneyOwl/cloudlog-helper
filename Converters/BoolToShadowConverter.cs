using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CloudlogHelper.Converters;

public class BoolToShadowConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return BoxShadows.Parse("0 0 20 0 Red");
        return value?.ToString() switch
        {
            "True" => BoxShadows.Parse("0 0 40 0 LawnGreen"),
            _ => BoxShadows.Parse("0 0 40 0 Red")
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}