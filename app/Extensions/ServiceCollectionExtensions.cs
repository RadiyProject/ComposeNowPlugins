namespace ComposeNowPlugins.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddControllers();

        services.AddPluginCatalog(configuration);
        services.AddRepositories();
        services.AddRedisCache();
        services.AddPluginClusterServices();
        services.AddRuntimeSessions();
        services.AddRealtimeTransports();
        services.AddVstProcessing();

        return services;
    }
}
