namespace ComposeNowPlugins.Application.Services.Processing;

public interface IPluginEnginePool : IAsyncDisposable
{
    ValueTask<IPluginEngineLease> AcquireAsync(
        string pluginName,
        int sampleRate,
        int blockSize,
        int channels,
        CancellationToken cancellationToken
    );

    ValueTask EvictIdleAsync(TimeSpan idleTimeout);
}
