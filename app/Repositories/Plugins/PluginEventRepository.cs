using ComposeNowPlugins.Cache;
using ComposeNowPlugins.Models;
using ComposeNowPlugins.Models.Ids;

namespace ComposeNowPlugins.Repositories.Plugins;

public sealed class PluginEventRepository(
    ICache cache,
    ILogger<PluginEventRepository> logger
) : IPluginEventRepository
{
    private readonly ICache _cache = cache;
    private readonly ILogger<PluginEventRepository> _logger = logger;

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    public async Task<RepositoryActionStatus> AddBlockEventAsync(
        PluginId pluginId,
        ulong seq,
        PluginEvent pluginEvent
    )
    {
        try
        {
            await _cache.ListRightPushAsync(
                CacheKeys.PluginBlockEvents(pluginId, seq),
                pluginEvent,
                DefaultTtl
            );

            return RepositoryActionStatus.Success;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to append block event. PluginId={PluginId}, Seq={Seq}",
                pluginId,
                seq
            );

            return RepositoryActionStatus.Error;
        }
    }

    public async Task<RepositoryActionStatus> AddControlEventAsync(
        PluginId pluginId,
        PluginEvent pluginEvent
    )
    {
        try
        {
            await _cache.ListRightPushAsync(
                CacheKeys.PluginControlEvents(pluginId),
                pluginEvent,
                DefaultTtl
            );

            return RepositoryActionStatus.Success;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to append control event. PluginId={PluginId}",
                pluginId
            );

            return RepositoryActionStatus.Error;
        }
    }

    public async Task<IReadOnlyList<PluginEvent>> GetBlockEventsAsync(
        PluginId pluginId,
        ulong seq
    )
    {
        return await _cache.ListRangeAsync<PluginEvent>(
            CacheKeys.PluginBlockEvents(pluginId, seq)
        );
    }

    public async Task<IReadOnlyList<PluginEvent>> GetControlEventsAsync(
        PluginId pluginId
    )
    {
        return await _cache.ListRangeAsync<PluginEvent>(
            CacheKeys.PluginControlEvents(pluginId)
        );
    }

    public async Task DeleteBlockEventsAsync(
        PluginId pluginId,
        ulong seq
    )
    {
        await _cache.RemoveAsync(
            CacheKeys.PluginBlockEvents(pluginId, seq)
        );
    }

    public async Task DeleteControlEventsAsync(
        PluginId pluginId
    )
    {
        await _cache.RemoveAsync(
            CacheKeys.PluginControlEvents(pluginId)
        );
    }

    public async Task<IReadOnlyList<PluginEvent>> PopControlEventsAsync(
        PluginId pluginId
    )
    {
        string key = CacheKeys.PluginControlEvents(pluginId);

        long count = await _cache.ListLengthAsync(key);

        if (count <= 0)
        {
            return [];
        }

        return await _cache.ListLeftPopAsync<PluginEvent>(
            key,
            count
        );
    }
}