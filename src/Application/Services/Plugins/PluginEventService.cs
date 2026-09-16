using ComposeNowPlugins.Application.Repositories.Plugins;
using ComposeNowPlugins.Domain.Factories;
using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Application.Services.Plugins;

public sealed class PluginEventService(
    IPluginEventRepository pluginEventRepository,
    PluginEventFactory pluginEventFactory
) : IPluginEventService
{
    private readonly IPluginEventRepository _pluginEventRepository = pluginEventRepository;
    private readonly PluginEventFactory _pluginEventFactory = pluginEventFactory;

    public Task AddNoteOnControlAsync(
        PluginId pluginId,
        int pitch,
        float velocity
    )
    {
        return AddControlAsync(
            pluginId,
            _pluginEventFactory.NoteOn(pluginId, pitch, velocity)
        );
    }

    public Task AddNoteOffControlAsync(PluginId pluginId, int pitch)
    {
        return AddControlAsync(
            pluginId,
            _pluginEventFactory.NoteOff(pluginId, pitch)
        );
    }

    public Task AddParameterControlAsync(
        PluginId pluginId,
        uint parameterId,
        float value
    )
    {
        return AddControlAsync(
            pluginId,
            _pluginEventFactory.Parameter(pluginId, parameterId, value)
        );
    }

    public Task AddPanicControlAsync(PluginId pluginId)
    {
        return AddControlAsync(
            pluginId,
            _pluginEventFactory.Panic(pluginId)
        );
    }

    public async Task<PluginEventDispatchResult> DispatchBlockNoteAsync(
        PluginId pluginId,
        ulong epoch,
        ulong seq,
        int blockFrames,
        PluginNoteEventCommand command,
        bool isLate,
        bool offline
    )
    {
        if (isLate && offline)
        {
            return PluginEventDispatchResult.Dropped;
        }

        PluginEvent pluginEvent = command.IsNoteOff
            ? _pluginEventFactory.NoteOff(
                pluginId,
                command.Pitch,
                epoch,
                seq,
                command.Offset,
                blockFrames
            )
            : _pluginEventFactory.NoteOn(
                pluginId,
                command.Pitch,
                command.Velocity,
                epoch,
                seq,
                command.Offset,
                blockFrames
            );

        if (isLate)
        {
            await AddControlAsync(pluginId, pluginEvent);
            return PluginEventDispatchResult.ControlQueue;
        }

        await _pluginEventRepository.AddBlockEventAsync(
            pluginId,
            epoch,
            seq,
            pluginEvent
        );

        return PluginEventDispatchResult.BlockQueue;
    }

    private Task AddControlAsync(PluginId pluginId, PluginEvent pluginEvent)
    {
        return _pluginEventRepository.AddControlEventAsync(
            pluginId,
            pluginEvent
        );
    }
}
