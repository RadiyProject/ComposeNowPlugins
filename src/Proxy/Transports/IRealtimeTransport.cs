namespace ComposeNowPlugins.Proxy.Transports;

public interface IRealtimeTransport
{
    Task ConnectAsync(HttpContext context, CancellationToken cancellationToken);
}