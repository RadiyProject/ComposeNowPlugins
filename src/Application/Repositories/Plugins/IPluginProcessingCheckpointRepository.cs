using ComposeNowPlugins.Application.Services.Processing;
using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Application.Repositories.Plugins;

public interface IPluginProcessingCheckpointRepository
{
    Task<PluginBlockProcessResult?> GetResultAsync(
        PluginId pluginId,
        ulong epoch,
        ulong seq
    );

    Task CommitAsync(
        Plugin plugin,
        string workerId,
        ulong epoch,
        ulong seq,
        PluginBlockProcessResult result,
        IReadOnlyList<PluginEventDelivery> deliveries
    );
}
