using ComposeNowPlugins.Application.Services.Processing;
using ComposeNowPlugins.Proxy.Services.Processing;

namespace ComposeNowPlugins.Proxy.Extensions;

public static class WorkerProcessingServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerProcessingClient(
        this IServiceCollection services
    )
    {
        services.AddScoped<IPluginBlockProcessor, GrpcPluginBlockProcessor>();

        return services;
    }
}
