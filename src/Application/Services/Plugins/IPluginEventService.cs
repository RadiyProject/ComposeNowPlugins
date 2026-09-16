using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Application.Services.Plugins;

public interface IPluginEventService
{
    Task AddNoteOnControlAsync(PluginId pluginId, int pitch, float velocity);
    Task AddNoteOffControlAsync(PluginId pluginId, int pitch);
    Task AddParameterControlAsync(PluginId pluginId, uint parameterId, float value);
    Task AddPanicControlAsync(PluginId pluginId);

    Task<PluginEventDispatchResult> DispatchBlockNoteAsync(
        PluginId pluginId,
        ulong epoch,
        ulong seq,
        int blockFrames,
        PluginNoteEventCommand command,
        bool isLate,
        bool offline
    );
}
