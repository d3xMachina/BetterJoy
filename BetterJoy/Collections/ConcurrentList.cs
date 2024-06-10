using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace BetterJoy.Collections;

/// A thread-safe IEnumerator implementation.
/// https://www.codeproject.com/Articles/56575/Thread-safe-enumeration-in-C
public class SafeEnumerator<T> : IEnumerator<T>
{
    private readonly IEnumerator<T> _inner;
    private readonly object _lock;

    public SafeEnumerator(IEnumerable<T> inner, object @lock)
    {
        _lock = @lock;

        Monitor.Enter(_lock);
        _inner = inner.GetEnumerator();
    }

    public void Dispose()
    {
        // called when foreach loop finishes
        Monitor.Exit(_lock);
    }

    public bool MoveNext()
    {
        return _inner.MoveNext();
    }

    public void Reset()
    {
        _inner.Reset();
    }

    public T Current => _inner.Current;

    object IEnumerator.Current => Current;
}

// https://codereview.stackexchange.com/a/125341
public sealed class ConcurrentList<T> : IList<T>
{
    public T this[int index]
    {
        get { return LockInternalListAndGet(l => l[index]); }

        set { LockInternalListAndCommand(l => l[index] = value); }
    }

    public int Count
    {
        get { return LockInternalListAndQuery(l => l.Count); }
    }

    public bool IsReadOnly => false;

    public void Add(T item)
    {
        LockInternalListAndCommand(l => l.Add(item));
    }

    public void Clear()
    {
        LockInternalListAndCommand(l => l.Clear());
    }

    public bool Contains(T item)
    {
        return LockInternalListAndQuery(l => l.Contains(item));
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        LockInternalListAndCommand(l => l.CopyTo(array, arrayIndex));
    }

    public int IndexOf(T item)
    {
        return LockInternalListAndQuery(l => l.IndexOf(item));
    }

    public void Insert(int index, T item)
    {
        LockInternalListAndCommand(l => l.Insert(index, item));
    }

    public bool Remove(T item)
    {
        return LockInternalListAndQuery(l => l.Remove(item));
    }

    public void RemoveAt(int index)
    {
        LockInternalListAndCommand(l => l.RemoveAt(index));
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return LockInternalAndEnumerate();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return LockInternalAndEnumerate();
    }

    public void Set(IList<T> list)
    {
        lock (_lock)
        {
            _internalList.Clear();
            foreach (var item in list)
            {
                _internalList.Add(item);
            }
        }
    }

    #region Fields

    private readonly IList<T> _internalList;
    private readonly object _lock = new();

    #endregion

    #region ctor

    public ConcurrentList()
    {
        _internalList = new List<T>();
    }

    public ConcurrentList(int capacity)
    {
        _internalList = new List<T>(capacity);
    }

    public ConcurrentList(IEnumerable<T> list)
    {
        _internalList = new List<T>();
        foreach (var item in list)
        {
            _internalList.Add(item);
        }
    }

    #endregion

    #region Utilities

    private void LockInternalListAndCommand(Action<IList<T>> action)
    {
        lock (_lock)
        {
            action(_internalList);
        }
    }

    private T LockInternalListAndGet(Func<IList<T>, T> func)
    {
        lock (_lock)
        {
            return func(_internalList);
        }
    }

    private TObject LockInternalListAndQuery<TObject>(Func<IList<T>, TObject> query)
    {
        lock (_lock)
        {
            return query(_internalList);
        }
    }

    private IEnumerator<T> LockInternalAndEnumerate()
    {
        return new SafeEnumerator<T>(_internalList, _lock);
    }

    #endregion
}
