using ComposeNowPlugins.Domain.Configurations;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Domain.Models;

public sealed class PluginEvent
{
    public required PluginEventId Id { get; init; }
    public required PluginId PluginId { get; init; }
    public required PluginEventType Type { get; init; }
    public ulong? Seq { get; init; }
    public int? BlockFrames { get; init; }
    public int? Pitch { get; init; }
    public float? Velocity { get; init; }
    public uint? ParameterId { get; init; }
    public float? ParameterValue { get; init; }
    public int Offset { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static PluginEvent NoteOn(
        PluginId pluginId,
        int pitch,
        float velocity,
        ulong? seq = null,
        int offset = 0,
        int? blockFrames = null
    )
    {
        return new PluginEvent
        {
            Id = PluginEventId.New(),
            PluginId = pluginId,
            Type = PluginEventType.NoteOn,
            Seq = seq,
            Pitch = pitch,
            Velocity = velocity,
            Offset = offset,
            BlockFrames = blockFrames
        };
    }

    public static PluginEvent NoteOff(
        PluginId pluginId,
        int pitch,
        ulong? seq = null,
        int offset = 0,
        int? blockFrames = null
    )
    {
        return new PluginEvent
        {
            Id = PluginEventId.New(),
            PluginId = pluginId,
            Type = PluginEventType.NoteOff,
            Seq = seq,
            Pitch = pitch,
            Offset = offset,
            BlockFrames = blockFrames
        };
    }

    public static PluginEvent Param(
        PluginId pluginId,
        uint parameterId,
        float value
    )
    {
        return new PluginEvent
        {
            Id = PluginEventId.New(),
            PluginId = pluginId,
            Type = PluginEventType.Param,
            ParameterId = parameterId,
            ParameterValue = value
        };
    }

    public static PluginEvent Panic(PluginId pluginId)
    {
        return new PluginEvent
        {
            Id = PluginEventId.New(),
            PluginId = pluginId,
            Type = PluginEventType.Panic
        };
    }
}