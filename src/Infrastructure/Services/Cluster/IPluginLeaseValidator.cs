namespace ComposeNowPlugins.Infrastructure.Services.Cluster;

public interface IPluginLeaseValidator
{
    Task<bool> ValidateAsync(
        string leaseId,
        string pluginName,
        CancellationToken cancellationToken
    );
}

