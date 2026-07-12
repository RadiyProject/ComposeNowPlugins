using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Worker.Services.Processing;

public interface IPluginEventMerger
{
    public IReadOnlyList<PluginEvent> Merge(
        PluginId pluginId,
        IReadOnlyList<PluginEvent> controlEvents,
        IReadOnlyList<PluginEvent> blockEvents
    );
}
