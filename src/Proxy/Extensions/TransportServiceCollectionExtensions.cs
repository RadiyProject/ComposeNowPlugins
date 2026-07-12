using ComposeNowPlugins.Proxy.Transports;
using ComposeNowPlugins.Proxy.Transports.WebSockets;

namespace ComposeNowPlugins.Proxy.Extensions;

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
