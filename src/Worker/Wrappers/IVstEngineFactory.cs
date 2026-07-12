using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Worker.Wrappers;

public interface IVstEngineFactory
{
    public VstEngine Create(PluginId pluginId, string pluginName, int sampleRate, int blockSize, int channels);
}
