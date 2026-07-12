using ComposeNowPlugins.Models.Ids;

namespace ComposeNowPlugins.Services.Processing;

public interface IPluginBlockProcessor
{
    public Task<PluginBlockProcessResult> ProcessBlockAsync(
        PluginId pluginId,
        ulong seq,
        int frames,
        bool offline,
        ReadOnlyMemory<float>? inputAudio,
        CancellationToken cancellationToken
    );
}
