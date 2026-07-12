using ComposeNowPlugins.Cache;
using ComposeNowPlugins.Exceptions;
using ComposeNowPlugins.Models;
using ComposeNowPlugins.Models.Ids;

namespace ComposeNowPlugins.Repositories.Plugins;

public sealed class PluginEventRepository(ICache cache) : IPluginEventRepository
{
    private readonly ICache _cache = cache;

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    public async Task AddBlockEventAsync(
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
        }
        catch (Exception exception)
        {
            throw new RepositoryException(
                $"Failed to append block event. PluginId={pluginId}, Seq={seq}",
                exception
            );
        }
    }

    public async Task AddControlEventAsync(
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
        }
        catch (Exception exception)
        {
            throw new RepositoryException(
                $"Failed to append control event. PluginId={pluginId}",
                exception
            );
        }
    }

    public async Task<IReadOnlyList<PluginEvent>> GetBlockEventsAsync(
        PluginId pluginId,
        ulong seq
    )
    {
        try
        {
            return await _cache.ListRangeAsync<PluginEvent>(
                CacheKeys.PluginBlockEvents(pluginId, seq)
            );
        }
        catch (Exception exception)
        {
            throw new RepositoryException(
                $"Failed to get block events. PluginId={pluginId}, Seq={seq}",
                exception
            );
        }
    }

    public async Task<IReadOnlyList<PluginEvent>> GetControlEventsAsync(
        PluginId pluginId
    )
    {
        try
        {
            return await _cache.ListRangeAsync<PluginEvent>(
                CacheKeys.PluginControlEvents(pluginId)
            );
        }
        catch (Exception exception)
        {
            throw new RepositoryException(
                $"Failed to get control events. PluginId={pluginId}",
                exception
            );
        }
    }

    public async Task DeleteBlockEventsAsync(
        PluginId pluginId,
        ulong seq
    )
    {
        try
        {
            await _cache.RemoveAsync(
                CacheKeys.PluginBlockEvents(pluginId, seq)
            );
        }
        catch (Exception exception)
        {
            throw new RepositoryException(
                $"Failed to delete block events. PluginId={pluginId}, Seq={seq}",
                exception
            );
        }
    }

    public async Task DeleteControlEventsAsync(
        PluginId pluginId
    )
    {
        try
        {
            await _cache.RemoveAsync(
                CacheKeys.PluginControlEvents(pluginId)
            );
        }
        catch (Exception exception)
        {
            throw new RepositoryException(
                $"Failed to delete control events. PluginId={pluginId}",
                exception
            );
        }
    }

    public async Task<IReadOnlyList<PluginEvent>> PopControlEventsAsync(
        PluginId pluginId
    )
    {
        try
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
        catch (Exception exception)
        {
            throw new RepositoryException(
                $"Failed to pop control events. PluginId={pluginId}",
                exception
            );
        }
    }
}
