using ComposeNowPlugins.Models.Ids;

namespace ComposeNowPlugins.Cache;

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
}