using System;
using System.Collections.Concurrent;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CloudlogHelper.Resources;

namespace CloudlogHelper.Utils;

public static class FlagImageUtil
{
    private static readonly ConcurrentDictionary<string, Bitmap> BitmapCache = new();
    private const string FallbackFlag = "fallback.png";
    private const string LogFlag = "log.png";

    public static IImage GetFlagImage(string? flagImg)
    {
        var fileName = string.IsNullOrWhiteSpace(flagImg) ? FallbackFlag : flagImg;
        return GetOrCreateBitmap(fileName);
    }

    public static IImage GetLogFlagImage()
    {
        return GetOrCreateBitmap(LogFlag);
    }

    private static Bitmap GetOrCreateBitmap(string fileName)
    {
        var resourcePath = $"{DefaultConfigs.AvaresFlagTemplate}{fileName}";
        if (!AssetLoader.Exists(new Uri(resourcePath)))
        {
            resourcePath = $"{DefaultConfigs.AvaresFlagTemplate}{FallbackFlag}";
        }

        return BitmapCache.GetOrAdd(resourcePath, static path =>
        {
            using var stream = AssetLoader.Open(new Uri(path));
            return new Bitmap(stream);
        });
    }
}
