using ComposeNowPlugins.Infrastructure.Services.Cluster;
using ComposeNowPlugins.Application.Services.Processing;

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

    public static IServiceCollection AddPluginWorkerDiscovery(
        this IServiceCollection services
    )
    {
        services.AddScoped<IPluginWorkerRouter, RedisPluginWorkerRouter>();
        return services;
    }

    public static IServiceCollection AddPluginWorkerClusterServices(
        this IServiceCollection services
    )
    {
        services.AddSingleton<IPluginWorkerIdentity, PluginWorkerIdentity>();
        services.AddHostedService<PluginWorkerHeartbeatService>();
        return services;
    }
}
