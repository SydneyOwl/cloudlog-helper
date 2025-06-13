using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CloudlogHelper.Converters;

public class EllipsisMiddleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str) return value;
        
        int keep = 9; 
        if (str.Length <= keep * 3) return str;
        
        return $"{str.Substring(0, keep)}...{str.Substring(str.Length - keep*2)}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}