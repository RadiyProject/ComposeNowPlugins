namespace ComposeNowPlugins.Worker.Services.Processing;

public interface IPluginDefaultParameterProvider
{
    public IReadOnlyDictionary<uint, float>? GetDefaults(string pluginName);
}
