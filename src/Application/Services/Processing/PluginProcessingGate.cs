namespace ComposeNowPlugins.Application.Services.Processing;

public sealed class PluginProcessingGate : IPluginProcessingGate
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _entries = [];

    public async Task<T> RunAsync<T>(
        string key,
        Func<Task<T>> action,
        CancellationToken cancellationToken
    )
    {
        Entry entry;
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }

            entry.Users++;
        }

        bool entered = false;
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            entered = true;
            return await action();
        }
        finally
        {
            if (entered)
            {
                entry.Semaphore.Release();
            }

            lock (_lock)
            {
                entry.Users--;
                if (entry.Users == 0)
                {
                    _entries.Remove(key);
                    entry.Semaphore.Dispose();
                }
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Users { get; set; }
    }
}
