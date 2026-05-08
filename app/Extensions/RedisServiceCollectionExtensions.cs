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

            return ConnectionMultiplexer.Connect(new ConfigurationOptions
            {
                EndPoints = { "redis:6379" },
                Password = redisPassword,
                AbortOnConnectFail = false
            });
        });

        services.AddScoped<ICache, RedisCache>();

        return services;
    }
}