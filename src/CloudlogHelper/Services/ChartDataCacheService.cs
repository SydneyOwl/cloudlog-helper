using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using ScottPlot.Collections;

namespace CloudlogHelper.Services;

/// <summary>
/// Simple cache service for charts...
/// </summary>
/// <typeparam name="T"></typeparam>
public class ChartDataCacheService<T> : IChartDataCacheService<T>, IDisposable
{ 
    private T[] _buffer = new T[DefaultConfigs.DefaultChartDataCacheNumber];
    private int _nextIndex = 0;
    private int _count = 0;
    private readonly object _lock = new();
    
    private readonly Subject<T> _itemAddedSubject = new();
    private IObservable<T> ItemAdded => _itemAddedSubject.AsObservable();

    public IObservable<T> GetItemAddedObservable()
    {
        return ItemAdded;
    }

    public void Add(T item)
    {
        lock (_lock)
        {
            _buffer[_nextIndex] = item;
            _nextIndex = (_nextIndex + 1) % _buffer.Length;
            _count = Math.Min(_count + 1, _buffer.Length);
            _itemAddedSubject.OnNext(item);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _buffer = new T[DefaultConfigs.DefaultChartDataCacheNumber];
            _nextIndex = 0;
            _count = 0;
        }
    }

    public IEnumerable<T> TakeLatestN(int count, IEqualityComparer<T>? comparer = null, Func<T, bool>? filterCondition = null)
    {
        lock (_lock)
        {
            var result = new List<T>();
            var seen = new HashSet<T>(comparer);
        
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