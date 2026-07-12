using Microsoft.Extensions.Options;
using ComposeNowPlugins.Domain.Configurations;

namespace ComposeNowPlugins.Infrastructure.Services.Plugins;

public sealed class PluginCatalog(IOptionsMonitor<PluginCatalogOptions> options) : IPluginCatalog
{
    private readonly IOptionsMonitor<PluginCatalogOptions> _options = options;

    public IReadOnlyList<PluginDescriptor> GetAvailable()
    {
        return [.._options.CurrentValue.Plugins.Where(plugin => plugin.Enabled)];
    }

    public PluginDescriptor? GetRequired(string pluginName)
    {
        return _options.CurrentValue.Plugins
            .FirstOrDefault(plugin => plugin.Enabled && plugin.Name == pluginName);
    }

    public PluginDescriptor? GetRequired(PluginType type)
    {
        return _options.CurrentValue.Plugins
            .FirstOrDefault(plugin => plugin.Enabled && plugin.Type == type);
    }

    public bool Exists(PluginType type)
    {
        return _options.CurrentValue.Plugins.Any(plugin => plugin.Enabled && plugin.Type == type);
    }
}