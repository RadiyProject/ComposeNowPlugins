using ComposeNowPlugins.Services.Plugins;

namespace ComposeNowPlugins.Wrappers;

public sealed class VstEngineFactory(IPluginCatalog pluginCatalog, ILoggerFactory loggerFactory, IConfiguration cfg) : IVstEngineFactory
{
    private List<Tuple<string, VstEngine>> _vstEnginesList = [];

    private readonly IPluginCatalog _pluginCatalog = pluginCatalog;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly IConfiguration _cfg = cfg;

    public VstEngine Create(string pluginName)
    {
        var descriptor = _pluginCatalog.GetRequired(pluginName)
            ?? throw new InvalidOperationException($"Plugin with name '{pluginName}' is not registered or disabled.");

        VstEngine? vstEngine = _vstEnginesList.Find(vstEngine => vstEngine.Item1 == pluginName)?.Item2;
        if (vstEngine != null)
        {
            return vstEngine;
        }

        string vstPath = _cfg["VST3_PATH"]
            ?? throw new NullReferenceException("Cannot get VST3 path. Config property doesn't exist.");

        vstEngine = new VstEngine(
            $"{vstPath}/{descriptor.PluginPath}",
            _loggerFactory.CreateLogger<VstEngine>()
        );
        _vstEnginesList.Add(new Tuple<string, VstEngine>(pluginName, vstEngine));

        return vstEngine;
    }
}