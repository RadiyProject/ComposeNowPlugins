namespace ComposeNowPlugins.Infrastructure.Services.Cluster;

public sealed record PluginNodeLease(
    string LeaseId,
    string NodeId,
    string PluginName,
    string WebSocketUrl,
    int ActiveSessions,
    double LoadPercent,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt
);
