using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CloudlogHelper.Enums;
using Material.Icons;

namespace CloudlogHelper.Converters;

public class RigUploadStatusToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = (value as RigUploadStatus?) switch
        {
            null                   => Color.Parse("#F44336"),
            RigUploadStatus.Success    => Color.Parse("#4CAF50"),
            RigUploadStatus.Failed     => Color.Parse("#F44336"),
            RigUploadStatus.Uploading  => Color.Parse("#F7DC6F"),
            RigUploadStatus.Unknown    => Color.Parse("#9E9E9E"),
            _                          => Color.Parse("#F44336")
        };

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}