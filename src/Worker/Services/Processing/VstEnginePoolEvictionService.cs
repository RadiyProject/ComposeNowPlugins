using ComposeNowPlugins.Application.Services.Processing;
using ComposeNowPlugins.Worker.Configurations;

namespace ComposeNowPlugins.Worker.Services.Processing;

public sealed class VstEnginePoolEvictionService(
    IPluginEnginePool enginePool,
    VstEnginePoolOptions options,
    ILogger<VstEnginePoolEvictionService> logger
) : BackgroundService
{
    private readonly IPluginEnginePool _enginePool = enginePool;
    private readonly VstEnginePoolOptions _options = options;
    private readonly ILogger<VstEnginePoolEvictionService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(_options.SweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _enginePool.EvictIdleAsync(_options.IdleTimeout);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Idle VST engine eviction failed.");
            }
        }
    }
}
