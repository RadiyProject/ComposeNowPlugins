using ComposeNowPlugins.Cache;
using ComposeNowPlugins.Models;
using ComposeNowPlugins.Models.Ids;

namespace ComposeNowPlugins.Repositories.Plugins;

public class PluginRepository(ICache cache, ILogger<PluginRepository> logger) : IPluginRepository
{
    private readonly ICache _cache = cache;
    private readonly ILogger<PluginRepository> _logger = logger;

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    public async Task<Plugin?> GetAsync(PluginId key)
    {
        return await _cache.GetAsync<Plugin>(CacheKeys.PluginState(key));
    }

    public async Task<RepositoryActionStatus> AddAsync(Plugin model)
    {
        try
        {
            string cacheKey = CacheKeys.PluginState(model.Id);
            bool exists = await _cache.ExistsAsync(cacheKey);

            if (exists)
            {
                return RepositoryActionStatus.Error;
            }

            await _cache.SetAsync(
                cacheKey,
                model,
                DefaultTtl
            );

            return RepositoryActionStatus.Success;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to add plugin state. PluginId={PluginId}",
                model.Id
            );

            return RepositoryActionStatus.Error;
        }
    }

    public async Task<RepositoryActionStatus> UpdateAsync(PluginId key, Plugin model)
    {
        try
        {
            string cacheKey = CacheKeys.PluginState(model.Id);
            await _cache.SetAsync(
                cacheKey,
                model,
                DefaultTtl
            );

            return RepositoryActionStatus.Success;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to update plugin state. PluginId={PluginId}",
                key
            );

            return RepositoryActionStatus.Error;
        }
    }

    public async Task<RepositoryActionStatus> DeleteAsync(PluginId key)
    {
        try
        {
            string cacheKey = CacheKeys.PluginState(key);
            await _cache.RemoveAsync(cacheKey);

            return RepositoryActionStatus.Success;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to delete plugin state. PluginId={PluginId}",
                key
            );

            return RepositoryActionStatus.Error;
        }
    }
}