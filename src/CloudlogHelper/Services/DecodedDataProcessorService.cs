using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Database;
using CloudlogHelper.Models;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using NLog;
using ReactiveUI;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Services;

internal enum CallsignLocationSource
{
    CountryFallback = 0,
    MessageGrid = 1,
    DatabaseGrid = 2
}

internal readonly struct ParsedDecodeData
{
    public ParsedDecodeData(Decode decode, string clientKey, string? callsign, string? gridSquare)
    {
        Decode = decode;
        ClientKey = clientKey;
        Callsign = callsign;
        GridSquare = gridSquare;
    }

    public Decode Decode { get; }

    /// <summary>
    ///     Client key derived at enqueue time, identical to the registry key and the tab key.
    /// </summary>
    public string ClientKey { get; }
    public string? Callsign { get; }
    public string? GridSquare { get; }
}

internal struct CallsignDataCache
{
    public double Bearing { get; set; }
    public double Distance { get; set; }
    public double Latitude { get; set; }

    public double Longitude { get; set; }
    public string Dxcc { get; set; }
    public string GridSquare { get; set; }
    public CallsignLocationSource Source { get; set; }
    public bool DatabaseGridLookupMissed { get; set; }

    // Accurate means the location comes from a callsign-level grid (message/database),
    // not from a country centroid fallback.
    public bool IsAccurate => Source != CallsignLocationSource.CountryFallback;
}

