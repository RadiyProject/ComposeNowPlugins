namespace ComposeNowPlugins.Application.Services.Processing;

public interface IPluginEngineLease : IAsyncDisposable
{
    IPluginEngine Engine { get; }
}
