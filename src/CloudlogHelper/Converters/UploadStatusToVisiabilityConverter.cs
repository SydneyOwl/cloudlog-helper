using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;

namespace CloudlogHelper.Converters;

public class UploadStatusToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return "?";
        return (value as UploadStatus?) switch
        {
            UploadStatus.Success => false,
            _ => true
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}