using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Converters;

public class UploadStatusToTranslatedStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return "?";
        return TranslationHelper.GetString("t_" + value);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}