using System.Text.Json;
using ComposeNowPlugins.Application.Services.Processing;
using StackExchange.Redis;

namespace ComposeNowPlugins.Infrastructure.Services.Cluster;

public sealed class PluginWorkerHeartbeatService(
    IConnectionMultiplexer redis,
    IPluginWorkerIdentity workerIdentity,
    ILogger<PluginWorkerHeartbeatService> logger
) : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HeartbeatTtl = TimeSpan.FromSeconds(12);
    private const int CleanupIntervalHeartbeats = 20;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _database = redis.GetDatabase();
    private readonly ILogger<PluginWorkerHeartbeatService> _logger = logger;
    private readonly IPluginWorkerIdentity _workerIdentity = workerIdentity;
    private int _heartbeatsUntilCleanup = CleanupIntervalHeartbeats;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Plugin worker heartbeat failed. WorkerId={WorkerId}", _workerIdentity.WorkerId);
            }

            await Task.Delay(HeartbeatInterval, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.StopAsync(cancellationToken);
        }
        finally
        {
            try
            {
                await _database.KeyDeleteAsync(PluginBrokerKeys.Worker(_workerIdentity.WorkerId));
                await _database.SetRemoveAsync(PluginBrokerKeys.WorkersSet, _workerIdentity.WorkerId);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Plugin worker deregistration failed. WorkerId={WorkerId}", _workerIdentity.WorkerId);
            }
        }
    }

    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string value = JsonSerializer.Serialize(
            new PluginWorkerSnapshot(
                _workerIdentity.WorkerId,
                _workerIdentity.Address.AbsoluteUri,
                DateTimeOffset.UtcNow
            ),
            JsonOptions
        );

        await _database.StringSetAsync(
            PluginBrokerKeys.Worker(_workerIdentity.WorkerId),
            value,
            HeartbeatTtl
        );
        await _database.SetAddAsync(PluginBrokerKeys.WorkersSet, _workerIdentity.WorkerId);

        _heartbeatsUntilCleanup--;
        if (_heartbeatsUntilCleanup <= 0)
        {
            _heartbeatsUntilCleanup = CleanupIntervalHeartbeats;
            await RemoveStaleWorkersAsync();
        }
    }

    private async Task RemoveStaleWorkersAsync()
    {
        RedisValue[] workerIds = await _database.SetMembersAsync(PluginBrokerKeys.WorkersSet);
        foreach (RedisValue workerId in workerIds)
        {
            string id = workerId!;
            if (!await _database.KeyExistsAsync(PluginBrokerKeys.Worker(id)))
            {
                await _database.SetRemoveAsync(PluginBrokerKeys.WorkersSet, workerId);
            }
        }
    }
}
