using ComposeNowPlugins.Application.Services.Plugins;
using ComposeNowPlugins.Application.Services.Processing;
using ComposeNowPlugins.Worker.Configurations;

namespace ComposeNowPlugins.Worker.Wrappers;

public sealed class VstEnginePool(
    IPluginCatalog pluginCatalog,
    ILoggerFactory loggerFactory,
    ILogger<VstEnginePool> logger,
    IConfiguration configuration,
    VstEnginePoolOptions options
) : IPluginEnginePool
{
    private readonly object _sync = new();
    private readonly Dictionary<string, PluginPool> _pools = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource<bool> _availability = CreateAvailabilitySignal();
    private readonly IPluginCatalog _pluginCatalog = pluginCatalog;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly ILogger<VstEnginePool> _logger = logger;
    private readonly IConfiguration _configuration = configuration;
    private readonly VstEnginePoolOptions _options = options;
    private int _totalInstances;
    private bool _disposed;

    public async ValueTask<IPluginEngineLease> AcquireAsync(
        string pluginName,
        int sampleRate,
        int blockSize,
        int channels,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            VstEngine? availableEngine = null;
            VstEngine? retiredEngine = null;
            bool createReserved = false;
            Task? availabilityTask = null;

            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                PluginPool pool = GetOrCreatePool(pluginName);
                if (pool.IdleEngines.Count > 0)
                {
                    int index = pool.IdleEngines.Count - 1;
                    availableEngine = pool.IdleEngines[index].Engine;
                    pool.IdleEngines.RemoveAt(index);
                }
                else if (CanCreate(pool))
                {
                    ReserveInstance(pool);
                    createReserved = true;
                }
                else if (pool.TotalInstances < _options.MaxInstancesPerPlugin &&
                         TryRetireOldestIdleEngine(pluginName, out retiredEngine))
                {
                    ReserveInstance(pool);
                    createReserved = true;
                }
                else
                {
                    availabilityTask = _availability.Task;
                }
            }

            if (availableEngine is not null)
            {
                return CreateLease(pluginName, availableEngine);
            }

            if (createReserved)
            {
                if (retiredEngine is not null)
                {
                    try
                    {
                        await retiredEngine.DisposeAsync();
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(exception, "Retired VST engine disposal failed.");
                    }
                }

                VstEngine engine;
                try
                {
                    engine = CreateEngine(pluginName, sampleRate, blockSize, channels);
                }
                catch
                {
                    CancelReservation(pluginName);
                    throw;
                }

                lock (_sync)
                {
                    if (!_disposed)
                    {
                        return CreateLease(pluginName, engine);
                    }
                }

                await engine.DisposeAsync();
                throw new ObjectDisposedException(nameof(VstEnginePool));
            }

            await availabilityTask!.WaitAsync(cancellationToken);
        }
    }

    public async ValueTask EvictIdleAsync(TimeSpan idleTimeout)
    {
        DateTimeOffset threshold = DateTimeOffset.UtcNow - idleTimeout;
        List<VstEngine> enginesToDispose = [];

        lock (_sync)
        {
            foreach ((_, PluginPool pool) in _pools)
            {
                for (int index = pool.IdleEngines.Count - 1; index >= 0; index--)
                {
                    IdleEngine idleEngine = pool.IdleEngines[index];
                    if (idleEngine.ReturnedAt > threshold)
                    {
                        continue;
                    }

                    pool.IdleEngines.RemoveAt(index);
                    pool.TotalInstances--;
                    _totalInstances--;
                    enginesToDispose.Add(idleEngine.Engine);
                }
            }

            RemoveEmptyPools();
        }

        await DisposeEnginesAsync(enginesToDispose, "idle eviction");

        if (enginesToDispose.Count > 0)
        {
            SignalAvailability();
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<VstEngine> idleEngines;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            idleEngines = _pools.Values
                .SelectMany(pool => pool.IdleEngines)
                .Select(idleEngine => idleEngine.Engine)
                .ToList();
            _pools.Clear();
            _totalInstances = 0;
        }

        SignalAvailability();

        await DisposeEnginesAsync(idleEngines, "pool shutdown");
    }

    private IPluginEngineLease CreateLease(string pluginName, VstEngine engine)
    {
        return new VstEngineLease(
            engine,
            () => ReturnAsync(pluginName, engine)
        );
    }

    private async ValueTask ReturnAsync(string pluginName, VstEngine engine)
    {
        bool dispose;

        lock (_sync)
        {
            dispose = _disposed;
            if (!dispose)
            {
                PluginPool pool = GetOrCreatePool(pluginName);
                pool.IdleEngines.Add(new IdleEngine(engine, DateTimeOffset.UtcNow));
            }
        }

        if (dispose)
        {
            await DisposeEnginesAsync([engine], "lease return after pool shutdown");
            return;
        }

        SignalAvailability();
    }

    private bool CanCreate(PluginPool pool)
    {
        return pool.TotalInstances < _options.MaxInstancesPerPlugin &&
               _totalInstances < _options.MaxTotalInstances;
    }

    private void ReserveInstance(PluginPool pool)
    {
        pool.TotalInstances++;
        _totalInstances++;
    }

    private void CancelReservation(string pluginName)
    {
        lock (_sync)
        {
            if (_pools.TryGetValue(pluginName, out PluginPool? pool))
            {
                pool.TotalInstances--;
                _totalInstances--;
                if (pool.TotalInstances == 0)
                {
                    _pools.Remove(pluginName);
                }
            }
        }

        SignalAvailability();
    }

    private bool TryRetireOldestIdleEngine(
        string requestedPluginName,
        out VstEngine? engine
    )
    {
        string? selectedPoolName = null;
        int selectedIndex = -1;
        DateTimeOffset oldestReturnedAt = DateTimeOffset.MaxValue;

        foreach ((string poolName, PluginPool pool) in _pools)
        {
            if (string.Equals(poolName, requestedPluginName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (int index = 0; index < pool.IdleEngines.Count; index++)
            {
                if (pool.IdleEngines[index].ReturnedAt >= oldestReturnedAt)
                {
                    continue;
                }

                selectedPoolName = poolName;
                selectedIndex = index;
                oldestReturnedAt = pool.IdleEngines[index].ReturnedAt;
            }
        }

        if (selectedPoolName is null)
        {
            engine = null;
            return false;
        }

        PluginPool selectedPool = _pools[selectedPoolName];
        engine = selectedPool.IdleEngines[selectedIndex].Engine;
        selectedPool.IdleEngines.RemoveAt(selectedIndex);
        selectedPool.TotalInstances--;
        _totalInstances--;

        if (selectedPool.TotalInstances == 0)
        {
            _pools.Remove(selectedPoolName);
        }

        return true;
    }

    private PluginPool GetOrCreatePool(string pluginName)
    {
        if (!_pools.TryGetValue(pluginName, out PluginPool? pool))
        {
            pool = new PluginPool();
            _pools.Add(pluginName, pool);
        }

        return pool;
    }

    private void RemoveEmptyPools()
    {
        string[] emptyPools = _pools
            .Where(pair => pair.Value.TotalInstances == 0)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (string poolName in emptyPools)
        {
            _pools.Remove(poolName);
        }
    }

    private void SignalAvailability()
    {
        TaskCompletionSource<bool> availability;

        lock (_sync)
        {
            availability = _availability;
            _availability = CreateAvailabilitySignal();
        }

        availability.TrySetResult(true);
    }

    private async ValueTask DisposeEnginesAsync(
        IEnumerable<VstEngine> engines,
        string reason
    )
    {
        foreach (VstEngine engine in engines)
        {
            try
            {
                await engine.DisposeAsync();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "VST engine disposal failed. Reason={Reason}", reason);
            }
        }
    }

    private static TaskCompletionSource<bool> CreateAvailabilitySignal()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private VstEngine CreateEngine(
        string pluginName,
        int sampleRate,
        int blockSize,
        int channels
    )
    {
        var descriptor = _pluginCatalog.GetRequired(pluginName)
            ?? throw new InvalidOperationException(
                $"Plugin with name '{pluginName}' is not registered or disabled."
            );

        string vstPath = _configuration["VST3_PATH"]
            ?? throw new InvalidOperationException("VST3_PATH configuration is missing.");

        return new VstEngine(
            Path.Combine(vstPath, descriptor.PluginPath),
            _loggerFactory.CreateLogger<VstEngine>(),
            sampleRate,
            blockSize,
            channels,
            _options.MaxStateBytes
        );
    }

    private sealed class PluginPool
    {
        public List<IdleEngine> IdleEngines { get; } = [];
        public int TotalInstances { get; set; }
    }

    private sealed record IdleEngine(VstEngine Engine, DateTimeOffset ReturnedAt);
}
