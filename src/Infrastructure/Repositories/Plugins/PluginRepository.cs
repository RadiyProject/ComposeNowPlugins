using ComposeNowPlugins.Infrastructure.Cache;
using ComposeNowPlugins.Application.Exceptions;
using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Infrastructure.Repositories.Plugins;

public class PluginRepository(ICache cache) : IPluginRepository
{
    private readonly ICache _cache = cache;

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    public async Task<Plugin?> GetAsync(PluginId key)
    {
        try
        {
            return await _cache.GetAsync<Plugin>(CacheKeys.PluginState(key));
        }
        catch (Exception exception)
        {
            throw new RepositoryException(
                $"Failed to get plugin state. PluginId={key}",
                exception
            );
        }
    }

    public async Task AddAsync(Plugin model)
    {
        try
        {
            string cacheKey = CacheKeys.PluginState(model.Id);
            bool exists = await _cache.ExistsAsync(cacheKey);

            if (exists)
            {
                throw new EntityAlreadyExistsException(
                    $"Plugin state already exists. PluginId={model.Id}"
                );
            }

            await _cache.SetAsync(
                cacheKey,
                model,
                DefaultTtl
            );
        }
        catch (RepositoryException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RepositoryException(
                $"Failed to add plugin state. PluginId={model.Id}",
                exception
            );
        }
    }

    public async Task UpdateAsync(PluginId key, Plugin model)
    {
        try
        {
            string cacheKey = CacheKeys.PluginState(key);
            await _cache.SetAsync(
                cacheKey,
                model,
                DefaultTtl
            );
        }
        catch (Exception exception)
        {
            throw new RepositoryException(
                $"Failed to update plugin state. PluginId={key}",
                exception
            );
        }
    }

    public async Task RefreshTtlAsync(PluginId key)
    {
        try
        {
            bool refreshed = await _cache.RefreshAsync(
                CacheKeys.PluginState(key),
                DefaultTtl
            );

            if (!refreshed)
            {
                throw new EntityNotFoundException(
                    $"Plugin state TTL was not refreshed because key does not exist. PluginId={key}"
                );
            }
        }
        catch (RepositoryException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RepositoryException(
                $"Failed to refresh plugin state TTL. PluginId={key}",
                exception
            );
        }
    }

    public async Task DeleteAsync(PluginId key)
    {
        try
        {
            string cacheKey = CacheKeys.PluginState(key);
            await _cache.RemoveAsync(cacheKey);
        }
        catch (Exception exception)
        {
            throw new RepositoryException(
                $"Failed to delete plugin state. PluginId={key}",
                exception
            );
        }
    }
}
