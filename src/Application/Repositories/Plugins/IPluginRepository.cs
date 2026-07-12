using ComposeNowPlugins.Models;
using ComposeNowPlugins.Models.Ids;

namespace ComposeNowPlugins.Repositories.Plugins;

public interface IPluginRepository : IRepository<Plugin, PluginId>
{
    public Task RefreshTtlAsync(PluginId key);
}
