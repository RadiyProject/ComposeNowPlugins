using ComposeNowPlugins.Infrastructure.Extensions;

namespace ComposeNowPlugins.Worker.Extensions;

public static class WorkerApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerApplication(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddInfrastructure(configuration);
        services.AddPluginWorkerClusterServices();
        services.AddVstProcessing(configuration);
        services.AddGrpc();

        return services;
    }
}
