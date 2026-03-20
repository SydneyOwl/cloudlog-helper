using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CloudlogHelper.Enums;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Converters;

public class UploadStatusToLangKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            UploadStatus.Uploading => LangKeys.UploadStatusUploading,
            UploadStatus.Retrying => LangKeys.UploadStatusRetrying,
            UploadStatus.Pending => LangKeys.UploadStatusPending,
            UploadStatus.Success => LangKeys.UploadStatusSuccess,
            UploadStatus.Fail => LangKeys.UploadStatusFailed,
            UploadStatus.Ignored => LangKeys.UploadStatusIgnored,
            _ => LangKeys.Unknown
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
