using ComposeNowPlugins.Proxy.Transports;

namespace ComposeNowPlugins.Proxy.Extensions;

public static class RuntimeSessionServiceCollectionExtensions
{
    public static IServiceCollection AddRuntimeSessions(
        this IServiceCollection services
    )
    {
        services.AddScoped<IRuntimeSessionFactory, RuntimeSessionFactory>();
        services.AddScoped<EchoRuntimeSession>();

        return services;
    }
}