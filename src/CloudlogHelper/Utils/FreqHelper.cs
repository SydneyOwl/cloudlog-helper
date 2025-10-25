using System;

namespace CloudlogHelper.Utils;

/// <summary>
///     Radio-related calculations.
///     Original Java code comes from FT8CN project, which has been rewritten here as c# code
/// </summary>
public static class FreqHelper
{
    /// <summary>
    ///     Converts the frequency in HZ to a string with MHz in the end.
    /// </summary>
    /// <param name="freq">Frequency in Hz.</param>
    /// <param name="addMHz"></param>
    /// <returns></returns>
    public static string GetFrequencyStr(long freq, bool addMHz = true)
    {
        var mhzValue = freq / 1_000_000.0;
        var mhz = mhzValue.ToString("0.00000"); // 固定5位小数，自动补零
        // var mhz = $"{freq / 1000000}.{freq % 1000000 / 10:D3}";
        if (addMHz) mhz = $"{mhz}MHz";
        return mhz;
    }

    public static string GetFrequencyStr(ulong freq, bool addMHz = true)
    {
        var mhzValue = freq / 1_000_000.0;
        var mhz = mhzValue.ToString("0.00000"); // 固定5位小数，自动补零
        // var mhz = $"{freq / 1000000}.{freq % 1000000 / 10:D3}";
        if (addMHz) mhz = $"{mhz}MHz";
        return mhz;
    }

    /// <summary>
    ///     Check if current frequency is a WSPR frequency.
    /// </summary>
    /// <param name="freq">Frequency in Hz.</param>
    /// <returns></returns>
    public static bool CheckIsWSPR2(long freq)
    {
        return freq is >= 137400 and <= 137600 //2190m
               || freq is >= 475400 and <= 475600 //630m
               || freq is >= 1838000 and <= 1838200 //160m
               || freq is >= 3594000 and <= 3594200 //80m
               || freq is >= 5288600 and <= 5288800 //60m
               || freq is >= 7040000 and <= 7040200 //40m
               || freq is >= 10140100 and <= 10140300 //30m
               || freq is >= 14097000 and <= 14097200 //20m
               || freq is >= 18106000 and <= 18106200 //17m
               || freq is >= 21096000 and <= 21096200 //15m
               || freq is >= 24926000 and <= 24926200 //12m
               || freq is >= 28126000 and <= 28126200 //10m
               || freq is >= 50294400 and <= 50294600 //6m
               || freq is >= 70092400 and <= 70092600 //4m
               || freq is >= 144489900 and <= 144490100 //2m
               || freq is >= 432301600 and <= 432301800 //70cm
               || freq is >= 1296501400 and <= 1296501600; //23cm
    }

    public static ulong GetRandomFreqFromMeter(string meterBand)
    {
        var random = new Random();

        return meterBand.ToLower() switch
        {
            "2200m" => (ulong)random.Next(135700, 137800 + 1),
            "630m" => (ulong)random.Next(472000, 479000 + 1),
            "160m" => (ulong)random.Next(1800000, 2000000 + 1),
            "80m" => (ulong)random.Next(3500000, 4000000 + 1),
            "60m" => (ulong)random.Next(5351500, 5366500 + 1),
            "40m" => (ulong)random.Next(7000000, 7300000 + 1),
            "30m" => (ulong)random.Next(10100000, 10150000 + 1),
            "20m" => (ulong)random.Next(14000000, 14350000 + 1),
            "17m" => (ulong)random.Next(18068000, 18168000 + 1),
            "15m" => (ulong)random.Next(21000000, 21450000 + 1),
            "12m" => (ulong)random.Next(24890000, 24990000 + 1),
            "10m" => (ulong)random.Next(28000000, 29700000 + 1),
            "6m" => (ulong)random.Next(50000000, 54000000 + 1),
            "2m" => (ulong)random.Next(144000000, 148000000 + 1),
            "1.25m" => (ulong)random.Next(220000000, 225000000 + 1),
            "70cm" => (ulong)random.Next(420000000, 450000000 + 1),
            "33cm" => (ulong)random.Next(902000000, 928000000 + 1),
            "23cm" => (ulong)random.Next(1240000000, 1300000000 + 1),
            _ => throw new ArgumentException($"Unknown band: {meterBand}")
        };
    }

