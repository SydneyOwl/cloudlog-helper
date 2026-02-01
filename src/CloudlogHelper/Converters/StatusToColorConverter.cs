using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CloudlogHelper.Enums;

namespace CloudlogHelper.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return Brushes.Red;

        if (value is StatusLightEnum statusLight)
        {
            return statusLight switch
            {
                StatusLightEnum.Running => Brushes.LawnGreen,
                StatusLightEnum.Stopped => Brushes.Red,
                StatusLightEnum.Loading => Brushes.Yellow,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
            };
        }
        
        if (value is RigUploadStatus rigUploadStatus)
        {
            return new SolidColorBrush(rigUploadStatus switch
            {
                RigUploadStatus.Success    => Color.Parse("#4CAF50"),
                RigUploadStatus.Failed     => Color.Parse("#F44336"),
                RigUploadStatus.Uploading  => Color.Parse("#F7DC6F"),
                RigUploadStatus.Unknown    => Color.Parse("#9E9E9E"),
                _                          => Color.Parse("#F44336")
            });
        }
        
        if (value is RigCommStatus rigCommStatus)
        {
            return new SolidColorBrush(rigCommStatus switch
            {
                RigCommStatus.Success    => Color.Parse("#4CAF50"),
                RigCommStatus.Error     => Color.Parse("#F44336"),
                RigCommStatus.FetchingData  => Color.Parse("#F7DC6F"),
                RigCommStatus.Unknown    => Color.Parse("#9E9E9E"),
                _                          => Color.Parse("#F44336")
            });
        }
        return Brushes.Red;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}