using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using NLog;
using ScottPlot.Statistics;

namespace CloudlogHelper.Services;

/// <summary>
///     Simple cache service for charts...
/// </summary>
/// <typeparam name="ChartQSOPoint"></typeparam>
public class ChartDataCacheService : IChartDataCacheService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly Subject<ChartQSOPoint> _itemAddedSubject = new();
    private readonly object _lock = new();

    private readonly Dictionary<string, double[,]> _accumulatedGridStationCount =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Histogram> _accumulatedStationBearing =
        new(StringComparer.OrdinalIgnoreCase);

    // <Band, <Dxcc, count>>
    private readonly Dictionary<string, Dictionary<string, double?>> _accumulatedStationCount =
        new(StringComparer.OrdinalIgnoreCase);

    // <Band, Histogram>
    private readonly Dictionary<string, Histogram> _accumulatedStationDistance =
        new(StringComparer.OrdinalIgnoreCase);

    // <Band, <Callsign, latest point>>
    private readonly Dictionary<string, Dictionary<string, ChartQSOPoint>> _latestStationPointByBandAndCallsign =
        new(StringComparer.OrdinalIgnoreCase);

    private ChartQSOPoint[] _buffer = new ChartQSOPoint[DefaultConfigs.DefaultChartDataCacheNumber];
    private int _count;
    private int _nextIndex;
    private bool _disposed;

    private IObservable<ChartQSOPoint> ItemAdded => _itemAddedSubject.AsObservable();

    public IObservable<ChartQSOPoint> GetItemAddedObservable()
    {
        return ItemAdded;
    }

    public void Add(ChartQSOPoint item)
    {
        if (_disposed) return;

        lock (_lock)
        {
            try
            {
                // cycle buffer
                _buffer[_nextIndex] = item;
                _nextIndex = (_nextIndex + 1) % _buffer.Length;
                _count = Math.Min(_count + 1, _buffer.Length);

                // acc count
                if (!_accumulatedStationCount.TryGetValue(item.Band, out var bandStationCount))
                {
                    bandStationCount = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
                    _accumulatedStationCount[item.Band] = bandStationCount;
                }

                if (!bandStationCount.TryGetValue(item.DXCC, out var dxccCount)) dxccCount = 0;
                bandStationCount[item.DXCC] = dxccCount + 1;

                if (!string.IsNullOrWhiteSpace(item.DxCallsign))
                {
                    if (!_latestStationPointByBandAndCallsign.TryGetValue(item.Band, out var dedupeCache))
                    {
                        dedupeCache = new Dictionary<string, ChartQSOPoint>(StringComparer.OrdinalIgnoreCase);
                        _latestStationPointByBandAndCallsign[item.Band] = dedupeCache;
                    }

                    dedupeCache[item.DxCallsign] = item;
                }

                if (item.IsAccurate)
                {
                    // acc dist
                    if (!_accumulatedStationDistance.TryGetValue(item.Band, out var distanceHistogram))
                    {
                        _accumulatedStationDistance[item.Band] = Histogram.WithBinCount(50, 0, 20000);
                        distanceHistogram = _accumulatedStationDistance[item.Band];
                    }

                    if (item.Distance >= 0) distanceHistogram.Add(item.Distance);

                    // acc bearing
                    if (!_accumulatedStationBearing.TryGetValue(item.Band, out var bearingHistogram))
                    {
                        _accumulatedStationBearing[item.Band] = Histogram.WithBinSize(22.5, 0, 360);
                        bearingHistogram = _accumulatedStationBearing[item.Band];
                    }

                    if (item.Azimuth >= 0) bearingHistogram.Add(item.Azimuth);

                    // acc grid data
                    if (!_accumulatedGridStationCount.TryGetValue(item.Band, out var grid))
                    {
                        grid = new double[DefaultConfigs.WorldHeatmapHeight, DefaultConfigs.WorldHeatmapWidth];
                        _accumulatedGridStationCount[item.Band] = grid;
                    }

                    _accumulateGridPoint(grid, item.Latitude, item.Longitude);
                }
            }
            catch (Exception e)
            {
                ClassLogger.Error(e, "Error while adding chart point.");
            }
        }

        if (_disposed) return;
        _itemAddedSubject.OnNext(item);
    }

    public void Clear()
    {
        ClearPolarBuffer();
        ClearAccuBuffer();
    }

    public void ClearPolarBuffer()
    {
        lock (_lock)
        {
            _buffer = new ChartQSOPoint[DefaultConfigs.DefaultChartDataCacheNumber];
            _nextIndex = 0;
            _count = 0;
        }
    }

    public void ClearAccuBuffer()
    {
        lock (_lock)
        {
            _accumulatedStationCount.Clear();
            _accumulatedStationBearing.Clear();
            _accumulatedStationDistance.Clear();
            _accumulatedGridStationCount.Clear();
            _latestStationPointByBandAndCallsign.Clear();
        }
    }

    public double[,]? GetGridStationCountByBand(string? band)
    {
        lock (_lock)
        {
            if (band is null) return null;
            return _accumulatedGridStationCount.GetValueOrDefault(band);
        }
    }

    public Histogram? GetDistanceHistogramByBand(string? band)
    {
        lock (_lock)
        {
            if (band is null) return null;
            return _accumulatedStationDistance.GetValueOrDefault(band);
        }
    }

    public Histogram? GetBearingHistogramByBand(string? band)
    {
        lock (_lock)
        {
            if (band is null) return null;
            return _accumulatedStationBearing.GetValueOrDefault(band);
        }
    }

    public Dictionary<string, double?>? GetStationCountByBand(string? band)
    {
        lock (_lock)
        {
            if (band is null) return null;
            return _accumulatedStationCount.GetValueOrDefault(band);
        }
    }

    public StationChartDataSnapshot GetStationChartDataSnapshotByBand(string? band, bool filterDupeByCallsign = false)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(band)) return _buildEmptyStationChartDataSnapshot();

            if (!filterDupeByCallsign)
                return _buildAccumulativeSnapshotByBand(band);

            return _buildDupeFilteredSnapshotByBand(band);
        }
    }

    public IEnumerable<ChartQSOPoint> TakeLatestN(int count, IEqualityComparer<ChartQSOPoint>? comparer = null,
        Func<ChartQSOPoint, bool>? filterCondition = null)
    {
        lock (_lock)
        {
            if (count <= 0 || _count == 0) return Array.Empty<ChartQSOPoint>();

            var result = new List<ChartQSOPoint>();
            HashSet<ChartQSOPoint>? seen = comparer is null ? null : new HashSet<ChartQSOPoint>(comparer);

            var takeCount = Math.Min(count, _count);
            var itemsTaken = 0;

            for (var i = 1; itemsTaken < takeCount && i <= _count; i++)
            {
                var index = (_nextIndex - i + _buffer.Length) % _buffer.Length;
                var item = _buffer[index];

                if (filterCondition != null && !filterCondition.Invoke(item))
                    continue;

                if (seen != null && !seen.Add(item))
                    continue;

                result.Add(item);
                itemsTaken++;
            }

            return result;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _itemAddedSubject.OnCompleted();
        _itemAddedSubject.Dispose();
    }

    private static StationChartDataSnapshot _buildEmptyStationChartDataSnapshot()
    {
        return new StationChartDataSnapshot(
            new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase),
            Histogram.WithBinCount(50, 0, 20000),
            Histogram.WithBinSize(22.5, 0, 360),
            new double[DefaultConfigs.WorldHeatmapHeight, DefaultConfigs.WorldHeatmapWidth]);
    }

    private StationChartDataSnapshot _buildAccumulativeSnapshotByBand(string band)
    {
        var stationCountSource = _accumulatedStationCount.GetValueOrDefault(band);
        var stationCount = stationCountSource is null
            ? new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, double?>(stationCountSource, StringComparer.OrdinalIgnoreCase);

        var distanceHistogram = _accumulatedStationDistance.GetValueOrDefault(band)
                                ?? Histogram.WithBinCount(50, 0, 20000);

        var bearingHistogram = _accumulatedStationBearing.GetValueOrDefault(band)
                               ?? Histogram.WithBinSize(22.5, 0, 360);

        var gridSource = _accumulatedGridStationCount.GetValueOrDefault(band);
        var grid = gridSource is null
            ? new double[DefaultConfigs.WorldHeatmapHeight, DefaultConfigs.WorldHeatmapWidth]
            : (double[,])gridSource.Clone();

        return new StationChartDataSnapshot(stationCount, distanceHistogram, bearingHistogram, grid);
    }

    private StationChartDataSnapshot _buildDupeFilteredSnapshotByBand(string band)
    {
        var stationCount = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        var distanceHistogram = Histogram.WithBinCount(50, 0, 20000);
        var bearingHistogram = Histogram.WithBinSize(22.5, 0, 360);
        var grid = new double[DefaultConfigs.WorldHeatmapHeight, DefaultConfigs.WorldHeatmapWidth];

        if (!_latestStationPointByBandAndCallsign.TryGetValue(band, out var callsignPoints))
            return new StationChartDataSnapshot(stationCount, distanceHistogram, bearingHistogram, grid);

        foreach (var point in callsignPoints.Values)
        {
            if (!stationCount.TryGetValue(point.DXCC, out var currCount)) currCount = 0;
            stationCount[point.DXCC] = currCount + 1;

            if (!point.IsAccurate) continue;
            if (point.Distance >= 0) distanceHistogram.Add(point.Distance);
            if (point.Azimuth >= 0) bearingHistogram.Add(point.Azimuth);
            _accumulateGridPoint(grid, point.Latitude, point.Longitude);
        }

        return new StationChartDataSnapshot(stationCount, distanceHistogram, bearingHistogram, grid);
    }

    private static void _accumulateGridPoint(double[,] grid, double latitude, double longitude)
    {
        var xIndex = (int)Math.Round((longitude + 180) / 360 * (DefaultConfigs.WorldHeatmapWidth - 1));
        var yIndex = (int)Math.Round((latitude + 90) / 180 * (DefaultConfigs.WorldHeatmapHeight - 1));

        xIndex = Math.Clamp(xIndex, 0, DefaultConfigs.WorldHeatmapWidth - 1);
        yIndex = Math.Clamp(yIndex, 0, DefaultConfigs.WorldHeatmapHeight - 1);

        grid[yIndex, xIndex] += 1;
    }
}
