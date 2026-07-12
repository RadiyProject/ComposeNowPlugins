namespace ComposeNowPlugins.Proxy.Transports;

public sealed record PluginEventInput(
    int Type,
    int Pitch,
    float Velocity,
    int Offset
);
