namespace ComposeNowPlugins.Cache;

public interface ICache
{
    public Task<T?> GetAsync<T>(string key);
    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
    public Task<bool> ExistsAsync(string key);
    public Task RemoveAsync(string key);
    public Task ListRightPushAsync<T>(string key, T value, TimeSpan? expiry = null);
    public Task<IReadOnlyList<T>> ListLeftPopAsync<T>(string key, long count);
    public Task<IReadOnlyList<T>> ListRangeAsync<T>(string key, long start = 0, long stop = -1);
    public Task<bool> LockTakeAsync(string key, string value, TimeSpan expiry);
    public Task<bool> LockReleaseAsync(string key, string value);
    public Task<long> ListLengthAsync(string key);
    public Task<bool> RefreshAsync(string key, TimeSpan ttl);
}