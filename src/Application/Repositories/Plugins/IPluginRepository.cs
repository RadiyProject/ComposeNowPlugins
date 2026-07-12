using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Application.Repositories.Plugins;

public interface IPluginRepository : IRepository<Plugin, PluginId>
{
    public Task RefreshTtlAsync(PluginId key);
}
