using ComposeNowPlugins.Infrastructure.Services.Cluster;

namespace ComposeNowPlugins.Infrastructure.Extensions;

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

