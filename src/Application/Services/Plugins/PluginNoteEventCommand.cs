namespace ComposeNowPlugins.Application.Services.Plugins;

public sealed record PluginNoteEventCommand(
    int Pitch,
    float Velocity,
    int Offset,
    bool IsNoteOff
);