public class DecodedDataProcessorService : IDecodedDataProcessorService, IDisposable
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly BasicSettings _basicSettings;

    // callsign -> azimuth/distance/location cache
    private readonly Dictionary<string, CallsignDataCache> _callsignDistanceAndBearing =
        new(StringComparer.OrdinalIgnoreCase);

    // callsign -> country detail cache to avoid repeated DB lookups
    private readonly Dictionary<string, CountryDatabase> _callsignCountryCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, string> _currentClientBand =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IDatabaseService _databaseService;

    private readonly IChartDataCacheService _dataCacheService;
    private readonly ConcurrentQueue<(Decode Decode, string ClientKey)> _decodedQueue = new();
    private readonly Subject<Unit> _decodedSignal = new();
    private readonly CompositeDisposable _compDisp = new ();
    private readonly SemaphoreSlim _batchProcessingLock = new(1, 1);

    private string _myGrid = string.Empty;
    private bool _disposed;

    public DecodedDataProcessorService(IDatabaseService databaseService,
        IApplicationSettingsService applicationSettingsService,
        IChartDataCacheService chartDataCacheService)
    {
        _basicSettings = applicationSettingsService.GetCurrentSettings().BasicSettings;
        _databaseService = databaseService;
        _dataCacheService = chartDataCacheService;

        if (MaidenheadGridUtil.CheckMaidenhead(_basicSettings.MyMaidenheadGrid))
            _myGrid = _basicSettings.MyMaidenheadGrid!;

        _decodedSignal
            .Throttle(TimeSpan.FromSeconds(3))
            .ObserveOn(RxSchedulers.TaskpoolScheduler)
            .Subscribe(r => _ = _processDecodedBatchAsync(),
                ex => ClassLogger.Error(ex, "Error while scheduling decoded batch processing."))
            .DisposeWith(_compDisp);
    }

    public void ProcessDecoded(Decode decode, EndPoint? source)
    {
        if (_disposed || _basicSettings.DisableAllCharts) return;
        _decodedQueue.Enqueue((decode, WsjtxMessageUtil.GetClientKey(decode.Id, source)));
        _decodedSignal.OnNext(Unit.Default);
    }

    public void UpdateClientBand(string clientId, string band, EndPoint? source)
    {
        if (_disposed) return;

        // Guard the derived key, not the raw id: a blank id plus an endpoint is a legitimate
        // client, and rejecting it here would leave that client's band empty on the chart.
        var key = WsjtxMessageUtil.GetClientKey(clientId, source);
        if (string.IsNullOrWhiteSpace(key)) return;
        _currentClientBand[key] = band;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _compDisp.Dispose();
        _decodedSignal.OnCompleted();
        _decodedSignal.Dispose();
        _batchProcessingLock.Dispose();
    }

    private async Task _processDecodedBatchAsync()
    {
        if (_disposed) return;
        
        try
        {
            if (_disposed) return;
            
            await _batchProcessingLock.WaitAsync();

            var pendingDecodes = _drainDecodedQueue();
            if (pendingDecodes.Count == 0) return;

            _refreshGridAndClearCacheIfNeeded();
            if (string.IsNullOrWhiteSpace(_myGrid)) return;

            var parsedDecodes = _parseDecodes(pendingDecodes);
            await _saveCallsignGridInfo(parsedDecodes).ConfigureAwait(false);
            await _cacheChartData(parsedDecodes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Error while processing decoded data...");
        }
        finally
        {
            if (!_disposed)
                _batchProcessingLock.Release();
        }
    }

    private List<(Decode Decode, string ClientKey)> _drainDecodedQueue()
    {
        var decodes = new List<(Decode, string)>();
        while (_decodedQueue.TryDequeue(out var decode))
            decodes.Add(decode);

        return decodes;
    }

    private void _refreshGridAndClearCacheIfNeeded()
    {
        var currentGrid = _basicSettings.MyMaidenheadGrid ?? string.Empty;

        if (string.Equals(_myGrid, currentGrid, StringComparison.OrdinalIgnoreCase))
            return;

        ClassLogger.Debug("Grid differs. Cleaning all data..");
        _callsignDistanceAndBearing.Clear();
        _dataCacheService.Clear();
        _myGrid = string.Empty;

        if (MaidenheadGridUtil.CheckMaidenhead(currentGrid))
            _myGrid = currentGrid;
    }

    private static List<ParsedDecodeData> _parseDecodes(
        IEnumerable<(Decode Decode, string ClientKey)> decodes)
    {
        var parsed = new List<ParsedDecodeData>();
        foreach (var decodedData in decodes)
        {
            var message = decodedData.Decode.Message ?? string.Empty;
            var callsign = WsjtxMessageUtil.ExtractDeFromMessage(message);
            var grid = WsjtxMessageUtil.ExtractGridFromMessage(message);
            parsed.Add(new ParsedDecodeData(decodedData.Decode, decodedData.ClientKey, callsign, grid));
        }

        return parsed;
    }

    private async Task _cacheChartData(IReadOnlyList<ParsedDecodeData> parsedDecodes)
    {
        if (string.IsNullOrWhiteSpace(_myGrid)) return;

        foreach (var decodedData in parsedDecodes)
        {
            if (decodedData.Callsign is null) continue;

            var callsign = decodedData.Callsign;
            var chartQsoPoint = _buildBaseChartQsoPoint(decodedData, callsign);
            if (_tryApplyCachedCallsignData(callsign, decodedData.GridSquare, ref chartQsoPoint))
            {
                _dataCacheService.Add(chartQsoPoint);
                continue;
            }

            var resolvedCallsignData = await _resolveCallsignDataAsync(callsign, decodedData.GridSquare)
                .ConfigureAwait(false);

            if (!resolvedCallsignData.HasValue) continue;

            var callsignData = resolvedCallsignData.Value;
            _callsignDistanceAndBearing[callsign] = callsignData;
            _applyCallsignDataToChartPoint(ref chartQsoPoint, callsignData);
            _dataCacheService.Add(chartQsoPoint);
        }
    }

    private ChartQSOPoint _buildBaseChartQsoPoint(in ParsedDecodeData decodedData, string callsign)
    {
        // The registry key, not the raw Id: a blank Id is keyed by source address, and this is
        // what the chart filter compares against the selected tab.
        var clientId = decodedData.ClientKey;
        var band = _currentClientBand.TryGetValue(clientId, out var cachedBand)
            ? cachedBand ?? string.Empty
            : string.Empty;

        return new ChartQSOPoint
        {
            DxCallsign = callsign,
            Band = band,
            Client = clientId,
            Mode = decodedData.Decode.DecodeModeNotationsToString(),
            Snr = decodedData.Decode.Snr
        };
    }

    private bool _tryApplyCachedCallsignData(string callsign, string? incomingGrid, ref ChartQSOPoint chartQsoPoint)
    {
        if (!_callsignDistanceAndBearing.TryGetValue(callsign, out var cachedData))
            return false;

        if (!_canReuseCachedResult(cachedData, incomingGrid))
            return false;

        ClassLogger.Trace($"Cache hits - {callsign}");
        _applyCallsignDataToChartPoint(ref chartQsoPoint, cachedData);
        return true;
    }

    private static bool _canReuseCachedResult(CallsignDataCache cachedData, string? incomingGrid)
    {
        if (incomingGrid is null)
        {
            if (cachedData.IsAccurate) return true;
            return cachedData.DatabaseGridLookupMissed;
        }

        if (!cachedData.IsAccurate) return false;
        return string.Equals(cachedData.GridSquare, incomingGrid, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<CallsignDataCache?> _resolveCallsignDataAsync(string callsign, string? incomingGrid)
    {
        if (incomingGrid is not null)
        {
            ClassLogger.Trace($"Calculating {callsign} from grid...");
            var dxcc = await _resolveDxccAsync(callsign).ConfigureAwait(false);
            return _buildCallsignDataFromGrid(
                incomingGrid,
                dxcc,
                CallsignLocationSource.MessageGrid,
                databaseGridLookupMissed: false);
        }

        var gridByCallsign = await _tryGetDatabaseGridByCallsignAsync(callsign).ConfigureAwait(false);
        if (gridByCallsign is not null)
        {
            ClassLogger.Trace($"Calculating {callsign} from grid - database cache...");
            var dxcc = await _resolveDxccAsync(callsign).ConfigureAwait(false);
            return _buildCallsignDataFromGrid(
                gridByCallsign,
                dxcc,
                CallsignLocationSource.DatabaseGrid,
                databaseGridLookupMissed: false);
        }

        var countryDetail = await _getCountryDetailAsync(callsign).ConfigureAwait(false);
        if (countryDetail.CountryName == "Unknown" || string.IsNullOrWhiteSpace(countryDetail.CountryName)) return null;

        var fallbackGrid = MaidenheadGridUtil.GetGridSquare(
            new LatLng(countryDetail.Latitude, countryDetail.Longitude));

        ClassLogger.Trace($"Calculating {callsign} from default country: " +
                          $"lat {countryDetail.Latitude} lon {countryDetail.Longitude}");

        return _buildCallsignDataFromGrid(
            fallbackGrid,
            countryDetail.Dxcc,
            CallsignLocationSource.CountryFallback,
            databaseGridLookupMissed: true);
    }

    private async Task<string?> _tryGetDatabaseGridByCallsignAsync(string callsign)
    {
        var gridByCallsign = await _databaseService.GetGridByCallsign(callsign).ConfigureAwait(false);
        return MaidenheadGridUtil.CheckMaidenhead(gridByCallsign) ? gridByCallsign : null;
    }

    private CallsignDataCache? _buildCallsignDataFromGrid(
        string grid,
        string dxcc,
        CallsignLocationSource source,
        bool databaseGridLookupMissed)
    {
        if (!MaidenheadGridUtil.CheckMaidenhead(_myGrid)
            || !MaidenheadGridUtil.CheckMaidenhead(grid))
            return null;

        var normalizedGrid = grid.ToUpperInvariant();
        var bearing = MaidenheadGridUtil.CalculateBearing(_myGrid, normalizedGrid);
        var distance = MaidenheadGridUtil.GetDist(_myGrid, normalizedGrid);
        var gridToLatLng = MaidenheadGridUtil.GridToLatLng(normalizedGrid);

        return new CallsignDataCache
        {
            Bearing = bearing,
            Distance = distance,
            Latitude = gridToLatLng?.Latitude ?? 0,
            Longitude = gridToLatLng?.Longitude ?? 0,
            Dxcc = dxcc ?? string.Empty,
            GridSquare = normalizedGrid,
            Source = source,
            DatabaseGridLookupMissed = databaseGridLookupMissed
        };
    }

    private async Task<string> _resolveDxccAsync(string callsign)
    {
        if (_callsignDistanceAndBearing.TryGetValue(callsign, out var cachedData)
            && !string.IsNullOrWhiteSpace(cachedData.Dxcc))
            return cachedData.Dxcc;

        var countryDetail = await _getCountryDetailAsync(callsign).ConfigureAwait(false);
        return countryDetail.Dxcc;
    }

    private async Task<CountryDatabase> _getCountryDetailAsync(string callsign)
    {
        if (_callsignCountryCache.TryGetValue(callsign, out var cachedDetail))
            return cachedDetail;

        var detail = await _databaseService.GetCallsignDetailAsync(callsign).ConfigureAwait(false);
        _callsignCountryCache[callsign] = detail;
        return detail;
    }

    private static void _applyCallsignDataToChartPoint(ref ChartQSOPoint chartQsoPoint, in CallsignDataCache callsignData)
    {
        chartQsoPoint.Azimuth = callsignData.Bearing;
        chartQsoPoint.Distance = callsignData.Distance;
        chartQsoPoint.DXCC = callsignData.Dxcc;
        chartQsoPoint.Latitude = callsignData.Latitude;
        chartQsoPoint.Longitude = callsignData.Longitude;
        chartQsoPoint.IsAccurate = callsignData.IsAccurate;
    }

    private async Task _saveCallsignGridInfo(IReadOnlyList<ParsedDecodeData> parsedDecodes)
    {
        if (parsedDecodes.Count == 0) return;

        var latestGridByCallsign = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var decodedData in parsedDecodes)
        {
            if (decodedData.Callsign is null || decodedData.GridSquare is null) continue;
            latestGridByCallsign[decodedData.Callsign] = decodedData.GridSquare;
        }

        if (latestGridByCallsign.Count == 0) return;

        var collectedGrid = new List<CollectedGridDatabase>(latestGridByCallsign.Count);
        foreach (var kv in latestGridByCallsign)
            collectedGrid.Add(new CollectedGridDatabase
            {
                Callsign = kv.Key,
                GridSquare = kv.Value
            });

        await _databaseService.BatchAddOrUpdateCallsignGridAsync(collectedGrid).ConfigureAwait(false);
        ClassLogger.Trace($"Added {collectedGrid.Count} grids.");
    }
}
