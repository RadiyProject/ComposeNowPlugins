namespace ComposeNowPlugins.Proxy.Transports;

public sealed class SequencedBuffer<T>(int capacity, ulong maxFutureDistance)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<ulong, T> _items = [];
    private readonly Dictionary<ulong, TaskCompletionSource<T>> _waiters = [];

    public bool TryAdd(ulong sequence, T value, long lastProcessedSequence)
    {
        ulong maxAllowed = lastProcessedSequence < 0
            ? maxFutureDistance
            : AddSaturating((ulong)lastProcessedSequence, maxFutureDistance);

        if (sequence > maxAllowed)
        {
            return false;
        }

        TaskCompletionSource<T>? waiter = null;
        lock (_lock)
        {
            if (_waiters.Remove(sequence, out waiter))
            {
            }
            else
            {
                if (!_items.ContainsKey(sequence) && _items.Count >= capacity)
                {
                    return false;
                }

                _items[sequence] = value;
            }
        }

        waiter?.TrySetResult(value);
        return true;
    }

    public bool TryTake(ulong sequence, out T? value)
    {
        lock (_lock)
        {
            return _items.Remove(sequence, out value);
        }
    }

    public async Task<T?> WaitAsync(
        ulong sequence,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        TaskCompletionSource<T> waiter;
        lock (_lock)
        {
            if (_items.Remove(sequence, out T? existing))
            {
                return existing;
            }

            waiter = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters[sequence] = waiter;
        }

        try
        {
            return await waiter.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return default;
        }
        finally
        {
            lock (_lock)
            {
                if (_waiters.TryGetValue(sequence, out TaskCompletionSource<T>? current) &&
                    ReferenceEquals(current, waiter))
                {
                    _waiters.Remove(sequence);
                }
            }
        }
    }

    public void Clear()
    {
        TaskCompletionSource<T>[] waiters;
        lock (_lock)
        {
            _items.Clear();
            waiters = [.. _waiters.Values];
            _waiters.Clear();
        }

        foreach (TaskCompletionSource<T> waiter in waiters)
        {
            waiter.TrySetCanceled();
        }
    }

    private static ulong AddSaturating(ulong value, ulong increment)
    {
        return ulong.MaxValue - value < increment
            ? ulong.MaxValue
            : value + increment;
    }
}
