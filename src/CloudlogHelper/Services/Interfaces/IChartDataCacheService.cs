using System;
using System.Collections.Generic;
using CloudlogHelper.Models;

namespace CloudlogHelper.Services.Interfaces;

public interface IChartDataCacheService
{
    IObservable<ChartQSOPoint> GetItemAddedObservable();

    IEnumerable<ChartQSOPoint> TakeLatestN(int count, IEqualityComparer<ChartQSOPoint>? comparer = null,
        Func<ChartQSOPoint, bool>? filterCondition = null);

    StationChartDataSnapshot GetStationChartDataSnapshotByBand(string? band, bool filterDupeByCallsign = false);
    void Add(ChartQSOPoint item);
    void Clear();
    void ClearPolarBuffer();
    void ClearAccuBuffer();
}
