using ComposeNowPlugins.Configurations;
using ComposeNowPlugins.Services.Plugins;

namespace ComposeNowPlugins.Extensions;

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