namespace ComposeNowPlugins.Services.Cluster;

public sealed record PluginNodeLease(
    string LeaseId,
    string NodeId,
    string PluginName,
    string WebSocketUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt
);

