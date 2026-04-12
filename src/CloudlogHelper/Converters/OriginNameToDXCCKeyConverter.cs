using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Converters;

public class OriginNameToDXCCKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return TranslationHelper.GetDxccDisplayName(value?.ToString());
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
