namespace ComposeNowPlugins.Services.Cluster;

public sealed class PluginNodeState(IHostApplicationLifetime lifetime) : IPluginNodeState
{
    private const string DrainFile = "/tmp/compose-now-draining";
    private readonly IHostApplicationLifetime _lifetime = lifetime;
    private int _activeSessions;

    public string NodeId { get; } = ReadNodeId();
    public string WebSocketUrl { get; } = ReadWebSocketUrl();
    public int ActiveSessions => Volatile.Read(ref _activeSessions);
    public int MaxSessions { get; } = ReadPositiveInt("COMPOSE_NOW_PLUGIN_MAX_THREADS_PER_NODE", 4);
    public double BusyThresholdPercent { get; } = ReadPositiveDouble("COMPOSE_NOW_PLUGIN_BUSY_THRESHOLD_PERCENT", 90);
    public double LoadPercent => MaxSessions <= 0
        ? 100
        : Math.Min(100, ActiveSessions * 100d / MaxSessions);

    public bool IsDraining =>
        _lifetime.ApplicationStopping.IsCancellationRequested
        || File.Exists(DrainFile);

    public bool TryAcquireSession()
    {
        while (true)
        {
            if (IsDraining)
            {
                return false;
            }

            int current = Volatile.Read(ref _activeSessions);
            double projectedLoadPercent = MaxSessions <= 0
                ? 100
                : (current + 1) * 100d / MaxSessions;

            if (current >= MaxSessions || projectedLoadPercent >= BusyThresholdPercent)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _activeSessions, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    public void ReleaseSession()
    {
        int value = Interlocked.Decrement(ref _activeSessions);
        if (value < 0)
        {
            Interlocked.Exchange(ref _activeSessions, 0);
        }
    }

    private static string ReadNodeId()
    {
        return Environment.GetEnvironmentVariable("PLUGIN_NODE_ID")
            ?? Environment.GetEnvironmentVariable("HOSTNAME")
            ?? Guid.NewGuid().ToString("N");
    }

    private static string ReadWebSocketUrl()
    {
        string? explicitUrl = Environment.GetEnvironmentVariable("PLUGIN_NODE_PUBLIC_WS_URL");
        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            return explicitUrl;
        }

        string host = Environment.GetEnvironmentVariable("POD_IP")
            ?? Environment.GetEnvironmentVariable("HOSTNAME")
            ?? "plugins";

        return $"ws://{host}:5001/ws";
    }

    private static int ReadPositiveInt(string name, int fallback)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out int value) && value > 0
            ? value
            : fallback;
    }

    private static double ReadPositiveDouble(string name, double fallback)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        return double.TryParse(raw, out double value) && value > 0
            ? value
            : fallback;
    }
}
