namespace ComposeNowPlugins.Services.Cluster;

public interface IPluginNodeState
{
    string NodeId { get; }
    string WebSocketUrl { get; }
    int ActiveSessions { get; }
    double LoadPercent { get; }
    bool IsDraining { get; }
    bool TryAcquireSession();
    void ReleaseSession();
}
