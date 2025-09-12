using System;
using System.Collections.Generic;
using System.Reactive;

namespace CloudlogHelper.Services.Interfaces;

public interface IChartDataCacheService<T>
{
    IObservable<Unit> GetItemAddedObservable();
    IEnumerable<T> TakeLatestN(int count, bool filterDupe = false, IEqualityComparer<T>? comparer = null);
    void Add(T item);
    void Clear();
}