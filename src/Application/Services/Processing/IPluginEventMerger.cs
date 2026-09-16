using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Application.Services.Processing;

public interface IPluginEventMerger
{
    public IReadOnlyList<PluginEvent> Merge(
        PluginId pluginId,
        IReadOnlyList<PluginEvent> controlEvents,
        IReadOnlyList<PluginEvent> blockEvents
    );
}
