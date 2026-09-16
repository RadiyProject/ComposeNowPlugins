namespace ComposeNowPlugins.Infrastructure.Services.Cluster;

public sealed record PluginWorkerSnapshot(
    string WorkerId,
    string Address,
    DateTimeOffset UpdatedAt
);
