using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CloudlogHelper.Enums;
using CloudlogHelper.Resources;
using CloudlogHelper.Resources.Language;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Converters;

public class UploadStatusToLangKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            UploadStatus.Uploading => TranslationHelper.GetString(Language.UploadStatusUploading),
            UploadStatus.Retrying => TranslationHelper.GetString(Language.UploadStatusRetrying),
            UploadStatus.Pending => TranslationHelper.GetString(Language.UploadStatusPending),
            UploadStatus.Success => TranslationHelper.GetString(Language.UploadStatusSuccess),
            UploadStatus.Fail => TranslationHelper.GetString(Language.UploadStatusFailed),
            UploadStatus.Ignored => TranslationHelper.GetString(Language.UploadStatusIgnored),
            _ => TranslationHelper.GetString(Language.Unknown)
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
