namespace ComposeNowPlugins.Domain.Configurations;

public sealed class PluginDescriptor
{
    public required PluginType Type { get; init; }
    public required string Name { get; init; }
    public required string PluginPath { get; init; }
    public bool Enabled { get; init; } = true;
}