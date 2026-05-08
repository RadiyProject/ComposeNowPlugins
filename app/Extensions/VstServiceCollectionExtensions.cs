using ComposeNowPlugins.Wrappers;

namespace ComposeNowPlugins.Extensions;

public static class VstServiceCollectionExtensions
{
    public static IServiceCollection AddVstProcessing(
        this IServiceCollection services
    )
    {
        services.AddScoped<IVstEngineFactory, VstEngineFactory>();

        return services;
    }
}