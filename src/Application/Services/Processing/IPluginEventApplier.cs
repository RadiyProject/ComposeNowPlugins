using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Application.Services.Processing;

namespace ComposeNowPlugins.Application.Services.Processing;

public interface IPluginEventApplier
{
    public void Apply(
        IPluginEngine engine,
        Plugin plugin,
        PluginEvent pluginEvent
    );
}
