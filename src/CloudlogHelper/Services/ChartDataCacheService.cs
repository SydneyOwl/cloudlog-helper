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

    // heatmap
    private readonly Dictionary<string, double[,]> _accumulatedGridStationCount =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double[,]> _dedupGridStationCount =
        new(StringComparer.OrdinalIgnoreCase);

    // azimuth
    private readonly Dictionary<string, Histogram> _accumulatedStationBearing =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Histogram> _dedupStationBearing =
        new(StringComparer.OrdinalIgnoreCase);

    // <Band, <Dxcc, count>> DXCC
    private readonly Dictionary<string, Dictionary<string, double?>> _accumulatedStationCount =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, double?>> _dedupStationCount =
        new(StringComparer.OrdinalIgnoreCase);

    // <Band, Histogram> distance
    private readonly Dictionary<string, Histogram> _accumulatedStationDistance =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Histogram> _dedupStationDistance =
        new(StringComparer.OrdinalIgnoreCase);

    // <Band, <Callsign, latest point>>
    private readonly Dictionary<string, Dictionary<string, ChartQSOPoint>> _latestStationPointByBandAndCallsign =
        new(StringComparer.OrdinalIgnoreCase);

    // polar
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

                    if (dedupeCache.TryGetValue(item.DxCallsign, out var oldPoint))
                        _applyPointToDedupAccumulators(oldPoint, -1);

                    dedupeCache[item.DxCallsign] = item;
                    _applyPointToDedupAccumulators(item, 1);
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
            _dedupStationCount.Clear();
            _dedupStationBearing.Clear();
            _dedupStationDistance.Clear();
            _dedupGridStationCount.Clear();
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
        distanceHistogram = _cloneDistanceHistogram(distanceHistogram);

        var bearingHistogram = _accumulatedStationBearing.GetValueOrDefault(band)
                               ?? Histogram.WithBinSize(22.5, 0, 360);
        bearingHistogram = _cloneBearingHistogram(bearingHistogram);

        var gridSource = _accumulatedGridStationCount.GetValueOrDefault(band);
        var grid = gridSource is null
            ? new double[DefaultConfigs.WorldHeatmapHeight, DefaultConfigs.WorldHeatmapWidth]
            : (double[,])gridSource.Clone();

        return new StationChartDataSnapshot(stationCount, distanceHistogram, bearingHistogram, grid);
    }

    private StationChartDataSnapshot _buildDupeFilteredSnapshotByBand(string band)
    {
        var stationCountSource = _dedupStationCount.GetValueOrDefault(band);
        var stationCount = stationCountSource is null
            ? new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, double?>(stationCountSource, StringComparer.OrdinalIgnoreCase);

        var distanceSource = _dedupStationDistance.GetValueOrDefault(band)
                             ?? Histogram.WithBinCount(50, 0, 20000);
        var distanceHistogram = _cloneDistanceHistogram(distanceSource);

        var bearingSource = _dedupStationBearing.GetValueOrDefault(band)
                            ?? Histogram.WithBinSize(22.5, 0, 360);
        var bearingHistogram = _cloneBearingHistogram(bearingSource);

        var gridSource = _dedupGridStationCount.GetValueOrDefault(band);
        var grid = gridSource is null
            ? new double[DefaultConfigs.WorldHeatmapHeight, DefaultConfigs.WorldHeatmapWidth]
            : (double[,])gridSource.Clone();

        return new StationChartDataSnapshot(stationCount, distanceHistogram, bearingHistogram, grid);
    }

    private void _applyPointToDedupAccumulators(ChartQSOPoint point, int delta)
    {
        if (delta == 0) return;

        var stationCountByBand = _getOrCreateDedupStationCount(point.Band);
        _applyStationCountDelta(stationCountByBand, point.DXCC, delta);

        if (!point.IsAccurate) return;

        var distanceHistogram = _getOrCreateDedupDistanceHistogram(point.Band);
        _applyHistogramDelta(distanceHistogram, point.Distance, delta, 0, 20000);

        var bearingHistogram = _getOrCreateDedupBearingHistogram(point.Band);
        _applyHistogramDelta(bearingHistogram, point.Azimuth, delta, 0, 360);

        var grid = _getOrCreateDedupGrid(point.Band);
        _applyGridDelta(grid, point.Latitude, point.Longitude, delta);
    }

    private static void _applyStationCountDelta(Dictionary<string, double?> stationCountByDxcc, string dxcc, int delta)
    {
        if (!stationCountByDxcc.TryGetValue(dxcc, out var rawCount)) rawCount = 0;
        var nextValue = (rawCount ?? 0) + delta;

        if (nextValue <= 0)
        {
            stationCountByDxcc.Remove(dxcc);
            return;
        }

        stationCountByDxcc[dxcc] = nextValue;
    }

    private static void _applyHistogramDelta(Histogram histogram, double value, int delta, double min, double max)
    {
        if (value < min || value > max) return;

        var counts = histogram.Counts;
        if (counts.Length == 0) return;

        var binSize = (max - min) / counts.Length;
        if (binSize <= 0) return;

        var idx = (int)Math.Floor((value - min) / binSize);
        if (idx >= counts.Length) idx = counts.Length - 1;
        if (idx < 0) return;

        var nextValue = counts[idx] + delta;
        counts[idx] = nextValue < 0 ? 0 : nextValue;
    }

    private static void _applyGridDelta(double[,] grid, double latitude, double longitude, int delta)
    {
        var xIndex = (int)Math.Round((longitude + 180) / 360 * (DefaultConfigs.WorldHeatmapWidth - 1));
        var yIndex = (int)Math.Round((latitude + 90) / 180 * (DefaultConfigs.WorldHeatmapHeight - 1));

        xIndex = Math.Clamp(xIndex, 0, DefaultConfigs.WorldHeatmapWidth - 1);
        yIndex = Math.Clamp(yIndex, 0, DefaultConfigs.WorldHeatmapHeight - 1);

        var nextValue = grid[yIndex, xIndex] + delta;
        grid[yIndex, xIndex] = nextValue < 0 ? 0 : nextValue;
    }

    private Dictionary<string, double?> _getOrCreateDedupStationCount(string band)
    {
        if (_dedupStationCount.TryGetValue(band, out var stationCount)) return stationCount;
        stationCount = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        _dedupStationCount[band] = stationCount;
        return stationCount;
    }

    private Histogram _getOrCreateDedupDistanceHistogram(string band)
    {
        if (_dedupStationDistance.TryGetValue(band, out var histogram)) return histogram;
        histogram = Histogram.WithBinCount(50, 0, 20000);
        _dedupStationDistance[band] = histogram;
        return histogram;
    }

    private Histogram _getOrCreateDedupBearingHistogram(string band)
    {
        if (_dedupStationBearing.TryGetValue(band, out var histogram)) return histogram;
        histogram = Histogram.WithBinSize(22.5, 0, 360);
        _dedupStationBearing[band] = histogram;
        return histogram;
    }

    private double[,] _getOrCreateDedupGrid(string band)
    {
        if (_dedupGridStationCount.TryGetValue(band, out var grid)) return grid;
        grid = new double[DefaultConfigs.WorldHeatmapHeight, DefaultConfigs.WorldHeatmapWidth];
        _dedupGridStationCount[band] = grid;
        return grid;
    }

    private static Histogram _cloneDistanceHistogram(Histogram source)
    {
        var histogram = Histogram.WithBinCount(50, 0, 20000);
        _copyHistogramCounts(source, histogram);
        return histogram;
    }

    private static Histogram _cloneBearingHistogram(Histogram source)
    {
        var histogram = Histogram.WithBinSize(22.5, 0, 360);
        _copyHistogramCounts(source, histogram);
        return histogram;
    }

    private static void _copyHistogramCounts(Histogram source, Histogram target)
    {
        var sourceCounts = source.Counts;
        var targetCounts = target.Counts;
        var len = Math.Min(sourceCounts.Length, targetCounts.Length);
        Array.Copy(sourceCounts, targetCounts, len);
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
