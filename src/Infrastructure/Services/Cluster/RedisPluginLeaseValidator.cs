using System.Text.Json;
using StackExchange.Redis;

namespace ComposeNowPlugins.Infrastructure.Services.Cluster;

public sealed class RedisPluginLeaseValidator(
    IConnectionMultiplexer redis,
    IPluginNodeState nodeState
) : IPluginLeaseValidator
{
    private const int MaxLeaseIdLength = 256;
    private const string ConsumeLeaseScript = """
        local value = redis.call('GET', KEYS[1])
        if not value or value ~= ARGV[1] then
            return 0
        end

        return redis.call('DEL', KEYS[1])
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDatabase _database = redis.GetDatabase();
    private readonly IPluginNodeState _nodeState = nodeState;
    private readonly bool _requireLease = ReadRequireLease();

    public async Task<bool> ValidateAsync(
        string leaseId,
        string pluginName,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_requireLease && string.IsNullOrWhiteSpace(leaseId))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(leaseId) || leaseId.Length > MaxLeaseIdLength)
        {
            return false;
        }

        RedisValue value = await _database.StringGetAsync(PluginBrokerKeys.Lease(leaseId));
        if (value.IsNullOrEmpty)
        {
            return false;
        }

        PluginNodeLease? lease;
        try
        {
            lease = JsonSerializer.Deserialize<PluginNodeLease>(
                value!,
                JsonOptions
            );
        }
        catch (JsonException)
        {
            return false;
        }

        bool valid = lease is not null
            && lease.NodeId == _nodeState.NodeId
            && string.Equals(lease.PluginName, pluginName, StringComparison.OrdinalIgnoreCase)
            && lease.ExpiresAt > DateTimeOffset.UtcNow;

        if (!valid)
        {
            return false;
        }

        RedisResult consumed = await _database.ScriptEvaluateAsync(
            ConsumeLeaseScript,
            [(RedisKey)PluginBrokerKeys.Lease(leaseId)],
            [(RedisValue)value]
        );

        return (long)consumed == 1;
    }

    private static bool ReadRequireLease()
    {
        string? raw = Environment.GetEnvironmentVariable("REQUIRE_PLUGIN_LEASE");
        return bool.TryParse(raw, out bool value) && value;
    }
}
