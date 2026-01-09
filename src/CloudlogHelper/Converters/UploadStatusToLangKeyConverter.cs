using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Converters;

public class UploadStatusToLangKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return "Unknown";
        return "t_" + value;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}