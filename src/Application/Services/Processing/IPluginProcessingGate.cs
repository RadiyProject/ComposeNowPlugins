namespace ComposeNowPlugins.Application.Services.Processing;

public interface IPluginProcessingGate
{
    Task<T> RunAsync<T>(
        string key,
        Func<Task<T>> action,
        CancellationToken cancellationToken
    );
}
