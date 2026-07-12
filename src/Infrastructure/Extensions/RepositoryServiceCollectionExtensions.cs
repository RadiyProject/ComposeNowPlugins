using ComposeNowPlugins.Application.Repositories.Plugins;
using ComposeNowPlugins.Infrastructure.Repositories.Plugins;

namespace ComposeNowPlugins.Infrastructure.Extensions;

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