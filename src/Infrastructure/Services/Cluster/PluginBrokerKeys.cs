namespace ComposeNowPlugins.Infrastructure.Services.Cluster;

public static class PluginBrokerKeys
{
    public const string NodesSet = "compose-now:plugin-nodes";
    public const string WorkersSet = "compose-now:plugin-workers";

    public static string Node(string nodeId) => $"compose-now:plugin-node:{nodeId}";

    public static string Lease(string leaseId) => $"compose-now:plugin-lease:{leaseId}";

    public static string Worker(string workerId) => $"compose-now:plugin-worker:{workerId}";

    public static string PluginOwner(string pluginId) => $"plugin:{{{pluginId}}}:owner";
}
