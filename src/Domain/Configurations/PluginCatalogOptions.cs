namespace ComposeNowPlugins.Configurations;

public sealed class PluginCatalogOptions
{
    public IReadOnlyList<PluginDescriptor> Plugins { get; init; } = [];
}