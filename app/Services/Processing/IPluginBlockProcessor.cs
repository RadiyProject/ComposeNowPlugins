using ComposeNowPlugins.Models.Ids;

namespace ComposeNowPlugins.Services.Processing;

public interface IPluginBlockProcessor
{
    public Task<PluginBlockProcessResult> ProcessBlockAsync(
        PluginId pluginId,
        ulong seq,
        int frames,
        bool offline,
        CancellationToken cancellationToken
    );
}