using System.Text.Json;
using ComposeNowPlugins.Application.Exceptions;
using ComposeNowPlugins.Application.Repositories.Plugins;
using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;
using ComposeNowPlugins.Infrastructure.Cache;
using StackExchange.Redis;

namespace ComposeNowPlugins.Infrastructure.Repositories.Plugins;

public sealed class PluginEventRepository(IConnectionMultiplexer redis) : IPluginEventRepository
{
    private const int MaxStreamLength = 4096;
    private const int MaxReadCount = MaxStreamLength;
    private const string PayloadField = "payload";

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ClaimIdleTime = TimeSpan.FromSeconds(20);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string AddWithExpiryScript = """
        local id = redis.call('XADD', KEYS[1], 'MAXLEN', '~', ARGV[2], '*', 'payload', ARGV[1])
        redis.call('PEXPIRE', KEYS[1], ARGV[3])
        return id
        """;

    private readonly IDatabase _database = redis.GetDatabase();

    public Task AddBlockEventAsync(
        PluginId pluginId,
        ulong epoch,
        ulong seq,
        PluginEvent pluginEvent
    )
    {
        return AddAsync(CacheKeys.PluginBlockEvents(pluginId, epoch, seq), pluginEvent);
    }

    public Task AddControlEventAsync(PluginId pluginId, PluginEvent pluginEvent)
    {
        return AddAsync(CacheKeys.PluginControlEvents(pluginId), pluginEvent);
    }

    public Task<IReadOnlyList<PluginEventDelivery>> ReadBlockEventsAsync(
        PluginId pluginId,
        ulong epoch,
        ulong seq,
        string consumerId
    )
    {
        return ReadAsync(CacheKeys.PluginBlockEvents(pluginId, epoch, seq), consumerId);
    }

    public Task<IReadOnlyList<PluginEventDelivery>> ReadControlEventsAsync(
        PluginId pluginId,
        string consumerId
    )
    {
        return ReadAsync(CacheKeys.PluginControlEvents(pluginId), consumerId);
    }

    private async Task AddAsync(string streamKey, PluginEvent pluginEvent)
    {
        try
        {
            string payload = JsonSerializer.Serialize(pluginEvent, JsonOptions);
            await _database.ScriptEvaluateAsync(
                AddWithExpiryScript,
                [(RedisKey)streamKey],
                [
                    (RedisValue)payload,
                    (RedisValue)MaxStreamLength,
                    (RedisValue)(long)DefaultTtl.TotalMilliseconds
                ]
            );
        }
        catch (Exception exception)
        {
            throw new RepositoryException($"Failed to append plugin event. Stream={streamKey}", exception);
        }
    }

    private async Task<IReadOnlyList<PluginEventDelivery>> ReadAsync(
        string streamKey,
        string consumerId
    )
    {
        try
        {
            await EnsureGroupAsync(streamKey);

            StreamAutoClaimResult claimed = await _database.StreamAutoClaimAsync(
                streamKey,
                CacheKeys.PluginEventConsumerGroup,
                consumerId,
                (long)ClaimIdleTime.TotalMilliseconds,
                "0-0",
                MaxReadCount
            );

            StreamEntry[] pending = await _database.StreamReadGroupAsync(
                streamKey,
                CacheKeys.PluginEventConsumerGroup,
                consumerId,
                "0-0",
                MaxReadCount
            );

            StreamEntry[] fresh = await _database.StreamReadGroupAsync(
                streamKey,
                CacheKeys.PluginEventConsumerGroup,
                consumerId,
                ">",
                MaxReadCount
            );

            return Deserialize(
                streamKey,
                claimed.ClaimedEntries
                    .Concat(pending)
                    .Concat(fresh)
                    .DistinctBy(entry => entry.Id)
            );
        }
        catch (Exception exception)
        {
            throw new RepositoryException($"Failed to read plugin event stream. Stream={streamKey}", exception);
        }
    }

    private async Task EnsureGroupAsync(string streamKey)
    {
        try
        {
            await _database.StreamCreateConsumerGroupAsync(
                streamKey,
                CacheKeys.PluginEventConsumerGroup,
                "0-0",
                createStream: true
            );
        }
        catch (RedisServerException exception) when (exception.Message.StartsWith("BUSYGROUP", StringComparison.Ordinal))
        {
        }
    }

    private static IReadOnlyList<PluginEventDelivery> Deserialize(
        string streamKey,
        IEnumerable<StreamEntry> entries
    )
    {
        List<PluginEventDelivery> deliveries = [];

        foreach (StreamEntry entry in entries.Take(MaxReadCount))
        {
            NameValueEntry payload = entry.Values.FirstOrDefault(value => value.Name == PayloadField);
            if (!payload.Value.HasValue)
            {
                continue;
            }

            PluginEvent? pluginEvent = JsonSerializer.Deserialize<PluginEvent>(payload.Value!, JsonOptions);
            if (pluginEvent is not null)
            {
                deliveries.Add(new PluginEventDelivery(streamKey, entry.Id.ToString(), pluginEvent));
            }
        }

        return deliveries;
    }
}
