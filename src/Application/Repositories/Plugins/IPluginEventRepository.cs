using ComposeNowPlugins.Models;
using ComposeNowPlugins.Models.Ids;

namespace ComposeNowPlugins.Repositories.Plugins;

public interface IPluginEventRepository
{
    public Task AddBlockEventAsync(
        PluginId pluginId,
        ulong seq,
        PluginEvent pluginEvent
    );

    public Task AddControlEventAsync(
        PluginId pluginId,
        PluginEvent pluginEvent
    );

    public Task<IReadOnlyList<PluginEvent>> GetBlockEventsAsync(
        PluginId pluginId,
        ulong seq
    );

    public Task<IReadOnlyList<PluginEvent>> GetControlEventsAsync(
        PluginId pluginId
    );

    public Task DeleteBlockEventsAsync(
        PluginId pluginId,
        ulong seq
    );

    public Task DeleteControlEventsAsync(
        PluginId pluginId
    );

    public Task<IReadOnlyList<PluginEvent>> PopControlEventsAsync(
        PluginId pluginId
    );
}
