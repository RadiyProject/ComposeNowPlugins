namespace ComposeNowPlugins.Cache;

public interface ICache
{
    public Task<T?> GetAsync<T>(string key);
    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
    public Task<bool> ExistsAsync(string key);
    public Task RemoveAsync(string key);
}