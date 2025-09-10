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
    private readonly T[] _buffer = new T[DefaultConfigs.DefaultChartDataCacheNumber];
    private int _nextIndex = 0;
    private int _count = 0;
    private readonly object _lock = new();
    
    private readonly Subject<Unit> _itemAddedSubject = new();
    private IObservable<Unit> ItemAdded => _itemAddedSubject.AsObservable();

    public IObservable<Unit> GetItemAddedObservable()
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
            _itemAddedSubject.OnNext(Unit.Default);
        }
    }

    public IEnumerable<T> TakeLatestN(int count)
    {
        lock (_lock)
        {
            count = Math.Min(count, _count);
            var result = new T[count];
            
            var startIndex = (_nextIndex - count + _buffer.Length) % _buffer.Length;
            
            for (var i = 0; i < count; i++)
            {
                var index = (startIndex + i) % _buffer.Length;
                result[i] = _buffer[index];
            }
            
            return result;
        }
    }

    public void Dispose() { }
}