using ComposeNowPlugins.Infrastructure.Extensions;

namespace ComposeNowPlugins.Proxy.Extensions;

public static class ProxyApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddProxyApplication(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddControllers();
        services.AddInfrastructure(configuration);
        services.AddPluginClusterServices();
        services.AddPluginWorkerDiscovery();
        services.AddRuntimeSessions();
        services.AddRealtimeTransports();
        services.AddWorkerProcessingClient();

        return services;
    }
}
