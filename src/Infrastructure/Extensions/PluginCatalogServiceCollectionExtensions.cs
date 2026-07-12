using ComposeNowPlugins.Domain.Configurations;
using ComposeNowPlugins.Application.Services.Plugins;
using ComposeNowPlugins.Infrastructure.Services.Plugins;

namespace ComposeNowPlugins.Infrastructure.Extensions;

public static class PluginCatalogServiceCollectionExtensions
{
    public static IServiceCollection AddPluginCatalog(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<PluginCatalogOptions>(configuration);
        services.AddSingleton<IPluginCatalog, PluginCatalog>();

        return services;
    }
}