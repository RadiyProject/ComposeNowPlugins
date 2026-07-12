using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Infrastructure.Cache;

public static class CacheKeys
{
    public static string RuntimeSession(string sessionId)
    {
        return $"runtime-session:{sessionId}";
    }

    public static string PluginState(PluginId pluginId)
    {
        return $"plugin-state:{pluginId.GetValue()}";
    }

    public static string PluginBlockEvents(PluginId pluginId, ulong seq)
    {
        return $"plugin-events:{pluginId.GetValue()}:block:{seq}";
    }

    public static string PluginControlEvents(PluginId pluginId)
    {
        return $"plugin-events:{pluginId.GetValue()}:control";
    }

    public static string PluginEngineLock(string pluginName)
    {
        return $"plugin-engine-lock:{pluginName}";
    }
}