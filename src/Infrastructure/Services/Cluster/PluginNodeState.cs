using System.Diagnostics;

namespace ComposeNowPlugins.Infrastructure.Services.Cluster;

public sealed class PluginNodeState(IHostApplicationLifetime lifetime) : IPluginNodeState
{
    private const string DrainFile = "/tmp/compose-now-draining";
    private static readonly TimeSpan LoadSampleInterval = TimeSpan.FromMilliseconds(500);

    private readonly IHostApplicationLifetime _lifetime = lifetime;
    private readonly object _loadLock = new();
    private readonly Process _process = Process.GetCurrentProcess();
    private DateTimeOffset _lastLoadSampleAt = DateTimeOffset.UtcNow;
    private TimeSpan _lastProcessorTime = Process.GetCurrentProcess().TotalProcessorTime;
    private double _loadPercent;
    private int _activeSessions;

    public string NodeId { get; } = ReadNodeId();
    public string WebSocketUrl { get; } = ReadWebSocketUrl();
    public int ActiveSessions => Volatile.Read(ref _activeSessions);
    public double BusyThresholdPercent { get; } = ReadPositiveDouble("COMPOSE_NOW_PLUGIN_BUSY_THRESHOLD_PERCENT", 90);
    public double LoadPercent => ReadProcessCpuLoadPercent();

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

            if (LoadPercent >= BusyThresholdPercent)
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

    private double ReadProcessCpuLoadPercent()
    {
        lock (_loadLock)
        {
            var now = DateTimeOffset.UtcNow;
            TimeSpan elapsed = now - _lastLoadSampleAt;
            if (elapsed < LoadSampleInterval)
            {
                return _loadPercent;
            }

            TimeSpan processorTime = _process.TotalProcessorTime;
            TimeSpan processorElapsed = processorTime - _lastProcessorTime;
            double processorCount = Math.Max(1, Environment.ProcessorCount);
            double loadPercent = processorElapsed.TotalMilliseconds / elapsed.TotalMilliseconds / processorCount * 100d;

            _lastLoadSampleAt = now;
            _lastProcessorTime = processorTime;
            _loadPercent = Math.Clamp(loadPercent, 0d, 100d);

            return _loadPercent;
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

    private static double ReadPositiveDouble(string name, double fallback)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        return double.TryParse(raw, out double value) && value > 0
            ? value
            : fallback;
    }
}
