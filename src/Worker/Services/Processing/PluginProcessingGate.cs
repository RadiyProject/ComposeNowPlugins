using System.Collections.Concurrent;

namespace ComposeNowPlugins.Worker.Services.Processing;

public sealed class PluginProcessingGate : IPluginProcessingGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public async Task<T> RunAsync<T>(
        string key,
        Func<Task<T>> action,
        CancellationToken cancellationToken
    )
    {
        SemaphoreSlim gate = _gates.GetOrAdd(
            key,
            _ => new SemaphoreSlim(1, 1)
        );

        await gate.WaitAsync(cancellationToken);

        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}