namespace ComposeNowPlugins.Services.Cluster;

public static class PluginBrokerKeys
{
    public const string NodesSet = "compose-now:plugin-nodes";

    public static string Node(string nodeId) => $"compose-now:plugin-node:{nodeId}";

    public static string Lease(string leaseId) => $"compose-now:plugin-lease:{leaseId}";
}

