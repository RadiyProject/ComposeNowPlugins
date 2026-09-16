using ComposeNowPlugins.Application.Exceptions;
using ComposeNowPlugins.Application.Repositories.Plugins;
using ComposeNowPlugins.Domain.Factories;
using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Application.Services.Plugins;

public sealed class PluginSessionService(
    IPluginCatalog pluginCatalog,
    IPluginRepository pluginRepository,
    PluginFactory pluginFactory
) : IPluginSessionService
{
    private readonly IPluginCatalog _pluginCatalog = pluginCatalog;
    private readonly IPluginRepository _pluginRepository = pluginRepository;
    private readonly PluginFactory _pluginFactory = pluginFactory;

    public async Task<Plugin> GetOrCreateAsync(
        PluginId pluginId,
        string pluginName
    )
    {
        Plugin? existing = await _pluginRepository.GetAsync(pluginId);
        if (existing is not null)
        {
            if (!string.Equals(
                existing.Descriptor.Name,
                pluginName,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                throw new EntityAlreadyExistsException(
                    $"PluginId={pluginId} is already assigned to plugin '{existing.Descriptor.Name}'."
                );
            }

            return existing;
        }

        var descriptor = _pluginCatalog.GetRequired(pluginName)
            ?? throw new EntityNotFoundException(
                $"Plugin '{pluginName}' is not registered or disabled."
            );

        Plugin plugin = _pluginFactory.Create(pluginId, descriptor);

        try
        {
            await _pluginRepository.AddAsync(plugin);
            return plugin;
        }
        catch (EntityAlreadyExistsException)
        {
            return await _pluginRepository.GetAsync(pluginId)
                ?? throw new RepositoryException(
                    $"Plugin was created concurrently but cannot be loaded. PluginId={pluginId}"
                );
        }
    }
}
