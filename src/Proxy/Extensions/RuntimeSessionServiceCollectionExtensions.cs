using ComposeNowPlugins.Application.Services.Plugins;
using ComposeNowPlugins.Application.Services.Processing;
using ComposeNowPlugins.Domain.Factories;
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
        services.AddScoped<PluginFactory>();
        services.AddScoped<PluginEventFactory>();
        services.AddScoped<IPluginSessionService, PluginSessionService>();
        services.AddScoped<IPluginEventService, PluginEventService>();
        services.AddScoped<IAudioSessionCoordinator, AudioSessionCoordinator>();

        return services;
    }
}
