namespace ComposeNowPlugins.Proxy.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        return services.AddProxyApplication(configuration);
    }
}
