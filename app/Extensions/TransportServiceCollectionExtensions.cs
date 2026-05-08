using ComposeNowPlugins.Transports;
using ComposeNowPlugins.Transports.WebSockets;

namespace ComposeNowPlugins.Extensions;

public static class TransportServiceCollectionExtensions
{
    public static IServiceCollection AddRealtimeTransports(
        this IServiceCollection services
    )
    {
        services.AddScoped<WebSocketTransport>();

        services.AddScoped<IRealtimeTransport>(serviceProvider =>
            new LoggingTransport(
                serviceProvider.GetRequiredService<WebSocketTransport>(),
                serviceProvider.GetRequiredService<ILogger<LoggingTransport>>()
            )
        );

        return services;
    }
}