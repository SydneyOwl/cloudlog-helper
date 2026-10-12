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

    /// <summary>
    ///     Aggregation key of the statistics accumulators. Mode is part of the key so the
    ///     statistics chart can be filtered per mode; client is deliberately not, because
    ///     the statistics chart is a cross-client band view.
    /// </summary>
    private readonly record struct BandModeKey(string Band, string Mode);

    private sealed class BandModeKeyComparer : IEqualityComparer<BandModeKey>
    {
        public bool Equals(BandModeKey x, BandModeKey y)
        {
            return string.Equals(x.Band, y.Band, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.Mode, y.Mode, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(BandModeKey obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Band ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Mode ?? string.Empty));
        }
    }

    private static readonly BandModeKeyComparer KeyComparer = new();

    // (band, mode) aggregates for statistics chart
    private readonly Dictionary<BandModeKey, BandChartAccumulators> _accumulatedChartDataByBandAndMode =
        new(KeyComparer);
    private readonly Dictionary<BandModeKey, BandChartAccumulators> _dedupChartDataByBandAndMode =
        new(KeyComparer);

    // <(band, mode), <Callsign, latest point>>
    private readonly Dictionary<BandModeKey, Dictionary<string, ChartQSOPoint>> _latestStationPointByBandModeAndCallsign =
        new(KeyComparer);

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
                _applyPointToBandAccumulators(_accumulatedChartDataByBandAndMode, item, 1);

                if (!string.IsNullOrWhiteSpace(item.DxCallsign) && !string.IsNullOrWhiteSpace(item.Band))
                {
                    var dedupeCache = _getOrCreateLatestPointByCallsign(_keyOf(item));

                    if (dedupeCache.TryGetValue(item.DxCallsign, out var oldPoint))
                        _applyPointToBandAccumulators(_dedupChartDataByBandAndMode, oldPoint, -1);

                    dedupeCache[item.DxCallsign] = item;
                    _applyPointToBandAccumulators(_dedupChartDataByBandAndMode, item, 1);
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
            _accumulatedChartDataByBandAndMode.Clear();
            _latestStationPointByBandModeAndCallsign.Clear();
            _dedupChartDataByBandAndMode.Clear();
        }
    }

    public StationChartDataSnapshot GetStationChartDataSnapshot(string? band, string? mode,
        bool filterDupeByCallsign = false)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(band)) return _buildEmptyStationChartDataSnapshot();

            var source = filterDupeByCallsign ? _dedupChartDataByBandAndMode : _accumulatedChartDataByBandAndMode;

            // A selected mode narrows the view to that single bucket; an empty mode
            // merges every bucket of the band instead.
            var buckets = new List<BandChartAccumulators>();
            foreach (var kv in source)
            {
                if (!string.Equals(kv.Key.Band, band, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(mode) &&
                    !string.Equals(kv.Key.Mode, mode, StringComparison.OrdinalIgnoreCase)) continue;
                buckets.Add(kv.Value);
            }

            if (buckets.Count == 0) return _buildEmptyStationChartDataSnapshot();
            if (buckets.Count == 1) return _buildSnapshotFromBucket(buckets[0]);
            return _buildSnapshotFromBuckets(buckets);
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

    private StationChartDataSnapshot _buildSnapshotFromBucket(BandChartAccumulators bandData)
    {
        var stationCount = new Dictionary<string, double?>(bandData.StationCountByDxcc,
            StringComparer.OrdinalIgnoreCase);

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

    private StationChartDataSnapshot _buildSnapshotFromBuckets(List<BandChartAccumulators> buckets)
    {
        var snapshot = _buildSnapshotFromBucket(buckets[0]);

        for (var i = 1; i < buckets.Count; i++)
        {
            var bucket = buckets[i];
            _mergeStationCounts(snapshot.StationCountByDxcc, bucket.StationCountByDxcc);
            _mergeHistogramCounts(snapshot.DistanceHistogram, bucket.DistanceHistogram);
            _mergeHistogramCounts(snapshot.BearingHistogram, bucket.BearingHistogram);
            _mergeGridCounts(snapshot.GridStationCount, bucket.GridStationCount);
        }

        return snapshot;
    }

    private static void _mergeStationCounts(Dictionary<string, double?> target, Dictionary<string, double?> source)
    {
        foreach (var kv in source)
            target[kv.Key] = (target.TryGetValue(kv.Key, out var existing) ? existing ?? 0 : 0) + (kv.Value ?? 0);
    }

    private static void _mergeHistogramCounts(Histogram target, Histogram? source)
    {
        if (source is null) return;

        var targetCounts = target.Counts;
        var sourceCounts = source.Counts;
        var len = Math.Min(targetCounts.Length, sourceCounts.Length);
        for (var i = 0; i < len; i++)
            targetCounts[i] += sourceCounts[i];
    }

    private static void _mergeGridCounts(double[,] target, double[,]? source)
    {
        if (source is null) return;

        for (var y = 0; y < target.GetLength(0); y++)
        for (var x = 0; x < target.GetLength(1); x++)
            target[y, x] += source[y, x];
    }

    private Dictionary<string, ChartQSOPoint> _getOrCreateLatestPointByCallsign(BandModeKey key)
    {
        if (_latestStationPointByBandModeAndCallsign.TryGetValue(key, out var latestByCallsign))
            return latestByCallsign;

        latestByCallsign = new Dictionary<string, ChartQSOPoint>(StringComparer.OrdinalIgnoreCase);
        _latestStationPointByBandModeAndCallsign[key] = latestByCallsign;
        return latestByCallsign;
    }

    private static BandModeKey _keyOf(ChartQSOPoint point)
    {
        return new BandModeKey(point.Band ?? string.Empty, point.Mode ?? string.Empty);
    }

    private static BandChartAccumulators _getOrCreateBandAccumulators(
        Dictionary<BandModeKey, BandChartAccumulators> chartDataByBand,
        BandModeKey key)
    {
        if (chartDataByBand.TryGetValue(key, out var bandAccumulators)) return bandAccumulators;

        bandAccumulators = new BandChartAccumulators();
        chartDataByBand[key] = bandAccumulators;
        return bandAccumulators;
    }

    private void _applyPointToBandAccumulators(
        Dictionary<BandModeKey, BandChartAccumulators> chartDataByBand,
        ChartQSOPoint point,
        int delta)
    {
        if (delta == 0 || string.IsNullOrWhiteSpace(point.Band)) return;

        var bandAccumulators = _getOrCreateBandAccumulators(chartDataByBand, _keyOf(point));
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
