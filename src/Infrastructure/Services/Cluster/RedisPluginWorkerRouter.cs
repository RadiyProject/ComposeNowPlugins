using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ComposeNowPlugins.Application.Services.Processing;
using ComposeNowPlugins.Domain.Models.Ids;
using StackExchange.Redis;

namespace ComposeNowPlugins.Infrastructure.Services.Cluster;

public sealed class RedisPluginWorkerRouter(IConnectionMultiplexer redis) : IPluginWorkerRouter
{
    private static readonly TimeSpan OwnershipTtl = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string RefreshOwnershipScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        return 0
        """;

    private const string DeleteOwnershipScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    private readonly IDatabase _database = redis.GetDatabase();

    public async Task<PluginWorkerEndpoint> ResolveAsync(
        PluginId pluginId,
        IReadOnlySet<string>? excludedWorkerIds = null
    )
    {
        string ownershipKey = PluginBrokerKeys.PluginOwner(pluginId.GetValue());
        RedisValue currentOwner = await _database.StringGetAsync(ownershipKey);

        if (currentOwner.HasValue)
        {
            if (IsExcluded(currentOwner!, excludedWorkerIds))
            {
                await DeleteOwnershipAsync(ownershipKey, currentOwner!);
            }
            else
            {
                PluginWorkerEndpoint? currentEndpoint = await GetEndpointAsync(currentOwner!);
                if (currentEndpoint is not null)
                {
                    await RefreshOwnershipAsync(ownershipKey, currentOwner!);
                    return currentEndpoint;
                }

                await DeleteOwnershipAsync(ownershipKey, currentOwner!);
            }
        }

        IReadOnlyList<PluginWorkerEndpoint> workers = await GetActiveWorkersAsync(excludedWorkerIds);
        if (workers.Count == 0)
        {
            throw new InvalidOperationException("No active plugin workers are registered.");
        }

        foreach (PluginWorkerEndpoint candidate in OrderByRendezvousHash(pluginId, workers))
        {
            bool acquired = await _database.StringSetAsync(
                ownershipKey,
                candidate.WorkerId,
                OwnershipTtl,
                When.NotExists
            );

            if (acquired)
            {
                return candidate;
            }

            RedisValue owner = await _database.StringGetAsync(ownershipKey);
            if (!owner.HasValue || IsExcluded(owner!, excludedWorkerIds))
            {
                if (owner.HasValue)
                {
                    await DeleteOwnershipAsync(ownershipKey, owner!);
                }

                continue;
            }

            PluginWorkerEndpoint? endpoint = await GetEndpointAsync(owner!);
            if (endpoint is not null)
            {
                await RefreshOwnershipAsync(ownershipKey, owner!);
                return endpoint;
            }

            await DeleteOwnershipAsync(ownershipKey, owner!);
        }

        throw new InvalidOperationException("Failed to acquire plugin worker ownership.");
    }

    public Task InvalidateOwnershipAsync(PluginId pluginId, string workerId)
    {
        return DeleteOwnershipAsync(
            PluginBrokerKeys.PluginOwner(pluginId.GetValue()),
            workerId
        );
    }

    private async Task<IReadOnlyList<PluginWorkerEndpoint>> GetActiveWorkersAsync(
        IReadOnlySet<string>? excludedWorkerIds
    )
    {
        RedisValue[] workerIds = await _database.SetMembersAsync(PluginBrokerKeys.WorkersSet);
        List<PluginWorkerEndpoint> workers = new(workerIds.Length);

        foreach (RedisValue workerId in workerIds)
        {
            string id = workerId!;
            if (IsExcluded(id, excludedWorkerIds))
            {
                continue;
            }

            PluginWorkerEndpoint? endpoint = await GetEndpointAsync(id);
            if (endpoint is null)
            {
                await _database.SetRemoveAsync(PluginBrokerKeys.WorkersSet, workerId);
                continue;
            }

            workers.Add(endpoint);
        }

        return workers;
    }

    private async Task<PluginWorkerEndpoint?> GetEndpointAsync(string workerId)
    {
        RedisValue value = await _database.StringGetAsync(PluginBrokerKeys.Worker(workerId));
        if (!value.HasValue)
        {
            return null;
        }

        PluginWorkerSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<PluginWorkerSnapshot>(value!, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        return snapshot is not null &&
               string.Equals(snapshot.WorkerId, workerId, StringComparison.Ordinal) &&
               Uri.TryCreate(snapshot.Address, UriKind.Absolute, out Uri? address) &&
               (string.Equals(address.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            ? new PluginWorkerEndpoint(snapshot.WorkerId, address)
            : null;
    }

    private async Task RefreshOwnershipAsync(string key, string workerId)
    {
        await _database.ScriptEvaluateAsync(
            RefreshOwnershipScript,
            [(RedisKey)key],
            [(RedisValue)workerId, (RedisValue)(long)OwnershipTtl.TotalMilliseconds]
        );
    }

    private async Task DeleteOwnershipAsync(string key, string workerId)
    {
        await _database.ScriptEvaluateAsync(
            DeleteOwnershipScript,
            [(RedisKey)key],
            [(RedisValue)workerId]
        );
    }

    private static IEnumerable<PluginWorkerEndpoint> OrderByRendezvousHash(
        PluginId pluginId,
        IReadOnlyList<PluginWorkerEndpoint> workers
    )
    {
        return workers.OrderByDescending(worker => CalculateScore(pluginId.GetValue(), worker.WorkerId));
    }

    private static ulong CalculateScore(string pluginId, string workerId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{pluginId}\n{workerId}"));
        return BinaryPrimitives.ReadUInt64BigEndian(hash);
    }

    private static bool IsExcluded(string workerId, IReadOnlySet<string>? excludedWorkerIds)
    {
        return excludedWorkerIds?.Contains(workerId) == true;
    }
}
