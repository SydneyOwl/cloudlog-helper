using System;
using System.Collections.Generic;
using CloudlogHelper.Models;
using ScottPlot.Statistics;

namespace CloudlogHelper.Services.Interfaces;

public interface IChartDataCacheService
{
    IObservable<ChartQSOPoint> GetItemAddedObservable();

    IEnumerable<ChartQSOPoint> TakeLatestN(int count, IEqualityComparer<ChartQSOPoint>? comparer = null,
        Func<ChartQSOPoint, bool>? filterCondition = null);

    public double[,]? GetGridStationCountByBand(string? band);
    Histogram? GetDistanceHistogramByBand(string? band);
    Histogram? GetBearingHistogramByBand(string? band);
    public Dictionary<string, double?>? GetStationCountByBand(string? band);
    void Add(ChartQSOPoint item);
    void Clear();
    void ClearPolarBuffer();
    void ClearAccuBuffer();
}