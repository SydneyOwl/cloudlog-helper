using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CloudlogHelper.Enums;

namespace CloudlogHelper.Converters;

public class StatusToShadowConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return BoxShadows.Parse("0 0 20 0 Red");
        return value switch
        {
            StatusLightEnum.Running => BoxShadows.Parse("0 0 40 0 LawnGreen"),
            StatusLightEnum.Stopped => BoxShadows.Parse("0 0 40 0 Red"),
            StatusLightEnum.Loading => BoxShadows.Parse("0 0 40 0 Yellow"),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}