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


internal class BandChartAccumulators
{
    public Dictionary<string, double?> StationCountByDxcc { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Histogram? DistanceHistogram { get; set; }
    public Histogram? BearingHistogram { get; set; }
    public double[,]? GridStationCount { get; set; }
}

/// <summary>
///     Simple cache service for charts...
/// </summary>
/// <typeparam name="ChartQSOPoint"></typeparam>
public class ChartDataCacheService : IChartDataCacheService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private const int DistanceHistogramBinCount = 50;
    private const double DistanceHistogramMin = 0;
    private const double DistanceHistogramMax = 20000;
    private const double BearingHistogramBinSize = 22.5;
    private const double BearingHistogramMin = 0;
    private const double BearingHistogramMax = 360;

    private readonly Subject<ChartQSOPoint> _itemAddedSubject = new();
    private readonly object _lock = new();

    // band aggregates for statistics chart
    private readonly Dictionary<string, BandChartAccumulators> _accumulatedChartDataByBand =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BandChartAccumulators> _dedupChartDataByBand =
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
                _appendToPolarBuffer(item);
                _applyPointToBandAccumulators(_accumulatedChartDataByBand, item, 1);

                if (!string.IsNullOrWhiteSpace(item.DxCallsign))
                {
                    var dedupeCache = _getOrCreateLatestPointByCallsign(item.Band);

                    if (dedupeCache.TryGetValue(item.DxCallsign, out var oldPoint))
                        _applyPointToBandAccumulators(_dedupChartDataByBand, oldPoint, -1);

                    dedupeCache[item.DxCallsign] = item;
                    _applyPointToBandAccumulators(_dedupChartDataByBand, item, 1);
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
            _accumulatedChartDataByBand.Clear();
            _latestStationPointByBandAndCallsign.Clear();
            _dedupChartDataByBand.Clear();
        }
    }

    public double[,]? GetGridStationCountByBand(string? band)
    {
        lock (_lock)
        {
            if (band is null) return null;
            return _accumulatedChartDataByBand.GetValueOrDefault(band)?.GridStationCount;
        }
    }

    public Histogram? GetDistanceHistogramByBand(string? band)
    {
        lock (_lock)
        {
            if (band is null) return null;
            return _accumulatedChartDataByBand.GetValueOrDefault(band)?.DistanceHistogram;
        }
    }

    public Histogram? GetBearingHistogramByBand(string? band)
    {
        lock (_lock)
        {
            if (band is null) return null;
            return _accumulatedChartDataByBand.GetValueOrDefault(band)?.BearingHistogram;
        }
    }

    public Dictionary<string, double?>? GetStationCountByBand(string? band)
    {
        lock (_lock)
        {
            if (band is null) return null;
            return _accumulatedChartDataByBand.GetValueOrDefault(band)?.StationCountByDxcc;
        }
    }

    public StationChartDataSnapshot GetStationChartDataSnapshotByBand(string? band, bool filterDupeByCallsign = false)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(band)) return _buildEmptyStationChartDataSnapshot();

            return _buildSnapshotByBand(
                filterDupeByCallsign ? _dedupChartDataByBand : _accumulatedChartDataByBand,
                band);
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
            _createDistanceHistogram(),
            _createBearingHistogram(),
            new double[DefaultConfigs.WorldHeatmapHeight, DefaultConfigs.WorldHeatmapWidth]);
    }

    private StationChartDataSnapshot _buildSnapshotByBand(Dictionary<string, BandChartAccumulators> source, string band)
    {
        var bandData = source.GetValueOrDefault(band);
        if (bandData is null) return _buildEmptyStationChartDataSnapshot();

        var stationCountSource = bandData.StationCountByDxcc;
        var stationCount = stationCountSource is null
            ? new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, double?>(stationCountSource, StringComparer.OrdinalIgnoreCase);

        var distanceSource = bandData.DistanceHistogram ?? _createDistanceHistogram();
        var distanceHistogram = _cloneDistanceHistogram(distanceSource);

        var bearingSource = bandData.BearingHistogram ?? _createBearingHistogram();
        var bearingHistogram = _cloneBearingHistogram(bearingSource);

        var gridSource = bandData.GridStationCount;
        var grid = gridSource is null
            ? new double[DefaultConfigs.WorldHeatmapHeight, DefaultConfigs.WorldHeatmapWidth]
            : (double[,])gridSource.Clone();

        return new StationChartDataSnapshot(stationCount, distanceHistogram, bearingHistogram, grid);
    }

    private Dictionary<string, ChartQSOPoint> _getOrCreateLatestPointByCallsign(string band)
    {
        if (_latestStationPointByBandAndCallsign.TryGetValue(band, out var latestByCallsign))
            return latestByCallsign;

        latestByCallsign = new Dictionary<string, ChartQSOPoint>(StringComparer.OrdinalIgnoreCase);
        _latestStationPointByBandAndCallsign[band] = latestByCallsign;
        return latestByCallsign;
    }

    private static BandChartAccumulators _getOrCreateBandAccumulators(
        Dictionary<string, BandChartAccumulators> chartDataByBand,
        string band)
    {
        if (chartDataByBand.TryGetValue(band, out var bandAccumulators)) return bandAccumulators;

        bandAccumulators = new BandChartAccumulators();
        chartDataByBand[band] = bandAccumulators;
        return bandAccumulators;
    }

    private void _applyPointToBandAccumulators(
        Dictionary<string, BandChartAccumulators> chartDataByBand,
        ChartQSOPoint point,
        int delta)
    {
        if (delta == 0 || string.IsNullOrWhiteSpace(point.Band)) return;

        var bandAccumulators = _getOrCreateBandAccumulators(chartDataByBand, point.Band);
        _applyStationCountDelta(bandAccumulators.StationCountByDxcc, point.DXCC ?? string.Empty, delta);

        if (!point.IsAccurate) return;

        bandAccumulators.DistanceHistogram ??= _createDistanceHistogram();
        _applyHistogramDelta(
            bandAccumulators.DistanceHistogram,
            point.Distance,
            delta,
            DistanceHistogramMin,
            DistanceHistogramMax);

        bandAccumulators.BearingHistogram ??= _createBearingHistogram();
        _applyHistogramDelta(
            bandAccumulators.BearingHistogram,
            point.Azimuth,
            delta,
            BearingHistogramMin,
            BearingHistogramMax);

        bandAccumulators.GridStationCount ??=
            new double[DefaultConfigs.WorldHeatmapHeight, DefaultConfigs.WorldHeatmapWidth];
        _applyGridDelta(bandAccumulators.GridStationCount, point.Latitude, point.Longitude, delta);
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

    private static Histogram _createDistanceHistogram()
    {
        return Histogram.WithBinCount(DistanceHistogramBinCount, DistanceHistogramMin, DistanceHistogramMax);
    }

    private static Histogram _createBearingHistogram()
    {
        return Histogram.WithBinSize(BearingHistogramBinSize, BearingHistogramMin, BearingHistogramMax);
    }

    private static Histogram _cloneDistanceHistogram(Histogram source)
    {
        var histogram = _createDistanceHistogram();
        _copyHistogramCounts(source, histogram);
        return histogram;
    }

    private static Histogram _cloneBearingHistogram(Histogram source)
    {
        var histogram = _createBearingHistogram();
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

    private void _appendToPolarBuffer(ChartQSOPoint item)
    {
        _buffer[_nextIndex] = item;
        _nextIndex = (_nextIndex + 1) % _buffer.Length;
        _count = Math.Min(_count + 1, _buffer.Length);
    }
}
