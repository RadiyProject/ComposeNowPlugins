using ComposeNowPlugins.Services.Cluster;

namespace ComposeNowPlugins.Extensions;

public static class ClusterServiceCollectionExtensions
{
    public static IServiceCollection AddPluginClusterServices(
        this IServiceCollection services
    )
    {
        services.AddSingleton<IPluginNodeState, PluginNodeState>();
        services.AddSingleton<IPluginLeaseValidator, RedisPluginLeaseValidator>();
        services.AddHostedService<PluginNodeHeartbeatService>();

        return services;
    }
}

