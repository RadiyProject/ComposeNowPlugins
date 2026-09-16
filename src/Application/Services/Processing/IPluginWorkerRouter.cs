using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Application.Services.Processing;

public interface IPluginWorkerRouter
{
    Task<PluginWorkerEndpoint> ResolveAsync(
        PluginId pluginId,
        IReadOnlySet<string>? excludedWorkerIds = null
    );

    Task InvalidateOwnershipAsync(
        PluginId pluginId,
        string workerId
    );
}
