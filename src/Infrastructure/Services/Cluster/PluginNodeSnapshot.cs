namespace ComposeNowPlugins.Services.Cluster;

public sealed record PluginNodeSnapshot(
    string NodeId,
    string WebSocketUrl,
    string[] Plugins,
    int ActiveSessions,
    double LoadPercent,
    bool Draining,
    DateTimeOffset UpdatedAt
);
