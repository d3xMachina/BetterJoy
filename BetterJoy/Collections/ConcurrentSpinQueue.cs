using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace BetterJoy.Collections;

public class ConcurrentSpinQueue<T>
{
    private readonly int _maxItems;
    private readonly Queue<T> _internalQueue;
    private SpinLock _lock;

    public ConcurrentSpinQueue(int maxItems)
    {
        _maxItems = maxItems;
        _internalQueue = new Queue<T>();
        _lock = new SpinLock();
    }

    public void Enqueue(T item)
    {
        LockInternalQueueAndCommand(
            queue =>
            {
                if (_internalQueue.Count >= _maxItems)
                {
                    _internalQueue.Dequeue();
                }
                _internalQueue.Enqueue(item);
            }
        );
    }

    public bool TryDequeue([MaybeNullWhen(false)] out T result)
    {
        var lockTaken = false;
        try
        {
            _lock.Enter(ref lockTaken);
            return _internalQueue.TryDequeue(out result);
        }
        finally
        {
            if (lockTaken)
            {
                _lock.Exit();
            }
        }
    }

    public void Clear()
    {
        LockInternalQueueAndCommand(queue => queue.Clear());
    }

    private void LockInternalQueueAndCommand(Action<Queue<T>> action)
    {
        var lockTaken = false;
        try
        {
            _lock.Enter(ref lockTaken);
            action(_internalQueue);
        }
        finally
        {
            if (lockTaken)
            {
                _lock.Exit();
            }
        }
    }
}
