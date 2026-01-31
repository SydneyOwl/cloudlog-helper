using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CloudlogHelper.Enums;
using Material.Icons;

namespace CloudlogHelper.Converters;

public class RigUploadStatusToAnimationConverter : IValueConverter
{ 
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var ani = (value as RigUploadStatus?) switch
        {
            RigUploadStatus.Uploading  => MaterialIconAnimation.FadeInOut,
            _                          => MaterialIconAnimation.None
        };

        return ani;
    }
   

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}