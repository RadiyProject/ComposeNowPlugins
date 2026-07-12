using System.Text.Json;
using StackExchange.Redis;

namespace ComposeNowPlugins.Cache;

public class RedisCache(IConnectionMultiplexer connectionMultiplexer) : ICache
{
    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task<T?> GetAsync<T>(string key)
    {
        RedisValue value = await _database.StringGetAsync(key);
        if (value.IsNullOrEmpty)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(value!, JsonOptions);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        string serializedValue = JsonSerializer.Serialize(value, JsonOptions);

        await _database.StringSetAsync(key, serializedValue);
        await _database.KeyExpireAsync(key, expiry);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        return await _database.KeyExistsAsync(key);
    }

    public async Task RemoveAsync(string key)
    {
        await _database.KeyDeleteAsync(key);
    }

    public async Task ListRightPushAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        string serializedValue = JsonSerializer.Serialize(value, JsonOptions);
        await _database.ListRightPushAsync(key, serializedValue);

        if (expiry.HasValue)
        {
            await _database.KeyExpireAsync(key, expiry);
        }
    }

    public async Task<long> ListLengthAsync(string key)
    {
        return await _database.ListLengthAsync(key);
    }

    public async Task<IReadOnlyList<T>> ListLeftPopAsync<T>(string key, long count)
    {
        RedisValue[] values = await _database.ListLeftPopAsync(
            key,
            count
        );

        List<T> result = [];

        foreach (RedisValue value in values)
        {
            if (value.IsNullOrEmpty)
            {
                continue;
            }

            T? item = JsonSerializer.Deserialize<T>(
                value!,
                JsonOptions
            );

            if (item is not null)
            {
                result.Add(item);
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<T>> ListRangeAsync<T>(string key, long start = 0, long stop = -1)
    {
        RedisValue[] values = await _database.ListRangeAsync(key,start, stop);

        List<T> result = [];
        foreach (RedisValue value in values)
        {
            if (value.IsNullOrEmpty)
            {
                continue;
            }

            T? item = JsonSerializer.Deserialize<T>(value!, JsonOptions);

            if (item is not null)
            {
                result.Add(item);
            }
        }

        return result;
    }

    public async Task<bool> LockTakeAsync(string key, string value, TimeSpan expiry)
    {
        return await _database.LockTakeAsync(key, value, expiry);
    }

    public async Task<bool> LockReleaseAsync(string key, string value)
    {
        return await _database.LockReleaseAsync(key, value);
    }

    public async Task<bool> RefreshAsync(string key, TimeSpan ttl)
    {
        bool exists = await _database.KeyExistsAsync(key);
        if (!exists)
        {
            return false;
        }

        return await _database.KeyExpireAsync(
            key,
            ttl
        );
    }
}