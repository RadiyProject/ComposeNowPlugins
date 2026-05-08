using ComposeNowPlugins.Transports;

namespace ComposeNowPlugins.Extensions;

public static class RuntimeSessionServiceCollectionExtensions
{
    public static IServiceCollection AddRuntimeSessions(
        this IServiceCollection services
    )
    {
        services.AddScoped<IRuntimeSessionFactory, RuntimeSessionFactory>();
        services.AddScoped<EchoRuntimeSession>();
        services.AddScoped<AudioRuntimeSession>();

        return services;
    }
}