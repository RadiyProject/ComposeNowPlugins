using ComposeNowPlugins.Configurations;
using ComposeNowPlugins.Services.Processing;

namespace ComposeNowPlugins.Extensions;

public static class WorkerProcessingServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerProcessingClient(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<PluginWorkerOptions>(
            configuration.GetSection(PluginWorkerOptions.SectionName)
        );

        services.AddScoped<IPluginBlockProcessor, GrpcPluginBlockProcessor>();

        return services;
    }
}
