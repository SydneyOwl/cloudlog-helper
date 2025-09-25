using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using ScottPlot.Collections;
using ScottPlot.Statistics;

namespace CloudlogHelper.Services;

/// <summary>
/// Simple cache service for charts...
/// </summary>
/// <typeparam name="ChartQSOPoint"></typeparam>
public class ChartDataCacheService : IChartDataCacheService, IDisposable
{ 
    // <Band, <Dxcc, count>>
    private Dictionary<string, Dictionary<string, double?>?> _accumulatedStationCount = new();
    
    // <Band, Histogram>
    private Dictionary<string, Histogram> _accumulatedStationDistance = new();
    private Dictionary<string, Histogram> _accumulatedStationBearing = new();
    private Dictionary<string, double[,]> _accumulatedGridStationCount = new();

    private ChartQSOPoint[] _buffer = new ChartQSOPoint[DefaultConfigs.DefaultChartDataCacheNumber];
    private int _nextIndex = 0;
    private int _count = 0;
    private readonly object _lock = new();
    
    private readonly Subject<ChartQSOPoint> _itemAddedSubject = new();
    private IObservable<ChartQSOPoint> ItemAdded => _itemAddedSubject.AsObservable();

    public IObservable<ChartQSOPoint> GetItemAddedObservable()
    {
        return ItemAdded;
    }

    public void Add(ChartQSOPoint item)
    {
        lock (_lock)
        {
            // cycle buffer
            _buffer[_nextIndex] = item;
            _nextIndex = (_nextIndex + 1) % _buffer.Length;
            _count = Math.Min(_count + 1, _buffer.Length);
            
            // acc count
            if (!_accumulatedStationCount.TryGetValue(item.Band, out _))
                _accumulatedStationCount[item.Band] = new Dictionary<string, double?>();

            if (!_accumulatedStationCount[item.Band]!.TryGetValue(item.DXCC, out _))
                _accumulatedStationCount[item.Band]![item.DXCC] = 0;

            _accumulatedStationCount[item.Band]![item.DXCC] += 1;
                
            // acc dist
            if (!_accumulatedStationDistance.TryGetValue(item.Band, out _))
                _accumulatedStationDistance[item.Band] = Histogram.WithBinCount(50,0,20000);
                
            if (item.Distance >= 0) _accumulatedStationDistance[item.Band].Add(item.Distance);
                
            // acc bearing
            if (!_accumulatedStationBearing.TryGetValue(item.Band, out _))
                _accumulatedStationBearing[item.Band] = Histogram.WithBinSize(22.5,0,360);
                
            if (item.Azimuth >= 0) _accumulatedStationBearing[item.Band].Add(item.Azimuth);
            
            // acc grid data
            if (!_accumulatedGridStationCount.TryGetValue(item.Band, out _))
            {
                var tmp = new double[DefaultConfigs.WorldHeatmapHeight,DefaultConfigs.WorldHeatmapWidth];
                for (var i = 0; i < DefaultConfigs.WorldHeatmapWidth; i++)
                {
                    for (var j = 0; j < DefaultConfigs.WorldHeatmapHeight; j++)
                    {
                        tmp[j, i] = double.NaN;
                    }
                }

                _accumulatedGridStationCount[item.Band] = tmp;
            }
            
            
            var xIndex = (int)((item.Longitude + 180) / 360 * DefaultConfigs.WorldHeatmapWidth);
            var yIndex = (int)((item.Latitude + 90) / 180 * DefaultConfigs.WorldHeatmapHeight);
        
            xIndex = Math.Clamp(xIndex, 0, DefaultConfigs.WorldHeatmapWidth - 1);
            yIndex = Math.Clamp(yIndex, 0, DefaultConfigs.WorldHeatmapHeight - 1);
            
            if (double.IsNaN(_accumulatedGridStationCount[item.Band][yIndex, xIndex])) _accumulatedGridStationCount[item.Band][yIndex, xIndex] = 0;
            _accumulatedGridStationCount[item.Band][yIndex, xIndex] += 1;
            
            _itemAddedSubject.OnNext(item);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _buffer = new ChartQSOPoint[DefaultConfigs.DefaultChartDataCacheNumber];
            _nextIndex = 0;
            _count = 0;
            
            _accumulatedStationCount.Clear();
            _accumulatedStationBearing.Clear();
            _accumulatedStationDistance.Clear();
        }
    }
    
    public double[,]? GetGridStationCountByBand(string? band)
    {
        if (band is null) return null;
        return _accumulatedGridStationCount.GetValueOrDefault(band);
    }

    public Histogram? GetDistanceHistogramByBand(string? band)
    {
        if (band is null) return null;
        return _accumulatedStationDistance.GetValueOrDefault(band);
    }
    
    public Histogram? GetBearingHistogramByBand(string? band)
    {
        if (band is null) return null;
        return _accumulatedStationBearing.GetValueOrDefault(band);
    }
    
    public Dictionary<string, double?>? GetStationCountByBand(string? band)
    {
        if (band is null) return null;
        return _accumulatedStationCount.GetValueOrDefault(band);
    }

    public IEnumerable<ChartQSOPoint> TakeLatestN(int count, IEqualityComparer<ChartQSOPoint>? comparer = null, Func<ChartQSOPoint, bool>? filterCondition = null)
    {
        lock (_lock)
        {
            var result = new List<ChartQSOPoint>();
            var seen = new HashSet<ChartQSOPoint>(comparer);
        
            var takeCount = Math.Min(count, _count);
            var itemsTaken = 0;

            for (var i = 1; itemsTaken < takeCount && i <= _count; i++)
            {
                var index = (_nextIndex - i + _buffer.Length) % _buffer.Length;
                var item = _buffer[index];

                if (filterCondition != null && !filterCondition.Invoke(item))
                    continue;

                if (comparer != null && seen.Contains(item))
                    continue;

                if (comparer != null) seen.Add(item);
                
                result.Add(item);
                itemsTaken++;
            }

            return result;
        }
    }

    public void Dispose() { }
}