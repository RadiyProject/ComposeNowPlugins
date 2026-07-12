namespace ComposeNowPlugins.Infrastructure.Extensions;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddPluginCatalog(configuration);
        services.AddRepositories();
        services.AddRedisCache();
        services.AddPluginClusterServices();

        return services;
    }
}
