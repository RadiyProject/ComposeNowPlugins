using ComposeNowPlugins.Cache;
using StackExchange.Redis;

namespace ComposeNowPlugins.Extensions;

public static class RedisServiceCollectionExtensions
{
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services
    )
    {
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var redisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD");
            var redisHost = Environment.GetEnvironmentVariable("PLUGINS_REDIS_HOST") ?? "redis";
            int timeoutMs = ReadPositiveInt("PLUGINS_REDIS_TIMEOUT_MS", 15000);

            return ConnectionMultiplexer.Connect(new ConfigurationOptions
            {
                EndPoints = { $"{redisHost}:6379" },
                Password = redisPassword,
                AbortOnConnectFail = false,
                ConnectRetry = 3,
                ConnectTimeout = timeoutMs,
                AsyncTimeout = timeoutMs,
                SyncTimeout = timeoutMs
            });
        });

        services.AddScoped<ICache, RedisCache>();

        return services;
    }

    private static int ReadPositiveInt(string key, int fallback)
    {
        string? raw = Environment.GetEnvironmentVariable(key);
        return int.TryParse(raw, out int value) && value > 0
            ? value
            : fallback;
    }
}
