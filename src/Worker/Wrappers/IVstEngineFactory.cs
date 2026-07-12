using ComposeNowPlugins.Models.Ids;

namespace ComposeNowPlugins.Wrappers;

public interface IVstEngineFactory
{
    public VstEngine Create(PluginId pluginId, string pluginName, int sampleRate, int blockSize, int channels);
}
