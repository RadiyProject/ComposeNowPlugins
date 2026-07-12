using ComposeNowPlugins.Application.Services.Processing;
using ComposeNowPlugins.Worker.Services.Processing;
using ComposeNowPlugins.Worker.Wrappers;

namespace ComposeNowPlugins.Worker.Extensions;

public static class VstServiceCollectionExtensions
{
    public static IServiceCollection AddVstProcessing(
        this IServiceCollection services
    )
    {
        services.AddSingleton<IVstEngineFactory, VstEngineFactory>();
        services.AddScoped<IPluginBlockProcessor, PluginBlockProcessor>();
        services.AddSingleton<IPluginProcessingGate, PluginProcessingGate>();
        services.AddSingleton<IPluginEventMerger, PluginEventMerger>();
        services.AddSingleton<IPluginDefaultParameterProvider, PluginDefaultParameterProvider>();
        services.AddSingleton<IPluginEventApplier, PluginEventApplier>();
        services.AddSingleton<IAudioSilenceDetector, AudioSilenceDetector>();

        return services;
    }
}
