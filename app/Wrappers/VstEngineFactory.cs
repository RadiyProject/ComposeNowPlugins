using System.Collections.Concurrent;
using ComposeNowPlugins.Models.Ids;
using ComposeNowPlugins.Services.Plugins;

namespace ComposeNowPlugins.Wrappers;

public sealed class VstEngineFactory(IPluginCatalog pluginCatalog, ILoggerFactory loggerFactory, IConfiguration cfg) : IVstEngineFactory
{
    private readonly ConcurrentDictionary<string, VstEngine> _vstEngines = new();

    private readonly IPluginCatalog _pluginCatalog = pluginCatalog;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly IConfiguration _cfg = cfg;

    public VstEngine Create(PluginId pluginId, string pluginName, int sampleRate, int blockSize, int channels)
    {
        string key = $"{pluginName}:{pluginId.GetValue()}";

        return _vstEngines.GetOrAdd(
            key,
            _ => CreateNew(pluginName, sampleRate, blockSize, channels)
        );
    }

    private VstEngine CreateNew(string pluginName, int sampleRate, int blockSize, int channels)
    {
        var descriptor = _pluginCatalog.GetRequired(pluginName)
            ?? throw new InvalidOperationException($"Plugin with name '{pluginName}' is not registered or disabled.");

        string vstPath = _cfg["VST3_PATH"]
            ?? throw new NullReferenceException("Cannot get VST3 path. Config property doesn't exist.");

        return new VstEngine(
            $"{vstPath}/{descriptor.PluginPath}",
            _loggerFactory.CreateLogger<VstEngine>(),
            sampleRate,
            blockSize,
            channels
        );
    }
}
