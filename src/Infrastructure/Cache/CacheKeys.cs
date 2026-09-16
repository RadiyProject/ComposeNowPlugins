using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Infrastructure.Cache;

public static class CacheKeys
{
    public static string PluginState(PluginId pluginId)
    {
        return $"plugin:{{{pluginId.GetValue()}}}:state";
    }

    public static string PluginBlockEvents(PluginId pluginId, ulong epoch, ulong seq)
    {
        return $"plugin:{{{pluginId.GetValue()}}}:events:{epoch}:block:{seq}";
    }

    public static string PluginControlEvents(PluginId pluginId)
    {
        return $"plugin:{{{pluginId.GetValue()}}}:events:control";
    }

    public static string PluginBlockResult(PluginId pluginId, ulong epoch, ulong seq)
    {
        return $"plugin:{{{pluginId.GetValue()}}}:result:{epoch}:{seq}";
    }

    public const string PluginEventConsumerGroup = "plugin-processors";
}
