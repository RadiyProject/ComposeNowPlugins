using System.Buffers.Binary;
using System.Text.Json;
using ComposeNowPlugins.Application.Exceptions;
using ComposeNowPlugins.Application.Repositories.Plugins;
using ComposeNowPlugins.Application.Services.Processing;
using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;
using ComposeNowPlugins.Infrastructure.Cache;
using ComposeNowPlugins.Infrastructure.Services.Cluster;
using StackExchange.Redis;

namespace ComposeNowPlugins.Infrastructure.Repositories.Plugins;

public sealed class PluginProcessingCheckpointRepository(
    IConnectionMultiplexer redis
) : IPluginProcessingCheckpointRepository
{
    private static readonly TimeSpan CheckpointTtl = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _database = redis.GetDatabase();

    public async Task<PluginBlockProcessResult?> GetResultAsync(
        PluginId pluginId,
        ulong epoch,
        ulong seq
    )
    {
        RedisValue value = await _database.StringGetAsync(
            CacheKeys.PluginBlockResult(pluginId, epoch, seq)
        );

        return value.HasValue
            ? DeserializeResult((byte[])value!)
            : null;
    }

    public async Task CommitAsync(
        Plugin plugin,
        string workerId,
        ulong epoch,
        ulong seq,
        PluginBlockProcessResult result,
        IReadOnlyList<PluginEventDelivery> deliveries
    )
    {
        try
        {
            ITransaction transaction = _database.CreateTransaction();
            transaction.AddCondition(Condition.StringEqual(
                PluginBrokerKeys.PluginOwner(plugin.Id.GetValue()),
                workerId
            ));

            _ = transaction.StringSetAsync(
                CacheKeys.PluginState(plugin.Id),
                JsonSerializer.Serialize(plugin, JsonOptions),
                CheckpointTtl
            );
            _ = transaction.StringSetAsync(
                CacheKeys.PluginBlockResult(plugin.Id, epoch, seq),
                SerializeResult(result),
                CheckpointTtl
            );

            foreach (IGrouping<string, PluginEventDelivery> stream in deliveries.GroupBy(x => x.StreamKey))
            {
                RedisValue[] ids = stream.Select(x => (RedisValue)x.EntryId).ToArray();
                if (ids.Length > 0)
                {
                    _ = transaction.StreamAcknowledgeAsync(
                        stream.Key,
                        CacheKeys.PluginEventConsumerGroup,
                        ids
                    );
                }
            }

            bool committed = await transaction.ExecuteAsync();
            if (!committed)
            {
                throw new PluginOwnershipLostException(
                    $"Plugin ownership was lost before checkpoint commit. PluginId={plugin.Id}, WorkerId={workerId}"
                );
            }
        }
        catch (PluginOwnershipLostException)
        {
            throw;
        }
        catch (RepositoryException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RepositoryException(
                $"Failed to commit plugin processing checkpoint. PluginId={plugin.Id}, Epoch={epoch}, Seq={seq}",
                exception
            );
        }
    }

    private static byte[] SerializeResult(PluginBlockProcessResult result)
    {
        if (result.Audio.Length > AudioProcessingLimits.MaxSamplesPerBlock)
        {
            throw new RepositoryException("Plugin block checkpoint audio exceeds processing limits.");
        }

        int length = checked(1 + sizeof(int) + result.Audio.Length * sizeof(float));
        byte[] payload = new byte[length];
        payload[0] = result.ShouldSend ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1, sizeof(int)), result.Audio.Length);

        Span<byte> audioBytes = payload.AsSpan(1 + sizeof(int));
        ReadOnlySpan<float> audio = result.Audio.Span;
        for (int i = 0; i < audio.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                audioBytes.Slice(i * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(audio[i])
            );
        }

        return payload;
    }

    private static PluginBlockProcessResult DeserializeResult(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 1 + sizeof(int))
        {
            throw new RepositoryException("Plugin block checkpoint payload is malformed.");
        }

        bool shouldSend = payload[0] != 0;
        int samples = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(1, sizeof(int)));
        if (samples is < 0 or > AudioProcessingLimits.MaxSamplesPerBlock)
        {
            throw new RepositoryException("Plugin block checkpoint audio payload is malformed.");
        }

        int expectedLength = checked(1 + sizeof(int) + samples * sizeof(float));
        if (payload.Length != expectedLength)
        {
            throw new RepositoryException("Plugin block checkpoint audio payload is malformed.");
        }

        float[] audio = new float[samples];
        ReadOnlySpan<byte> audioBytes = payload[(1 + sizeof(int))..];
        for (int i = 0; i < samples; i++)
        {
            audio[i] = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(
                    audioBytes.Slice(i * sizeof(float), sizeof(float))
                )
            );
        }

        return new PluginBlockProcessResult(audio, shouldSend);
    }
}
