using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CloudlogHelper.Converters;

public class FailReasonToTooltipConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string failReason) return "This QSO is being processed.";
        return !string.IsNullOrEmpty(failReason) ? failReason : "This QSO has no error message.";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}