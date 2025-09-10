using System;
using System.Collections.Generic;
using System.Reactive;

namespace CloudlogHelper.Services.Interfaces;

public interface IChartDataCacheService<T>
{
    IObservable<Unit> GetItemAddedObservable();
    IEnumerable<T> TakeLatestN(int count);
    void Add(T item);
}