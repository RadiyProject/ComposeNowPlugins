namespace ComposeNowPlugins.Worker.Configurations;

public sealed record VstEnginePoolOptions(
    int MaxTotalInstances,
    int MaxInstancesPerPlugin,
    int MaxStateBytes,
    TimeSpan IdleTimeout,
    TimeSpan SweepInterval
)
{
    public static VstEnginePoolOptions FromConfiguration(IConfiguration configuration)
    {
        int defaultConcurrency = Math.Max(1, Environment.ProcessorCount);
        int maxTotalInstances = ReadPositiveInt(
            configuration,
            "VST_ENGINE_POOL_MAX_TOTAL_INSTANCES",
            defaultConcurrency
        );
        int maxInstancesPerPlugin = ReadPositiveInt(
            configuration,
            "VST_ENGINE_POOL_MAX_INSTANCES_PER_PLUGIN",
            maxTotalInstances
        );
        if (maxInstancesPerPlugin > maxTotalInstances)
        {
            throw new InvalidOperationException(
                "VST_ENGINE_POOL_MAX_INSTANCES_PER_PLUGIN cannot exceed VST_ENGINE_POOL_MAX_TOTAL_INSTANCES."
            );
        }

        return new VstEnginePoolOptions(
            maxTotalInstances,
            maxInstancesPerPlugin,
            ReadPositiveInt(configuration, "VST_ENGINE_MAX_STATE_BYTES", 64 * 1024 * 1024),
            TimeSpan.FromSeconds(ReadPositiveInt(configuration, "VST_ENGINE_POOL_IDLE_SECONDS", 120)),
            TimeSpan.FromSeconds(ReadPositiveInt(configuration, "VST_ENGINE_POOL_SWEEP_SECONDS", 30))
        );
    }

    private static int ReadPositiveInt(
        IConfiguration configuration,
        string key,
        int defaultValue
    )
    {
        string? value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out int result) || result <= 0)
        {
            throw new InvalidOperationException($"Configuration value '{key}' must be a positive integer.");
        }

        return result;
    }
}
