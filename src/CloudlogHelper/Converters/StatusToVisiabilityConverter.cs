using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CloudlogHelper.Enums;

namespace CloudlogHelper.Converters;

public class StatusToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return false;

        if (value is UploadStatus status)
        {
            return status switch
            {
                UploadStatus.Success => false,
                _ => true
            };
        }

        if (value is RigUploadStatus rstatus)
        {
           return rstatus switch
            {
                RigUploadStatus.Unknown => false,
                _ => true,
            };
        }

        if (value is RigCommStatus cstatus)
        {
            return cstatus switch
            {
                RigCommStatus.Unknown => false,
                _ => true,
            };
        }
        
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}