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

    private readonly IDatabase _database = redis.GetDatabase();
    private readonly IPluginNodeState _nodeState = nodeState;
    private readonly IPluginCatalog _pluginCatalog = pluginCatalog;
    private readonly ILogger<PluginNodeHeartbeatService> _logger = logger;

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
            await PublishAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Final plugin node heartbeat failed.");
        }

        await base.StopAsync(cancellationToken);
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
    }
}
