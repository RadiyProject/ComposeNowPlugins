using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Worker.Wrappers;

namespace ComposeNowPlugins.Worker.Services.Processing;

public interface IPluginEventApplier
{
    public void Apply(
        VstEngine vst,
        Plugin plugin,
        PluginEvent pluginEvent
    );
}
