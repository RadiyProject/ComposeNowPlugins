using ComposeNowPlugins.Repositories.Plugins;

namespace ComposeNowPlugins.Extensions;

public static class RepositoryServiceCollectionExtensions
{
    public static IServiceCollection AddRepositories(
        this IServiceCollection services
    )
    {
        services.AddScoped<IPluginRepository, PluginRepository>();
        services.AddScoped<IPluginEventRepository, PluginEventRepository>();

        return services;
    }
}