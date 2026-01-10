using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Converters;

public class OriginNameToDXCCKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return "Unknown";
        var originName = value.ToString();
        return originName is null ? 
            "Unknown" : 
            TranslationHelper.ParseToDXCCKey(originName);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}