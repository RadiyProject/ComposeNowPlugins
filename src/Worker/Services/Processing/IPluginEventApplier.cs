using ComposeNowPlugins.Models;
using ComposeNowPlugins.Wrappers;

namespace ComposeNowPlugins.Services.Processing;

public interface IPluginEventApplier
{
    public void Apply(
        VstEngine vst,
        Plugin plugin,
        PluginEvent pluginEvent
    );
}
