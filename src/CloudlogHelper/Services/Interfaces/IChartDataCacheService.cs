using System;
using System.Collections.Generic;
using System.Reactive;

namespace CloudlogHelper.Services.Interfaces;

public interface IChartDataCacheService<T>
{
    IObservable<T> GetItemAddedObservable();
    IEnumerable<T> TakeLatestN(int count, IEqualityComparer<T>? comparer = null, Func<T, bool>? filterCondition = null);
    void Add(T item);
    void Clear();
}