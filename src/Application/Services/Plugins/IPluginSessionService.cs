using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Application.Services.Plugins;

public interface IPluginSessionService
{
    Task<Plugin> GetOrCreateAsync(
        PluginId pluginId,
        string pluginName
    );
}
