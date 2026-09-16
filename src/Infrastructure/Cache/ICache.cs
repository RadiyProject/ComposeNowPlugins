namespace ComposeNowPlugins.Infrastructure.Cache;

public interface ICache
{
    public Task<T?> GetAsync<T>(string key);
    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
    public Task<bool> SetIfNotExistsAsync<T>(string key, T value, TimeSpan? expiry = null);
    public Task RemoveAsync(string key);
    public Task<bool> RefreshAsync(string key, TimeSpan ttl);
}
