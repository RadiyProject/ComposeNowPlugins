using ComposeNowPlugins.Domain.Configurations;
using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Domain.Factories;

public sealed class PluginFactory : ModelFactory<Plugin, PluginId>
{
    public Plugin Create(PluginDescriptor descriptor)
    {
        return Create(id => Create(id, descriptor));
    }

    public Plugin Create(PluginId id, PluginDescriptor descriptor)
    {
        return new Plugin
        {
            Id = id,
            Descriptor = descriptor
        };
    }

    public PluginId CreateId(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? CreateNewId()
            : new PluginId(value);
    }

    protected override PluginId CreateNewId()
    {
        return PluginId.New();
    }
}
