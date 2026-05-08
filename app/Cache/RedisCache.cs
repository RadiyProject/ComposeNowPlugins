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
}