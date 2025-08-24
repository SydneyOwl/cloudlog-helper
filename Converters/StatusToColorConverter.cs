using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CloudlogHelper.Enums;

namespace CloudlogHelper.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return Brushes.Red;
        return value switch
        {
            StatusLightEnum.Running => Brushes.LawnGreen,
            StatusLightEnum.Stopped =>Brushes.Red,
            StatusLightEnum.Loading => Brushes.Yellow,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}