using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Application.Services.Processing;

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
