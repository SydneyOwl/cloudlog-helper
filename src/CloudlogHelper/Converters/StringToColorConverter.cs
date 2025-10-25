using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CloudlogHelper.Converters;

public class StringToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // ‘USB’, ‘LSB’, ‘CW’, ‘CWR’, ‘RTTY’, ‘RTTYR’, ‘AM’, ‘FM’, ‘WFM’,
        // ‘AMS’, ‘PKTLSB’, ‘PKTUSB’, ‘PKTFM’, ‘ECSSUSB’, ‘ECSSLSB’, ‘FA’,
        // ‘SAM’, ‘SAL’, ‘SAH’, ‘DSB’.
        if (value is null) return Brushes.Red;
        return value?.ToString() switch
        {
            // Upload status
            "Fail" => Brushes.Red,
            "Success" => Brushes.LawnGreen,
            "Uploading" => Brushes.Orange,
            "Retrying" => Brushes.BlueViolet,
            // Rig mode
            "USB" => Brushes.DodgerBlue,
            "LSB" => Brushes.RoyalBlue,
            "CW" => Brushes.Gold,
            "CWR" => Brushes.Goldenrod,
            "RTTY" => Brushes.MediumPurple,
            "RTTYR" => Brushes.MediumOrchid,
            "AM" => Brushes.OrangeRed,
            "FM" => Brushes.LimeGreen,
            "WFM" => Brushes.ForestGreen,
            "AMS" => Brushes.Coral,
            "PKTLSB" => Brushes.SteelBlue,
            "PKTUSB" => Brushes.DeepSkyBlue,
            "PKTFM" => Brushes.MediumSeaGreen,
            "ECSSUSB" => Brushes.CornflowerBlue,
            "ECSSLSB" => Brushes.SlateBlue,
            "FA" => Brushes.HotPink,
            "SAM" => Brushes.Tomato,
            "SAL" => Brushes.Salmon,
            "SAH" => Brushes.LightCoral,
            "DSB" => Brushes.DarkOrange,
            // wavelength
            "160m" => Brushes.DarkSlateBlue,
            "80m" => Brushes.MediumPurple,
            "60m" => Brushes.Orchid,
            "40m" => Brushes.MediumSlateBlue,
            "30m" => Brushes.SteelBlue,
            "20m" => Brushes.DodgerBlue,
            "17m" => Brushes.DeepSkyBlue,
            "15m" => Brushes.LightSkyBlue,
            "12m" => Brushes.Turquoise,
            "10m" => Brushes.MediumTurquoise,
            "6m" => Brushes.LightSeaGreen,
            "2m" => Brushes.LimeGreen,
            "1.25m" => Brushes.YellowGreen,
            "70cm" => Brushes.Gold,
            "33cm" => Brushes.Goldenrod,
            "23cm" => Brushes.Orange,
            // digimode
            "FT8" => Brushes.MediumPurple,
            "FT4" => Brushes.MediumOrchid,
            "JS8" => Brushes.DarkOrchid,
            "PSK31" => Brushes.SteelBlue,
            "PSK63" => Brushes.LightSteelBlue,
            "Olivia" => Brushes.Teal,
            "Contestia" => Brushes.DarkCyan,
            "JT65" => Brushes.MediumVioletRed,
            "JT9" => Brushes.DeepPink,
            "MSK144" => Brushes.HotPink,
            "WSPR" => Brushes.LightPink,
            "Hellschreiber" => Brushes.DarkOrange,
            "Packet" => Brushes.LimeGreen,
            _ => Brushes.DeepSkyBlue
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}