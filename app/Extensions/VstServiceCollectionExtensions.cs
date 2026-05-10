using ComposeNowPlugins.Services.Processing;
using ComposeNowPlugins.Wrappers;

namespace ComposeNowPlugins.Extensions;

public static class VstServiceCollectionExtensions
{
    public static IServiceCollection AddVstProcessing(
        this IServiceCollection services
    )
    {
        services.AddSingleton<IVstEngineFactory, VstEngineFactory>();
        services.AddScoped<IPluginBlockProcessor, PluginBlockProcessor>();
        services.AddSingleton<IPluginProcessingGate, PluginProcessingGate>();

        return services;
    }
}