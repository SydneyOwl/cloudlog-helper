using System;
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
    
    private readonly Dictionary<string, DXCCCountryInfo> _dxccCountryInfo = new();

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
        // special 
        if (dxcc == "__log")
        {
            return new Bitmap(AssetLoader.Open(new Uri($"{DefaultConfigs.AvaresFlagTemplate}log.png")));
        }
        
        var fallback = $"{DefaultConfigs.AvaresFlagTemplate}fallback.png";

        if (string.IsNullOrWhiteSpace(dxcc)) return new Bitmap(AssetLoader.Open(new Uri(fallback)));
        
        if (!_dxccCountryInfo.TryGetValue(dxcc, out var result))
        {
            return new Bitmap(AssetLoader.Open(new Uri(fallback)));
        }

        var resPath = $"{DefaultConfigs.AvaresFlagTemplate}{result.FlagPngName}";
        if (AssetLoader.Exists(new Uri(resPath)))
        {
            return new Bitmap(AssetLoader.Open(new Uri(resPath)));
        }

        return new Bitmap(AssetLoader.Open(new Uri(fallback)));
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
        // TODO release managed resources here
    }
}