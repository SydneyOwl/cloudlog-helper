using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CloudlogHelper.Converters;

public class StringToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return Brushes.Red;
        return value?.ToString() switch
        {
            "Fail" => Brushes.Red,
            "Success" => Brushes.LawnGreen,
            "Uploading" => Brushes.Orange,
            "Retrying" => Brushes.BlueViolet,
            _ => Brushes.DeepSkyBlue
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}