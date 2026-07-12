namespace ComposeNowPlugins.Worker.Services.Processing;

public sealed class PluginDefaultParameterProvider : IPluginDefaultParameterProvider
{
    private static readonly IReadOnlyDictionary<uint, float> DelayDefaults = new Dictionary<uint, float>
    {
        [100] = 0.32f,
        [101] = 0.35f,
        [102] = 0.35f,
        [103] = 0.0f
    };

    private static readonly IReadOnlyDictionary<uint, float> ReverbDefaults = new Dictionary<uint, float>
    {
        [100] = 0.55f,
        [101] = 0.35f,
        [102] = 0.65f,
        [103] = 0.8f
    };

    public IReadOnlyDictionary<uint, float>? GetDefaults(string pluginName)
    {
        return pluginName switch
        {
            "Delay" => DelayDefaults,
            "Reverb" => ReverbDefaults,
            _ => null
        };
    }
}
