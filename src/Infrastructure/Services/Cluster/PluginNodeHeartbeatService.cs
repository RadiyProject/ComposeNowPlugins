using System.Text.Json;
using ComposeNowPlugins.Application.Services.Plugins;
using StackExchange.Redis;

namespace ComposeNowPlugins.Infrastructure.Services.Cluster;

public sealed class PluginNodeHeartbeatService(
    IConnectionMultiplexer redis,
    IPluginNodeState nodeState,
    IPluginCatalog pluginCatalog,
    ILogger<PluginNodeHeartbeatService> logger
) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HeartbeatTtl = TimeSpan.FromSeconds(20);
    private const int CleanupIntervalHeartbeats = 12;

    private readonly IDatabase _database = redis.GetDatabase();
    private readonly IPluginNodeState _nodeState = nodeState;
    private readonly IPluginCatalog _pluginCatalog = pluginCatalog;
    private readonly ILogger<PluginNodeHeartbeatService> _logger = logger;
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
                _logger.LogWarning(exception, "Plugin node heartbeat failed.");
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
                await _database.KeyDeleteAsync(PluginBrokerKeys.Node(_nodeState.NodeId));
                await _database.SetRemoveAsync(PluginBrokerKeys.NodesSet, _nodeState.NodeId);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Plugin node deregistration failed. NodeId={NodeId}", _nodeState.NodeId);
            }
        }
    }

    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = new PluginNodeSnapshot(
            _nodeState.NodeId,
            _nodeState.WebSocketUrl,
            [.. _pluginCatalog.GetAvailable().Select(plugin => plugin.Name)],
            _nodeState.ActiveSessions,
            _nodeState.LoadPercent,
            _nodeState.IsDraining,
            DateTimeOffset.UtcNow
        );

        string serialized = JsonSerializer.Serialize(snapshot, JsonOptions);
        await _database.StringSetAsync(
            PluginBrokerKeys.Node(_nodeState.NodeId),
            serialized,
            HeartbeatTtl
        );
        await _database.SetAddAsync(PluginBrokerKeys.NodesSet, _nodeState.NodeId);

        _heartbeatsUntilCleanup--;
        if (_heartbeatsUntilCleanup <= 0)
        {
            _heartbeatsUntilCleanup = CleanupIntervalHeartbeats;
            await RemoveStaleNodesAsync();
        }
    }

    private async Task RemoveStaleNodesAsync()
    {
        RedisValue[] nodeIds = await _database.SetMembersAsync(PluginBrokerKeys.NodesSet);
        foreach (RedisValue nodeId in nodeIds)
        {
            string id = nodeId!;
            if (!await _database.KeyExistsAsync(PluginBrokerKeys.Node(id)))
            {
                await _database.SetRemoveAsync(PluginBrokerKeys.NodesSet, nodeId);
            }
        }
    }
}
