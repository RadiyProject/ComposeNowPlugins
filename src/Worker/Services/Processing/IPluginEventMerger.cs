using ComposeNowPlugins.Models;
using ComposeNowPlugins.Models.Ids;

namespace ComposeNowPlugins.Services.Processing;

public interface IPluginEventMerger
{
    public IReadOnlyList<PluginEvent> Merge(
        PluginId pluginId,
        IReadOnlyList<PluginEvent> controlEvents,
        IReadOnlyList<PluginEvent> blockEvents
    );
}
