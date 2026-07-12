namespace ComposeNowPlugins.Proxy.Transports;

public interface IRuntimeSession
{
    Task RunAsync(IRuntimeChannel channel, CancellationToken ct);
}