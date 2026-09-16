using System.Net;
using System.Net.Sockets;
using ComposeNowPlugins.Application.Services.Processing;

namespace ComposeNowPlugins.Infrastructure.Services.Cluster;

public sealed class PluginWorkerIdentity : IPluginWorkerIdentity
{
    public string WorkerId { get; } = ReadWorkerId();
    public Uri Address { get; } = ReadWorkerAddress();

    private static string ReadWorkerId()
    {
        return Environment.GetEnvironmentVariable("PLUGIN_WORKER_ID")
            ?? Environment.GetEnvironmentVariable("HOSTNAME")
            ?? Guid.NewGuid().ToString("N");
    }

    private static Uri ReadWorkerAddress()
    {
        string? configured = Environment.GetEnvironmentVariable("PLUGIN_WORKER_PUBLIC_ADDRESS");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return new Uri(configured, UriKind.Absolute);
        }

        string host = Environment.GetEnvironmentVariable("POD_IP")
            ?? ResolveLocalIpv4Address()
            ?? Environment.GetEnvironmentVariable("HOSTNAME")
            ?? "plugin-worker";

        return new Uri($"http://{host}:5002", UriKind.Absolute);
    }

    private static string? ResolveLocalIpv4Address()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .FirstOrDefault(address =>
                    address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address)
                )
                ?.ToString();
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
