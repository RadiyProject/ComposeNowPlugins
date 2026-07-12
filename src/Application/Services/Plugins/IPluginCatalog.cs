using ComposeNowPlugins.Domain.Configurations;

namespace ComposeNowPlugins.Application.Services.Plugins;

public interface IPluginCatalog
{
    IReadOnlyList<PluginDescriptor> GetAvailable();
    public PluginDescriptor? GetRequired(string pluginName);
    public PluginDescriptor? GetRequired(PluginType type);
    public bool Exists(PluginType type);
}