    /// <summary>
    ///     Convert frequency to corresponding wavelength.
    /// </summary>
    /// <param name="freq">Frequency in Hz.</param>
    /// <returns></returns>
    public static string GetMeterFromFreq(long freq)
    {
        return freq switch
        {
            >= 135700 and <= 137800 => "2200m",
            >= 472000 and <= 479000 => "630m",
            >= 1800000 and <= 2000000 => "160m",
            >= 3500000 and <= 4000000 => "80m",
            >= 5351500 and <= 5366500 => "60m",
            >= 7000000 and <= 7300000 => "40m",
            >= 10100000 and <= 10150000 => "30m",
            >= 14000000 and <= 14350000 => "20m",
            >= 18068000 and <= 18168000 => "17m",
            >= 21000000 and <= 21450000 => "15m",
            >= 24890000 and <= 24990000 => "12m",
            >= 28000000 and <= 29700000 => "10m",
            >= 50000000 and <= 54000000 => "6m",
            >= 144000000 and <= 148000000 => "2m",
            >= 220000000 and <= 225000000 => "1.25m",
            >= 420000000 and <= 450000000 => "70cm",
            >= 902000000 and <= 928000000 => "33cm",
            >= 1240000000 and <= 1300000000 => "23cm",
            _ => CalculationMeterFromFreq(freq)
        };
    }


    /// <summary>
    ///     Convert frequency to corresponding wavelength.
    /// </summary>
    /// <param name="freq">Frequency in Hz.</param>
    /// <returns></returns>
    public static string GetMeterFromFreq(ulong freq)
    {
        return freq switch
        {
            >= 135700 and <= 137800 => "2200m",
            >= 472000 and <= 479000 => "630m",
            >= 1800000 and <= 2000000 => "160m",
            >= 3500000 and <= 4000000 => "80m",
            >= 5351500 and <= 5366500 => "60m",
            >= 7000000 and <= 7300000 => "40m",
            >= 10100000 and <= 10150000 => "30m",
            >= 14000000 and <= 14350000 => "20m",
            >= 18068000 and <= 18168000 => "17m",
            >= 21000000 and <= 21450000 => "15m",
            >= 24890000 and <= 24990000 => "12m",
            >= 28000000 and <= 29700000 => "10m",
            >= 50000000 and <= 54000000 => "6m",
            >= 144000000 and <= 148000000 => "2m",
            >= 220000000 and <= 225000000 => "1.25m",
            >= 420000000 and <= 450000000 => "70cm",
            >= 902000000 and <= 928000000 => "33cm",
            >= 1240000000 and <= 1300000000 => "23cm",
            _ => CalculationMeterFromFreq(freq)
        };
    }

    /// <summary>
    ///     Calculate wavelength by frequency.
    /// </summary>
    /// <param name="freq">Frequency in Hz.</param>
    /// <returns></returns>
    private static string CalculationMeterFromFreq(long freq)
    {
        if (freq == 0) return "";
        var meter = 300000000f / freq;
        return meter switch
        {
            < 1 => $"{Math.Round(meter * 10) * 10}cm",
            < 20 => $"{Math.Round(meter)}m",
            _ => $"{Math.Round(meter / 10) * 10}m"
        };
    }

    /// <summary>
    ///     Calculate wavelength by frequency.
    /// </summary>
    /// <param name="freq">Frequency in Hz.</param>
    /// <returns></returns>
    private static string CalculationMeterFromFreq(ulong freq)
    {
        if (freq == 0) return "";
        var meter = 300000000f / freq;
        return meter switch
        {
            < 1 => $"{Math.Round(meter * 10) * 10}cm",
            < 20 => $"{Math.Round(meter)}m",
            _ => $"{Math.Round(meter / 10) * 10}m"
        };
    }

    /// <summary>
    ///     Detailed freq info. e.g. 21.074MHz (10m)
    /// </summary>
    /// <param name="freq">Frequency in Hz.</param>
    /// <returns></returns>
    public static string GetFrequencyAllInfo(long freq)
    {
        return $"{GetFrequencyStr(freq)} ({GetMeterFromFreq(freq)})";
    }
}