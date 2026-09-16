using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Application.Repositories.Plugins;

public interface IPluginEventRepository
{
    public Task AddBlockEventAsync(
        PluginId pluginId,
        ulong epoch,
        ulong seq,
        PluginEvent pluginEvent
    );

    public Task AddControlEventAsync(
        PluginId pluginId,
        PluginEvent pluginEvent
    );

    public Task<IReadOnlyList<PluginEventDelivery>> ReadBlockEventsAsync(
        PluginId pluginId,
        ulong epoch,
        ulong seq,
        string consumerId
    );

    public Task<IReadOnlyList<PluginEventDelivery>> ReadControlEventsAsync(
        PluginId pluginId,
        string consumerId
    );
}
