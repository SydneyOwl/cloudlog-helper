using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using CloudlogHelper.Database;
using CloudlogHelper.Models;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using DynamicData.Binding;
using NLog;
using ReactiveUI;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Services;

internal struct CallsignDataCache
{
    public double Bearing { get; set; }
    public double Distance { get; set; }
    public double Latitude { get; set; }

    public double Longitude { get; set; }
    public string Dxcc { get; set; }
    public bool IsAccurate { get; set; }
}

public class DecodedDataProcessorService : IDecodedDataProcessorService, IDisposable
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly BasicSettings _basicSettings;

    // az, dist, isAccu
    private readonly Dictionary<string, CallsignDataCache> _callsignDistanceAndBearing = new();

    private readonly Dictionary<string, string>　_currentClientBand = new();

    private readonly IDatabaseService _databaseService;

    private readonly IChartDataCacheService _dataCacheService;

    private readonly ObservableCollection<Decode> _decodedCache = new();

    private string _myGrid;

    public DecodedDataProcessorService(IDatabaseService databaseService,
        IApplicationSettingsService applicationSettingsService,
        IChartDataCacheService chartDataCacheService)
    {
        _basicSettings = applicationSettingsService.GetCurrentSettings().BasicSettings;
        _databaseService = databaseService;
        _dataCacheService = chartDataCacheService;
        _decodedCache.ObserveCollectionChanges()
            .Throttle(TimeSpan.FromSeconds(3))
            .ObserveOn(RxApp.TaskpoolScheduler)
            .Subscribe(async void (a) =>
            {
                try
                {
                    var decodes = _decodedCache.ToArray();
                    if (decodes.Length == 0) return;
                    _decodedCache.Clear();
                    // refresh all data if my grid changed
                    if (_myGrid != _basicSettings.MyMaidenheadGrid)
                    {
                        ClassLogger.Debug("Callsign differs. Cleaning all data..");
                        _callsignDistanceAndBearing.Clear();
                        _dataCacheService.Clear();
                        _myGrid = string.Empty;
                        if (MaidenheadGridUtil.CheckMaidenhead(_basicSettings.MyMaidenheadGrid))
                            _myGrid = _basicSettings.MyMaidenheadGrid!;
                    }

                    await _saveCallsignGridInfo(decodes);
                    await _cacheChartData(decodes);
                }
                catch (Exception ex)
                {
                    ClassLogger.Error(ex, "Error while processing decoded data...");
                }
            });
    }

    public void ProcessDecoded(Decode decode)
    {
        if (_basicSettings.DisableAllCharts) return;
        _decodedCache.Add(decode);
    }

    public void UpdateClientBand(string clientId, string band)
    {
        _currentClientBand[clientId] = band;
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }

    private async Task _cacheChartData(Decode[] decode)
    {
        if (string.IsNullOrWhiteSpace(_myGrid)) return;
        foreach (var tmp in decode)
        {
            var chartQsoPoint = new ChartQSOPoint();
            var callsign = WsjtxMessageUtil.ExtractDeFromMessage(tmp.Message);
            var grid = WsjtxMessageUtil.ExtractGridFromMessage(tmp.Message);

            if (callsign is null) continue;

            chartQsoPoint.DxCallsign = callsign;
            chartQsoPoint.Band = _currentClientBand.GetValueOrDefault(tmp.Id, string.Empty);
            chartQsoPoint.Client = tmp.Id;
            // there's some bug here; for example ft4 messages will be recognized as q65 here;
            // so we just simply cancel filter of modes...
            chartQsoPoint.Mode = tmp.DecodeModeNotationsToString();
            chartQsoPoint.Snr = tmp.Snr;

            // cache hits!
            if (_callsignDistanceAndBearing.TryGetValue(callsign, out var value))
            {
                if (value.IsAccurate || grid is null)
                {
                    ClassLogger.Debug($"Cache hits - {callsign}");
                    chartQsoPoint.Azimuth = value.Bearing;
                    chartQsoPoint.Distance = value.Distance;
                    chartQsoPoint.DXCC = value.Dxcc;
                    chartQsoPoint.Latitude = value!.Latitude;
                    chartQsoPoint.Longitude = value!.Longitude;
                    chartQsoPoint.IsAccurate = value.IsAccurate;
                    _dataCacheService.Add(chartQsoPoint);
                    continue;
                }

                ClassLogger.Debug($"{callsign} Cache hits but we could get a more accurate result by recalc grid...");
            }

            string iGrid;
            var isAccurate = false;
            var countryDetail = await _databaseService.GetCallsignDetailAsync(callsign);

            // calc from grid
            if (grid is not null)
            {
                iGrid = grid;
                isAccurate = true;
                ClassLogger.Trace($"Calculating {callsign} from grid...");
            }
            else
            {
                // fallback option 01: find grid from database
                var gridByCallsign = await _databaseService.GetGridByCallsign(callsign);
                if (gridByCallsign is not null)
                {
                    iGrid = gridByCallsign;
                    isAccurate = false;
                    ClassLogger.Trace($"Calculating {callsign} from grid - database cache...");
                }
                else
                {
                    // fallback option 02: find target country and use that gridcountryDetail
                    if (countryDetail.CountryNameEn == "Unknown") continue;
                    iGrid = MaidenheadGridUtil.GetGridSquare(
                        new LatLng(countryDetail.Latitude, countryDetail.Longitude));
                    ClassLogger.Debug($"Calculating {callsign} from default country: " +
                                      $"lat {countryDetail.Latitude} lon {countryDetail.Longitude}");
                }
            }

            if (!MaidenheadGridUtil.CheckMaidenhead(_myGrid)
                || !MaidenheadGridUtil.CheckMaidenhead(iGrid)) continue;

            var bearing = MaidenheadGridUtil.CalculateBearing(_myGrid, iGrid);
            var distance = MaidenheadGridUtil.GetDist(_myGrid, iGrid);
            var gridToLatLng = MaidenheadGridUtil.GridToLatLng(iGrid);
            chartQsoPoint.Azimuth = bearing;
            chartQsoPoint.Distance = distance;
            chartQsoPoint.DXCC = countryDetail.Dxcc;
            chartQsoPoint.Latitude = gridToLatLng?.Latitude ?? 0;
            chartQsoPoint.Longitude = gridToLatLng?.Longitude ?? 0;
            chartQsoPoint.IsAccurate = isAccurate;

            _callsignDistanceAndBearing[callsign] = new CallsignDataCache
            {
                Bearing = bearing,
                Distance = distance,
                Latitude = chartQsoPoint.Latitude,
                Longitude = chartQsoPoint.Longitude,
                Dxcc = countryDetail.Dxcc,
                IsAccurate = isAccurate
            };

            _dataCacheService.Add(chartQsoPoint);
        }
    }

    private async Task _saveCallsignGridInfo(Decode[] decodes)
    {
        var collectedGrid = new List<CollectedGridDatabase>();
        foreach (var decMsg in decodes)
        {
            var call = WsjtxMessageUtil.ExtractDeFromMessage(decMsg.Message);
            var grid = WsjtxMessageUtil.ExtractGridFromMessage(decMsg.Message);
            if (call is not null && grid is not null)
                collectedGrid.Add(new CollectedGridDatabase
                {
                    Callsign = call,
                    GridSquare = grid
                });
        }

        await _databaseService.BatchAddOrUpdateCallsignGridAsync(collectedGrid);
        ClassLogger.Info($"Added {collectedGrid.Count} grids.");
    }
}