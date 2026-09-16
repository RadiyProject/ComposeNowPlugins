using ComposeNowPlugins.Domain.Configurations;
using ComposeNowPlugins.Domain.Models.Ids;

namespace ComposeNowPlugins.Domain.Models;

public sealed class PluginEvent : Model<PluginEventId>
{
    public required PluginId PluginId { get; init; }
    public required PluginEventType Type { get; init; }
    public ulong? Epoch { get; init; }
    public ulong? Seq { get; init; }
    public int? BlockFrames { get; init; }
    public int? Pitch { get; init; }
    public float? Velocity { get; init; }
    public uint? ParameterId { get; init; }
    public float? ParameterValue { get; init; }
    public int Offset { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
