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
        services.AddVstProcessing();
        services.AddGrpc();

        return services;
    }
}
