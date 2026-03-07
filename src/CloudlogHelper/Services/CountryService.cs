using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using NLog;

namespace CloudlogHelper.Services;

public class CountryService : ICountryService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private const string LogFlagKey = "__log";
    private const string FallbackFlagUri = $"{DefaultConfigs.AvaresFlagTemplate}fallback.png";
    
    private readonly Dictionary<string, DXCCCountryInfo> _dxccCountryInfo = new();
    private readonly ConcurrentDictionary<string, Bitmap> _flagCache = new();
    private bool _disposed;

    public CountryService()
    {
        var stream = ApplicationStartUpUtil.GetSingleResourceStream(DefaultConfigs.DefaultDxccInfoFile);
        if (stream == null)
        {
            ClassLogger.Warn($"Embedded resource not found: {DefaultConfigs.DefaultDxccInfoFile}");
            return;
        }

        using var reader = new StreamReader(stream);
        var prefixCountryJson = reader.ReadToEnd();
        _dxccCountryInfo = _dxccCountryJsonParse(prefixCountryJson);
    }

    public IImage GetFlagResourceByDXCC(string? dxcc)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CountryService));

        // special
        if (dxcc == LogFlagKey)
        {
            return GetOrCreateFlagBitmap($"{DefaultConfigs.AvaresFlagTemplate}log.png");
        }

        if (string.IsNullOrWhiteSpace(dxcc))
        {
            return GetOrCreateFlagBitmap(FallbackFlagUri);
        }

        if (!_dxccCountryInfo.TryGetValue(dxcc, out var result))
        {
            return GetOrCreateFlagBitmap(FallbackFlagUri);
        }

        var resPath = $"{DefaultConfigs.AvaresFlagTemplate}{result.FlagPngName}";
        return AssetLoader.Exists(new Uri(resPath))
            ? GetOrCreateFlagBitmap(resPath)
            : GetOrCreateFlagBitmap(FallbackFlagUri);
    }

    private Bitmap GetOrCreateFlagBitmap(string resourcePath)
    {
        return _flagCache.GetOrAdd(resourcePath, static path =>
        {
            using var stream = AssetLoader.Open(new Uri(path));
            return new Bitmap(stream);
        });
    }
    
    private Dictionary<string, DXCCCountryInfo> _dxccCountryJsonParse(string json)
    {
        return JsonSerializer.Deserialize(
            json,
            SourceGenerationContext.Default.DictionaryStringDXCCCountryInfo
        ) ?? new Dictionary<string, DXCCCountryInfo>();
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var bitmap in _flagCache.Values)
        {
            bitmap.Dispose();
        }

        _flagCache.Clear();
        _disposed = true;
    }
}
