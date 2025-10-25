using System;
using System.Collections.Generic;

namespace CloudlogHelper.Utils;

/// <summary>
///     Thread-safe dynamic fixed-size queue with auto-eviction
///     Not a very good approach, but it just works...
/// </summary>
/// <typeparam name="T"></typeparam>
public class ConcurrentFixedSizeQueue<T>
{
    private readonly object _lockObject = new();
    private readonly int _maxCapacity;
    private readonly int _minCapacity;
    private readonly Queue<T> _queue;
    private int _currentCapacity;

    public ConcurrentFixedSizeQueue(int initialCapacity, int minCapacity = 1, int maxCapacity = int.MaxValue)
    {
        if (initialCapacity < minCapacity || initialCapacity > maxCapacity)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));

        _currentCapacity = initialCapacity;
        _minCapacity = minCapacity;
        _maxCapacity = maxCapacity;
        _queue = new Queue<T>(initialCapacity);
    }

    public int Count
    {
        get
        {
            lock (_lockObject)
            {
                return _queue.Count;
            }
        }
    }

    public int Capacity
    {
        get
        {
            lock (_lockObject)
            {
                return _currentCapacity;
            }
        }
    }

    public void Enqueue(T item)
    {
        lock (_lockObject)
        {
            _queue.Enqueue(item);

            while (_queue.Count > _currentCapacity) _queue.Dequeue();
        }
    }

    public bool TryEnqueue(T item)
    {
        lock (_lockObject)
        {
            try
            {
                Enqueue(item);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public T Dequeue()
    {
        lock (_lockObject)
        {
            return _queue.Dequeue();
        }
    }

    public bool TryDequeue(out T result)
    {
        lock (_lockObject)
        {
            if (_queue.Count > 0)
            {
                result = _queue.Dequeue();
                return true;
            }

            result = default;
            return false;
        }
    }

    public bool TryPeek(out T result)
    {
        lock (_lockObject)
        {
            if (_queue.Count > 0)
            {
                result = _queue.Peek();
                return true;
            }

            result = default;
            return false;
        }
    }

    public void Resize(int newCapacity)
    {
        lock (_lockObject)
        {
            if (newCapacity == _currentCapacity) return;
            if (newCapacity < _minCapacity || newCapacity > _maxCapacity)
                throw new ArgumentOutOfRangeException(nameof(newCapacity));

            _currentCapacity = newCapacity;

            while (_queue.Count > _currentCapacity) _queue.Dequeue();
        }
    }

    public bool TryResize(int newCapacity)
    {
        lock (_lockObject)
        {
            try
            {
                Resize(newCapacity);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Expand(int additionalCapacity = 1)
    {
        lock (_lockObject)
        {
            Resize(_currentCapacity + additionalCapacity);
        }
    }

    public void Shrink(int reduceCapacity = 1)
    {
        lock (_lockObject)
        {
            Resize(Math.Max(_minCapacity, _currentCapacity - reduceCapacity));
        }
    }

    public void Clear()
    {
        lock (_lockObject)
        {
            _queue.Clear();
        }
    }

    public IEnumerable<T> GetItems()
    {
        lock (_lockObject)
        {
            return new List<T>(_queue);
        }
    }

    public T[] ToArray()
    {
        lock (_lockObject)
        {
            return _queue.ToArray();
        }
    }

    public bool Contains(T item)
    {
        lock (_lockObject)
        {
            return _queue.Contains(item);
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        lock (_lockObject)
        {
            _queue.CopyTo(array, arrayIndex);
        }
    }
}