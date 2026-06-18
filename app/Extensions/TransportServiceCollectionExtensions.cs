using ComposeNowPlugins.Transports;
using ComposeNowPlugins.Transports.WebSockets;

namespace ComposeNowPlugins.Extensions;

public static class TransportServiceCollectionExtensions
{
    public static IServiceCollection AddRealtimeTransports(
        this IServiceCollection services
    )
    {
        services.AddScoped<IRealtimeTransport, WebSocketTransport>();

        return services;
    }
}
