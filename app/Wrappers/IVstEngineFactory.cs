namespace ComposeNowPlugins.Wrappers;

public interface IVstEngineFactory
{
    public VstEngine Create(string pluginName, int sampleRate, int blockSize, int channels);
}
