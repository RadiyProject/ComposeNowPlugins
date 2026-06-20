namespace ComposeNowPlugins.Services.Cluster;

public sealed record PluginNodeLease(
    string LeaseId,
    string ReservationId,
    string NodeId,
    string PluginName,
    string WebSocketUrl,
    int ActiveSessions,
    int MaxSessions,
    double LoadPercent,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt
);
