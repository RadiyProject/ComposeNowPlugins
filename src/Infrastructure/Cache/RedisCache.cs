using System.Text.Json;
using StackExchange.Redis;

namespace ComposeNowPlugins.Infrastructure.Cache;

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

        await _database.StringSetAsync(
            key,
            serializedValue,
            expiry,
            When.Always
        );
    }

    public async Task<bool> SetIfNotExistsAsync<T>(
        string key,
        T value,
        TimeSpan? expiry = null
    )
    {
        string serializedValue = JsonSerializer.Serialize(value, JsonOptions);

        return await _database.StringSetAsync(
            key,
            serializedValue,
            expiry,
            When.NotExists
        );
    }

    public async Task RemoveAsync(string key)
    {
        await _database.KeyDeleteAsync(key);
    }

    public async Task<bool> RefreshAsync(string key, TimeSpan ttl)
    {
        return await _database.KeyExpireAsync(
            key,
            ttl
        );
    }
}
