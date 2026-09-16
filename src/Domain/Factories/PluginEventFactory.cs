using ComposeNowPlugins.Domain.Configurations;
using ComposeNowPlugins.Domain.Models;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Domain.Factories;

public sealed class PluginEventFactory : ModelFactory<PluginEvent, PluginEventId>
{
    public PluginEvent NoteOn(
        PluginId pluginId,
        int pitch,
        float velocity,
        ulong? epoch = null,
        ulong? seq = null,
        int offset = 0,
        int? blockFrames = null
    )
    {
        ValidatePitch(pitch);
        ValidateVelocity(velocity);
        ValidateBlockPosition(offset, blockFrames);

        return Create(id => new PluginEvent
        {
            Id = id,
            PluginId = pluginId,
            Type = PluginEventType.NoteOn,
            Epoch = epoch,
            Seq = seq,
            BlockFrames = blockFrames,
            Pitch = pitch,
            Velocity = velocity,
            Offset = offset
        });
    }

    public PluginEvent NoteOff(
        PluginId pluginId,
        int pitch,
        ulong? epoch = null,
        ulong? seq = null,
        int offset = 0,
        int? blockFrames = null
    )
    {
        ValidatePitch(pitch);
        ValidateBlockPosition(offset, blockFrames);

        return Create(id => new PluginEvent
        {
            Id = id,
            PluginId = pluginId,
            Type = PluginEventType.NoteOff,
            Epoch = epoch,
            Seq = seq,
            BlockFrames = blockFrames,
            Pitch = pitch,
            Offset = offset
        });
    }

    public PluginEvent Parameter(
        PluginId pluginId,
        uint parameterId,
        float value
    )
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return Create(id => new PluginEvent
        {
            Id = id,
            PluginId = pluginId,
            Type = PluginEventType.Param,
            ParameterId = parameterId,
            ParameterValue = value
        });
    }

    public PluginEvent Panic(PluginId pluginId)
    {
        return Create(id => new PluginEvent
        {
            Id = id,
            PluginId = pluginId,
            Type = PluginEventType.Panic
        });
    }

    protected override PluginEventId CreateNewId()
    {
        return PluginEventId.New();
    }

    private static void ValidatePitch(int pitch)
    {
        if (pitch is < 0 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(pitch));
        }
    }

    private static void ValidateVelocity(float velocity)
    {
        if (!float.IsFinite(velocity) || velocity is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(velocity));
        }
    }

    private static void ValidateBlockPosition(int offset, int? blockFrames)
    {
        if (offset < 0 ||
            (blockFrames.HasValue &&
                (blockFrames.Value <= 0 || offset >= blockFrames.Value)))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
    }
}
